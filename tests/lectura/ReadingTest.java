package com.loklj.folioxr;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Contratos independientes de Android: geometria, TOC y nombres; no acredita SQLite/WebView. */
public final class ReadingTest {
    static int checks;
    static void check(boolean b,String why){if(!b)throw new AssertionError(why);checks++;}
    static final String ROOT="tests/fixtures/lectura/";
    public static void main(String[] args)throws Exception {
        for(int font=0;font<2;font++)for(float line:new float[]{1.2f,1.4f,1.8f})for(int margin:new int[]{24,48,72}){
            FolioReadingSettings s=new FolioReadingSettings(font,line,margin);String css=s.css(1f);
            check(css.contains("column-width:"+(596-2*margin)+"px")&&css.contains("column-gap:"+(margin*2)+"px"),"Paso de columna");
            check(css.contains("line-height:"+line+"!important")&&css.contains(s.family()+"-BoldItalic.ttf"),"Interlineado y familia");
        }
        check(new FolioReadingSettings(99,Float.NaN,-2).equals(new FolioReadingSettings(0,1.4f,48)),"Preferencias invalidas");
        check(!FolioReadingSettings.fontAsset("folio-fonts/../secret.ttf"),"Lista cerrada fuentes");check(FolioReadingSettings.fontAsset("folio-fonts/sans-Regular.ttf"),"Fuente incluida");
        check(FolioReadingSettings.label("  Nota\ncon\ttexto  ").equals("Nota con texto"),"Normalizar nombre");
        check(FolioReadingSettings.label(null).isEmpty(),"Nombre antiguo nulo");
        String longName=String.join("",Collections.nCopies(121,"\ud83d\ude00"));check(FolioReadingSettings.label(longName).length()==240,"Nombre Unicode sin cortar pares");
        check(FolioContents.quote("a\"\n\\").equals("\"a\\\"\\u000a\\\\\""),"Escapado JSON/JS");
        for(String f:new String[]{"indice.epub","indice-ncx.epub"})try(FolioEpubContainer c=new FolioEpubContainer(ROOT+f)){
            List<FolioContents.Entry> toc=c.contents();check(toc.size()==2,"Solo TOC: "+f);
            check(toc.get(0).chapter==0&&toc.get(1).chapter==0,"Destinos en el mismo capitulo");
            check(toc.get(0).fragment.equals("uno")&&toc.get(1).fragment.equals("dos"),"Conservar fragmentos");
            check(toc.get(0).depth==0&&toc.get(1).depth==1,"Jerarquia nav/NCX");
            check(toc.get(0).title.startsWith("Primero")&&!toc.get(0).title.contains("Segundo"),"Etiqueta propia");
        }
        for(String f:new String[]{"indice.mobi","indice-html.mobi"})try(FolioMobiSource c=new FolioMobiSource(ROOT+f)){
            List<FolioContents.Entry> toc=c.contents();check(toc.size()==1,"Indice MOBI: "+f);check(toc.get(0).chapter==0,"Capitulo MOBI");
            String html=new String(c.chapter(0),StandardCharsets.UTF_8);check(html.contains("id=\""+toc.get(0).fragment+"\""),"Ancla sintetica");
            check(html.indexOf("id=\""+toc.get(0).fragment)>html.indexOf("<h2>"),"Ancla en destino real");
        }
        for(String f:new String[]{"indice.pdf","indice-stream.pdf"})try(FolioPdfOutline c=new FolioPdfOutline(ROOT+f,()->false)){
            List<FolioContents.Entry> toc=c.read();check(toc.size()==3,"Marcadores PDF: "+f);
            check(toc.get(0).page==0&&toc.get(1).page==1&&toc.get(2).page==1,"Paginas PDF reales/nombradas");
            check(toc.get(1).depth==1&&toc.get(1).title.equals("Cap\u00edtulo"),"UTF16 y jerarquia PDF");
            check(FolioContents.json(toc).contains("\"page\":1"),"Protocolo paginado");
        }
        try(FolioMobiSource c=new FolioMobiSource(ROOT+"indice.azw3")){
            check(c.contents().size()==1,"TOC KF8");String h=new String(c.chapter(0),StandardCharsets.UTF_8);
            check(h.contains("<p><span id=\"folio-pos-0\"></span>KF8"),"Destino dentro de fragmento KF8");
        }
        try(FolioPdfOutline c=new FolioPdfOutline(ROOT+"indice-incremental.pdf",()->false)){
            check(c.read().get(1).title.equals("Revisado"),"Ultima revision xref prevalece");
        }
        // Archivo disperso temporal: offsets mayores que 4 GiB, sin almacenar 5 GiB de datos.
        Path sparse=Files.createTempFile("folio-outline-large-", ".pdf");long shift=5L*1024*1024*1024;
        try{
            String original=new String(Files.readAllBytes(Paths.get(ROOT+"indice.pdf")),StandardCharsets.ISO_8859_1);
            java.util.regex.Matcher x=java.util.regex.Pattern.compile("(?m)^(\\d{10}) (\\d{5}) n").matcher(original);StringBuffer changed=new StringBuffer();
            while(x.find())x.appendReplacement(changed,String.format(java.util.Locale.ROOT,"%010d %s n",Long.parseLong(x.group(1))+shift,x.group(2)));x.appendTail(changed);
            String data=changed.toString();int at=data.lastIndexOf("startxref\n")+10;long offset=Long.parseLong(data.substring(at).split("\\s+")[0]);data=data.substring(0,at)+(offset+shift)+"\n%%EOF\n";
            try(RandomAccessFile f=new RandomAccessFile(sparse.toFile(),"rw")){f.write("%PDF-1.7\n".getBytes(StandardCharsets.US_ASCII));f.seek(shift);f.write(data.getBytes(StandardCharsets.ISO_8859_1));}
            try(FolioPdfOutline c=new FolioPdfOutline(sparse.toString(),()->false)){check(c.read().get(2).page==1,"Offsets PDF de 64 bits");}
        }finally{Files.delete(sparse);}
        for(String f:new String[]{"ciclo.pdf","cifrado-metadata.pdf","truncado.pdf"}){
            try(FolioPdfOutline c=new FolioPdfOutline(ROOT+f,()->false)){c.read();throw new AssertionError("Aceptado "+f);}catch(IOException e){check(e.getMessage().contains("Indice PDF"),"Diagnostico PDF");}
        }
        try(FolioPdfOutline c=new FolioPdfOutline(ROOT+"indice.pdf",()->true)){c.read();throw new AssertionError("No cancelado");}catch(InterruptedIOException expected){checks++;}
        if(args.length>0){Path dir=Paths.get(args[0]);Files.createDirectories(dir);
            int i=0;for(FolioReadingSettings s:new FolioReadingSettings[]{new FolioReadingSettings(0,1.4f,48),new FolioReadingSettings(1,1.4f,48),new FolioReadingSettings(0,1.8f,72),new FolioReadingSettings(0,1.2f,24)}){
                StringBuilder content=new StringBuilder();for(int p=0;p<80;p++)content.append("<p id='p").append(p).append("'>").append(p).append(". Texto original de lectura: acci\u00f3n, ni\u00f1o y p\u00e1ginas con <b>negrita</b> e <i>it\u00e1lica</i>. ").append(String.join("",Collections.nCopies(6,"Leer en el libro virtual. "))).append("</p>");
                Files.write(dir.resolve("layout"+(i++)+".html"),("<!doctype html><html><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; img-src https://folio.invalid data:; style-src 'unsafe-inline' https://folio.invalid; font-src https://folio.invalid/folio-fonts/; script-src 'none';\"></head><body>"+content+s.css(1f)+"</body></html>").getBytes(StandardCharsets.UTF_8));
            }
        }
        System.out.println("OK: "+checks+" comprobaciones JVM de lectura");
    }
}
