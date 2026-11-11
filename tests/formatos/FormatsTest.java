package com.loklj.folioxr;
import java.io.*;
import java.nio.charset.*;
import java.util.*;
import java.util.zip.*;
public final class FormatsTest {
    static int checks;
    static void ok(boolean condition){if(!condition)throw new AssertionError("Prueba "+checks);checks++;}
    static String html(FolioHtmlSource source,int n)throws Exception{return new String(source.chapter(n),StandardCharsets.UTF_8);}
    public static void main(String[] args)throws Exception{
        File root=new File("tests/fixtures/formatos");
        for(String name:new String[]{"propio.mobi","palmdoc.azw","huff.mobi"})try(FolioMobiSource s=new FolioMobiSource(new File(root,name).getPath())){
            ok(s.chapterCount()==1);ok(html(s,0).contains("espa\u00f1ol"));ok(html(s,0).contains("https://folio.invalid/image/1"));ok(s.resource("image/1",1000)[0]==(byte)137);
        }
        try(FolioMobiSource s=new FolioMobiSource(new File(root,"kf8.azw3").getPath())){
            String h=html(s,0);ok(h.contains("<body><p>KF8 propio"));ok(h.contains("coraz\u00f3n"));ok(h.contains("https://folio.invalid/image/1"));ok(h.endsWith("</body></html>"));
        }
        try{new FolioMobiSource(new File(root,"cifrado-sintetico.mobi").getPath());throw new AssertionError();}catch(FolioEpubContainer.DrmException expected){checks++;}
        try{new FolioMobiSource(new File(root,"truncado.mobi").getPath());throw new AssertionError();}catch(IOException expected){checks++;}
        ok(new String(FolioMobiCodec.palm(new byte[]{'a','b',(byte)128,19}),StandardCharsets.US_ASCII).equals("abababab"));
        try{FolioMobiCodec.palm(new byte[]{(byte)128,0});throw new AssertionError();}catch(IOException expected){checks++;}
        for(String name:new String[]{"texto-utf-16le.txt","texto-utf-16be.txt","texto-iso-8859-1.txt"})try(FolioTxtSource s=new FolioTxtSource(new File(root,name).getPath(),"auto")){ok(html(s,0).contains("Acci\u00f3n y ni\u00f1o"));}
        for(String name:new String[]{"texto-utf8.txt","texto-utf8-bom.txt","texto-windows1252.txt","texto-utf16le-bom.txt"})try(FolioTxtSource s=new FolioTxtSource("tests/fixtures/"+name,"auto")){ok(s.chapterCount()>0);ok(!html(s,0).contains("\ufffd"));}
        try { new FolioTxtSource("tests/fixtures/texto-windows1252.txt", "UTF-8"); throw new AssertionError(); }
        catch(java.nio.charset.CharacterCodingException expected) { checks++; }
        try { new FolioTxtSource("tests/fixtures/texto-utf8.txt", "auto", () -> true); throw new AssertionError(); }
        catch(InterruptedIOException expected) { checks++; }
        try { new FolioMobiSource(new File(root,"propio.mobi").getPath(), () -> true); throw new AssertionError(); }
        catch(InterruptedIOException expected) { checks++; }
        int repetitions=args.length>0 && args[0].equals("--large") ? 12000000 : 300000;
        File big=File.createTempFile("folio-big-", ".txt");
        try{
            String paragraph="<Texto> con & y \u00f1.\n";byte[] bytes=paragraph.getBytes(StandardCharsets.UTF_8);
            try(OutputStream out=new BufferedOutputStream(new FileOutputStream(big))){for(int i=0;i<repetitions;i++)out.write(bytes);}
            try(FolioTxtSource s=new FolioTxtSource(big.getPath(),"auto")){
                ok(s.chapterCount()>20);long occurrences=0;
                for(int i=0;i<s.chapterCount();i++){String h=html(s,i);ok(h.contains("&lt;Texto&gt; con &amp;"));occurrences+=h.split("&lt;Texto&gt;",-1).length-1;}
                ok(occurrences==repetitions);
            }
        }finally{big.delete();}
        ok(FolioCbrArchive.kind(new File(root,"zip-disfrazado.cbr")).equals("zip"));
        try{FolioCbrArchive.prepare(new File(root,"rar5-firma.cbr"),()->false);throw new AssertionError();}catch(IOException expected){ok(expected.getMessage().equals("CBR (RAR5) a\u00fan no compatible: convi\u00e9rtelo a CBZ"));}
        File converted=FolioCbrArchive.prepare(new File(root,"rar4-almacenado.cbr"),()->false);
        try(ZipFile zip=new ZipFile(converted)){ok(zip.size()==1);ok(zip.entries().nextElement().getSize()>0);}finally{converted.delete();}
        System.out.println("OK: "+checks+" comprobaciones de formatos");
    }
}
