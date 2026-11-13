package com.loklj.folioxr;

import java.io.*;
import java.nio.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicLong;

/** Archivos dispersos reales: tamano logico de hasta 8 GiB, sin reservar ese disco. */
public final class LargeFilesTest {
    static int checks;
    static void check(boolean ok){if(!ok)throw new AssertionError("Prueba "+checks);checks++;}
    interface Checked {void run()throws Exception;}
    static void fails(Checked action)throws Exception{try{action.run();throw new AssertionError("Se acepto entrada invalida");}catch(IOException expected){checks++;}}
    static void raw(File file,int w,int h)throws Exception{try(RandomAccessFile f=new RandomAccessFile(file,"rw")){
        ByteBuffer b=ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN);b.put(new byte[]{'F','X','R','1'}).putInt(w).putInt(h).putInt(0);f.write(b.array());f.setLength(16L+4L*w*h);
    }}
    public static void main(String[] args)throws Exception{
        Path dir=Files.createTempDirectory("folio-giantes-");
        try{
            for(long n:new long[]{(1L<<31)-1,1L<<31,(1L<<31)+1,(1L<<32)-1,1L<<32,(1L<<32)+1,8L<<30}){
                check(FolioLargeFiles.contentLength(Long.toString(n))==n);
                File sparse=dir.resolve("source.pdf").toFile();
                try(RandomAccessFile f=new RandomAccessFile(sparse,"rw")){f.setLength(n);f.seek(n-8);f.writeLong(n);}
                FolioLargeFiles.verify(sparse,n);checks++;
                try(RandomAccessFile f=new RandomAccessFile(sparse,"r")){f.seek(n-8);check(f.readLong()==n);}
                fails(()->FolioLargeFiles.verify(sparse,n-1));
                // El mismo bucle de copia de produccion procesa todos los bytes logicos.
                // El destino de prueba convierte solo ceros en huecos; no lo hace la app.
                File target=dir.resolve("copied.part").toFile();AtomicLong progress=new AtomicLong();
                try(InputStream in=new FileInputStream(sparse);RandomAccessFile out=new RandomAccessFile(target,"rw");
                    FolioLargeFiles.Lease lease=FolioLargeFiles.reserve(()->Long.MAX_VALUE,n)){
                    OutputStream sink=new OutputStream(){
                        public void write(int b)throws IOException{out.write(b);}
                        public void write(byte[] b,int offset,int length)throws IOException{
                            boolean zero=true;for(int i=offset;i<offset+length;i++)if(b[i]!=0){zero=false;break;}
                            if(zero)out.seek(out.getFilePointer()+length);else out.write(b,offset,length);
                        }};
                    long copied=FolioLargeFiles.copy(in,sink,n,lease,()->false,(done,total)->{if(done<progress.get())throw new AssertionError();progress.set(done);});
                    out.setLength(copied);check(copied==n && progress.get()==n);out.seek(n-8);check(out.readLong()==n);
                }
                Files.delete(target.toPath());Files.delete(sparse.toPath());
            }
            for(String bad:new String[]{"-1","0","+8","1x","9223372036854775808","1,2",""})fails(()->FolioLargeFiles.contentLength(bad));
            check(!FolioLargeFiles.fits(Long.MAX_VALUE,Long.MAX_VALUE));check(FolioLargeFiles.fits(FolioLargeFiles.MARGIN+100,100));
            AtomicLong free=new AtomicLong(FolioLargeFiles.MARGIN+100);
            try(FolioLargeFiles.Lease first=FolioLargeFiles.reserve(free::get,70)){
                fails(()->FolioLargeFiles.reserve(free::get,40));first.written(20);free.addAndGet(-20);fails(()->first.check(90));
            }
            try(FolioLargeFiles.Lease lease=FolioLargeFiles.reserve(()->Long.MAX_VALUE,10)){
                fails(()->FolioLargeFiles.copy(new ByteArrayInputStream(new byte[9]),new ByteArrayOutputStream(),10,lease,()->false,(a,b)->{}));
            }
            try(FolioLargeFiles.Lease lease=FolioLargeFiles.reserve(()->Long.MAX_VALUE,-1)){
                check(FolioLargeFiles.copy(new ByteArrayInputStream(new byte[99]),new ByteArrayOutputStream(),-1,lease,()->false,(a,b)->{})==99);
                fails(()->FolioLargeFiles.copy(new ByteArrayInputStream(new byte[99]),new ByteArrayOutputStream(),-1,lease,()->true,(a,b)->{}));
            }
            try(FolioLargeFiles.Lease lease=FolioLargeFiles.reserve(free::get,-1)){
                free.set(FolioLargeFiles.MARGIN);fails(()->lease.check(1));
            }
            for(int pressure=0;pressure<=2;pressure++)for(int h:new int[]{1,595,14000,Integer.MAX_VALUE}){
                int[] d=FolioRenderPolicy.dimensions(Integer.MAX_VALUE,420,h,FolioRenderPolicy.pixels(pressure));
                check(d[0]>0 && d[1]>0 && d[0]<=8192 && d[1]<=8192 && (long)d[0]*d[1]<=FolioRenderPolicy.pixels(pressure));
            }
            check(FolioRenderPolicy.pressure(0,0,256L<<20)==0);
            check(FolioRenderPolicy.pressure(1200L<<20,0,256L<<20)==1);
            check(FolioRenderPolicy.pressure(1600L<<20,0,256L<<20)==2);
            check(FolioRenderPolicy.pressure(0,260L<<20,256L<<20)==2);
            check(FolioRenderPolicy.acquire(100L<<20));check(!FolioRenderPolicy.acquire(30L<<20));FolioRenderPolicy.release(100L<<20);check(FolioRenderPolicy.acquire(128L<<20));FolioRenderPolicy.release(128L<<20);
            FolioRawCache cache=new FolioRawCache(dir.toFile());
            File source=dir.resolve("test.pdf").toFile();Files.write(source.toPath(),new byte[]{1});
            File name=cache.file(source.getPath(),3,1200);raw(name,100,100);check(FolioRawCache.size(name).equals("100x100"));
            check(!cache.file(source.getPath(),4,1200).equals(name));
            File out=dir.resolve("page.raw").toFile();check(cache.take(name,out));check(!name.exists() && out.isFile());out.delete();
            raw(name,100,100);try(RandomAccessFile f=new RandomAccessFile(name,"rw")){f.setLength(f.length()-1);}check(!cache.take(name,out));
            for(int i=0;i<8;i++){File f=cache.file(source.getPath(),i,4000);raw(f,2000,4000);f.setLastModified(i+1);}
            cache.trim(0);long bytes=0;for(File f:cache.folder.listFiles())bytes+=f.length();check(bytes<=FolioRawCache.LIMIT);
            check(!cache.file(source.getPath(),0,4000).exists());
            fails(()->cache.trim(FolioRawCache.LIMIT+1));
            FolioPerformance.Samples samples=new FolioPerformance.Samples();for(int i=1;i<=100;i++)samples.add(i);
            check(samples.percentile(.5)==50 && samples.percentile(.95)==95 && samples.percentile(.99)==99);
            for(int i=0;i<5000;i++)samples.add(10);check(samples.count==1024 && samples.total==5100 && samples.percentile(.99)==10);
            samples.add(Double.NaN);check(samples.total==5100);
            System.out.println("OK: "+checks+" comprobaciones JVM de rendimiento (copias sparse reales hasta 8 GiB)");
        }finally{try(java.util.stream.Stream<Path> paths=Files.walk(dir)){paths.sorted(Comparator.reverseOrder()).forEach(p->{try{Files.delete(p);}catch(IOException e){throw new RuntimeException(e);}});}}
    }
}
