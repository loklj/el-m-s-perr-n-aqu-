package com.loklj.folioxr;

import java.io.*;
import java.util.function.BooleanSupplier;
import java.util.function.LongSupplier;

/** Transferencias acotadas y reservas compartidas entre SAF, Wi-Fi y otras importaciones. */
final class FolioLargeFiles {
    static final long MARGIN = 128L << 20;
    private static long reserved;
    interface Progress { void update(long copied, long total); }
    static long contentLength(String text) throws IOException {
        if (text == null || !text.matches("[0-9]{1,19}")) throw new IOException("Content-Length no valido");
        try { long n=Long.parseLong(text); if(n<=0)throw new NumberFormatException(); return n; }
        catch(NumberFormatException e){throw new IOException("Content-Length fuera de rango");}
    }
    static boolean fits(long free, long bytes) { return free >= MARGIN && bytes >= 0 && bytes <= free-MARGIN; }
    static synchronized long unreserved(long free){return Math.max(0,free-reserved);}
    static synchronized Lease reserve(LongSupplier space,long expected) throws IOException {
        long n=Math.max(0,expected), free=space.getAsLong();
        if(reserved>free || !fits(free-reserved,n))throw new IOException("No hay espacio suficiente en el visor");
        reserved+=n; return new Lease(space,n);
    }
    static final class Lease implements AutoCloseable {
        final LongSupplier space; long remaining; boolean closed;
        Lease(LongSupplier space,long n){this.space=space;remaining=n;}
        void check(int next) throws IOException { synchronized(FolioLargeFiles.class){
            long free=space.getAsLong(), others=reserved-remaining;
            if(closed || others>free || !fits(free-others,Math.max(remaining,next)))
                throw new IOException("No hay espacio suficiente durante la copia");
        }}
        void written(int n){synchronized(FolioLargeFiles.class){long used=Math.min(remaining,n);remaining-=used;reserved-=used;}}
        public void close(){synchronized(FolioLargeFiles.class){if(!closed){reserved-=remaining;remaining=0;closed=true;}}}
    }
    /** expected=-1 permite SAF sin tamano; HTTP consume exactamente Content-Length. */
    static long copy(InputStream in,OutputStream out,long expected,Lease lease,
                     BooleanSupplier cancel,Progress progress) throws IOException {
        byte[] buffer=new byte[256<<10]; long done=0,last=0;
        while(expected<0 || done<expected){
            if(cancel.getAsBoolean())throw new InterruptedIOException("Copia cancelada");
            int wanted=expected<0?buffer.length:(int)Math.min(buffer.length,expected-done);
            int n=in.read(buffer,0,wanted);
            if(n<0)break;
            if(n==0){int b=in.read();if(b<0)break;buffer[0]=(byte)b;n=1;}
            if(done>Long.MAX_VALUE-n)throw new IOException("Tamano fuera de rango");
            lease.check(n);out.write(buffer,0,n);lease.written(n);done+=n;
            long now=System.nanoTime();if(now-last>=250000000L){progress.update(done,expected);last=now;}
        }
        if(cancel.getAsBoolean())throw new InterruptedIOException("Copia cancelada");
        if(done==0 || (expected>=0 && done!=expected))throw new EOFException("La copia quedo incompleta");
        progress.update(done,expected);return done;
    }
    static void verify(File file,long expected) throws IOException {
        if(expected<=0 || !file.isFile() || file.length()!=expected)throw new IOException("Archivo incompleto: tamano distinto al diario");
    }
}
