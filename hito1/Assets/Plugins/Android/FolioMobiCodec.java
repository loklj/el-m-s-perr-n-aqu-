package com.loklj.folioxr;
import java.io.*;
import java.util.*;

/** Decodificadores propios con salida, diccionario y recursion acotados. */
final class FolioMobiCodec {
    static final int LIMIT = 65536;
    static int u16(byte[] b,int p) throws IOException { range(b,p,2); return ((b[p]&255)<<8)|(b[p+1]&255); }
    static long u32(byte[] b,int p) throws IOException { return ((long)u16(b,p)<<16)|u16(b,p+2); }
    static void range(byte[] b,int p,int n) throws IOException { if(p<0 || n<0 || p>b.length-n) throw new IOException("Registro MOBI truncado"); }
    static void magic(byte[] b,int p,String text) throws IOException {
        range(b,p,text.length()); for(int i=0;i<text.length();i++) if(b[p+i]!=text.charAt(i)) throw new IOException("Cabecera MOBI no valida: "+text);
    }
    static byte[] palm(byte[] input) throws IOException {
        byte[] output = new byte[LIMIT]; int p=0,n=0;
        while(p<input.length) {
            int value=input[p++]&255;
            if(value>=1 && value<=8) {
                range(input,p,value); if(n>LIMIT-value) throw new IOException("PalmDOC demasiado grande");
                System.arraycopy(input,p,output,n,value); p+=value; n+=value;
            } else if(value>=128 && value<192) {
                range(input,p,1); int pair=(value<<8)|(input[p++]&255);
                int distance=(pair&16383)>>>3, length=(pair&7)+3;
                if(distance==0 || distance>n || n>LIMIT-length) throw new IOException("Referencia PalmDOC invalida");
                for(int i=0;i<length;i++) { output[n]=output[n-distance]; n++; }
            } else {
                int length=value>=192?2:1; if(n>LIMIT-length) throw new IOException("PalmDOC demasiado grande");
                if(length==2) output[n++]=32;
                output[n++]=(byte)(length==2?value^128:value);
            }
        }
        return Arrays.copyOf(output,n);
    }
    static final class Huff {
        private final long[] minimum=new long[33], maximum=new long[33];
        private final int[] fast=new int[256];
        private final List<byte[]> phrases=new ArrayList<>();
        private final List<Boolean> literal=new ArrayList<>();
        private long stored;
        Huff(byte[] header) throws IOException {
            magic(header,0,"HUFF"); int a=(int)u32(header,8), b=(int)u32(header,12);
            range(header,a,1024); range(header,b,256);
            for(int i=0;i<256;i++) fast[i]=(int)u32(header,a+4*i);
            for(int i=1;i<=32;i++) { minimum[i]=u32(header,b+(i-1)*8); maximum[i]=u32(header,b+(i-1)*8+4); }
        }
        void dictionary(byte[] record) throws IOException {
            magic(record,0,"CDIC"); int count=(int)u32(record,8), bits=(int)u32(record,12);
            if(bits<0 || bits>16 || count<0 || count>65536) throw new IOException("Diccionario HUFF demasiado grande");
            int entries=Math.min(1<<bits,count-phrases.size()); if(entries<0) throw new IOException("CDIC inconsistente");
            for(int i=0;i<entries;i++) {
                int start=16+u16(record,16+2*i), length=u16(record,start);
                boolean raw=(length&32768)!=0; length&=32767; range(record,start+2,length);
                stored+=length; if(stored>16*1024*1024) throw new IOException("Limite de diccionario HUFF");
                phrases.add(Arrays.copyOfRange(record,start+2,start+2+length)); literal.add(raw);
            }
        }
        byte[] decode(byte[] record) throws IOException {
            ByteArrayOutputStream output=new ByteArrayOutputStream(); decode(record,output,new BitSet(),0); return output.toByteArray();
        }
        private void decode(byte[] input,ByteArrayOutputStream output,BitSet visiting,int depth) throws IOException {
            if(depth>32) throw new IOException("HUFF recursivo");
            long bit=0,total=input.length*8L;
            while(bit<total) {
                long look=0;
                for(int j=0;j<32;j++) { long at=bit+j; look=(look<<1)|(at<total?(input[(int)(at/8)]>>(7-(at%8)))&1:0); }
                int first=fast[(int)(look>>>24)], length=first&31;
                if(length==0) throw new IOException("Codigo HUFF sin longitud");
                long max;
                if((first&128)!=0) max=(first>>>8);
                else {
                    while(length<=32 && (look>>>(32-length))<minimum[length]) length++;
                    if(length>32) throw new IOException("Codigo HUFF no valido");
                    max=maximum[length];
                }
                if(bit+length>total) break; // Relleno final hasta el siguiente octeto.
                long symbol=max-(look>>>(32-length)); bit+=length;
                if(symbol<0 || symbol>=phrases.size()) throw new IOException("Indice HUFF fuera de rango");
                int id=(int)symbol;
                if(visiting.get(id)) throw new IOException("Ciclo CDIC");
                byte[] phrase=phrases.get(id);
                if(literal.get(id)) {
                    if(output.size()>LIMIT-phrase.length) throw new IOException("Salida HUFF demasiado grande");
                    output.write(phrase,0,phrase.length);
                } else { visiting.set(id); decode(phrase,output,visiting,depth+1); visiting.clear(id); }
            }
        }
    }
    static byte[] trim(byte[] record,int flags) throws IOException {
        int end=record.length;
        for(int bit=15;bit>=1;bit--) if((flags&(1<<bit))!=0) {
            long size=0; int shift=0,p=end;
            while(true) {
                if(p<=0 || shift>28) throw new IOException("Cola MOBI invalida");
                int b=record[--p]&255; size|=(long)(b&127)<<shift; shift+=7;
                if((b&128)!=0) break;
            }
            if(size<end-p || size>end) throw new IOException("Cola MOBI fuera de rango"); end-=(int)size;
        }
        if((flags&1)!=0) { if(end==0) throw new IOException("Solapamiento MOBI invalido"); end-=(record[end-1]&3)+1; }
        if(end<0) throw new IOException("Registro truncado");
        return Arrays.copyOf(record,end);
    }
}
