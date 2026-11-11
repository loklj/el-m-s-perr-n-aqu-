package com.loklj.folioxr;
import java.io.*;
import java.nio.*;
import java.nio.charset.*;
/** Codigos HUFF cortos/largos, recursion CDIC y limites con datos propios. */
public final class CodecTest {
    static byte[] header(boolean fallback) {
        ByteBuffer b=ByteBuffer.allocate(1296).order(ByteOrder.BIG_ENDIAN);
        b.put("HUFF".getBytes(StandardCharsets.US_ASCII)).putInt(16).putInt(16).putInt(1040);
        for(int i=0;i<256;i++)b.putInt((255<<8)|(fallback?0:128)|8);
        for(int i=1;i<=32;i++)b.putInt(i==8?0:1).putInt(i==8?255:0);
        return b.array();
    }
    static byte[] dictionary(boolean cycle) throws Exception {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();DataOutputStream out=new DataOutputStream(bytes);
        out.writeBytes("CDIC");out.writeInt(16);out.writeInt(256);out.writeInt(8);
        for(int i=0;i<256;i++)out.writeShort(512+3*i);
        for(int i=0;i<256;i++){
            int symbol=255-i;
            out.writeShort(symbol=='A'?1:32769);
            out.writeByte(symbol=='A'?(cycle?'A':'B'):symbol);
        }
        return bytes.toByteArray();
    }
    public static void main(String[] args)throws Exception{
        for(boolean fallback:new boolean[]{true,false}){
            FolioMobiCodec.Huff h=new FolioMobiCodec.Huff(header(fallback));h.dictionary(dictionary(false));
            if(!new String(h.decode(new byte[]{'A','C'}),StandardCharsets.US_ASCII).equals("BC"))throw new AssertionError();
        }
        FolioMobiCodec.Huff cyclic=new FolioMobiCodec.Huff(header(false));cyclic.dictionary(dictionary(true));
        try{cyclic.decode(new byte[]{'A'});throw new AssertionError();}catch(IOException expected){}
        // Cola con longitud VLI y solapamiento UTF-8 eliminados antes de descomprimir.
        byte[] trimmed=FolioMobiCodec.trim(new byte[]{'a','b',0,42,(byte)130},3);
        if(!java.util.Arrays.equals(trimmed,new byte[]{'a','b'}))throw new AssertionError();
        System.out.println("OK: 4 comprobaciones de codificadores");
    }
}
