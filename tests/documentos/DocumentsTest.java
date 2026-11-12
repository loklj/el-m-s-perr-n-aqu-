package com.loklj.folioxr;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.regex.*;
import java.util.zip.*;

/** Pruebas JVM del contrato real: conversion, recursos, limites, cancelacion y datos hostiles. */
public final class DocumentsTest {
    private static int checks;
    private static final Path ROOT=Paths.get("tests/fixtures/documentos");
    private static void check(boolean value,String message){if(!value)throw new AssertionError(message);checks++;}
    private static String html(FolioHtmlSource source)throws Exception {
        StringBuilder b=new StringBuilder();for(int i=0;i<source.chapterCount();i++)b.append(new String(source.chapter(i),StandardCharsets.UTF_8));return b.toString();
    }
    private static void fail(Path path,String expected)throws Exception {
        try(FolioHtmlSource source=FolioHtmlSource.open(path.toString(),"auto")){throw new AssertionError("Aceptado: "+path);}
        catch(Exception e){check(expected==null||messages(e).contains(expected),"Diagnostico: "+e);}
    }
    private static String messages(Throwable e){return e==null?"":String.valueOf(e.getMessage())+messages(e.getCause());}
    private static Map<String,byte[]> parts(String name)throws Exception {
        Map<String,byte[]> result=new LinkedHashMap<>();try(ZipFile zip=new ZipFile(ROOT.resolve(name).toFile())){
            Enumeration<? extends ZipEntry> entries=zip.entries();while(entries.hasMoreElements()){ZipEntry e=entries.nextElement();try(InputStream in=zip.getInputStream(e)){ByteArrayOutputStream b=new ByteArrayOutputStream();byte[] buf=new byte[4096];int n;while((n=in.read(buf))!=-1)b.write(buf,0,n);result.put(e.getName(),b.toByteArray());}}}return result;
    }
    private static Path variant(String suffix,Map<String,byte[]> parts)throws Exception {
        Path p=Files.createTempFile("folio-doc-test-",suffix);try(ZipOutputStream z=new ZipOutputStream(Files.newOutputStream(p))){for(Map.Entry<String,byte[]> e:parts.entrySet()){z.putNextEntry(new ZipEntry(e.getKey()));z.write(e.getValue());z.closeEntry();}}return p;
    }
    private static void rtf(String content,String expected,boolean reject)throws Exception {
        Path p=Files.createTempFile("folio-rtf-test-", ".rtf");try{Files.write(p,content.getBytes(StandardCharsets.ISO_8859_1));
            if(reject)fail(p,expected);else try(FolioHtmlSource s=FolioHtmlSource.open(p.toString(),"auto")){check(html(s).contains(expected),"RTF: "+expected);}
        }finally{Files.delete(p);}
    }
    public static void main(String[] args)throws Exception {
        for(String format:new String[]{"docx","odt","rtf"}){
            try(FolioHtmlSource source=FolioHtmlSource.open(ROOT.resolve("propio."+format).toString(),"auto")){
                String html=html(source);check(source.chapterCount()>0,"Capitulos "+format);
                for(String part:new String[]{"<h1>Documento propio</h1>","<strong>negrita</strong>","<em>cursiva</em>","<li>","Primero","Segundo","<table>","Celda A","Celda B","&lt;seguro&gt;","\u00e1\u00e9\u00ed\u00f3\u00fa","Vista simplificada"})check(html.contains(part),format+": "+part);
                Matcher image=Pattern.compile("https://folio.invalid/([^\"]+)").matcher(html);check(image.find(),"Imagen "+format);
                byte[] png=source.resource(image.group(1),100000);check(png.length>40&&png[0]==(byte)137,"PNG local "+format);
                try{source.resource(image.group(1),1);throw new AssertionError("Ignoro limite");}catch(IOException e){checks++;}
                try{source.resource("../../etc/passwd",1024);throw new AssertionError("Ruta expuesta");}catch(IOException e){checks++;}
                source.checkDrm();check(!source.isFont("fuente.ttf"),"Fuente sistema");
                if(format.equals("rtf")){check(html.contains("\u20ac"),"Unicode RTF");check(html.contains("\ud83d\ude00"),"Par sustituto en negrita");check(!html.contains("NO MOSTRAR")&&!html.contains("HYPERLINK")&&html.contains("Enlace visible"),"Destinos/campos");}
            }
            fail(ROOT.resolve("danado."+format),null);
            try(FolioHtmlSource source=FolioHtmlSource.open(ROOT.resolve("propio."+format).toString(),"auto",()->true)){throw new AssertionError("No cancelado");}catch(Exception e){check(messages(e).contains("Cancelado"),"Cancelacion "+format);}
        }
        // Variante binaria pict y destino shppict: el mismo PNG original, sin hexadecimal.
        byte[] image=parts("propio.docx").get("word/media/propia.png");
        Path binary=Files.createTempFile("folio-pict-", ".rtf");
        try{
            ByteArrayOutputStream b=new ByteArrayOutputStream();
            b.write(("{\\rtf1{\\*\\shppict{\\pict\\pngblip\\bin"+image.length+" ").getBytes(StandardCharsets.US_ASCII));b.write(image);b.write("}}}".getBytes(StandardCharsets.US_ASCII));Files.write(binary,b.toByteArray());
            try(FolioHtmlSource source=FolioHtmlSource.open(binary.toString(),"auto")){
                check(html(source).contains("<img"),"Imagen binaria RTF/shppict");
                check(Arrays.equals(image,source.resource("document-images/0.png",10000)),"Bytes pict intactos");
            }
        }finally{Files.delete(binary);}
        fail(ROOT.resolve("entidad.docx"),"DTD");fail(ROOT.resolve("cifrado-sintetico.odt"),"ODT cifrado");
        try(FolioHtmlSource s=FolioHtmlSource.open(ROOT.resolve("externo.docx").toString(),"auto")){String html=html(s);check(!html.contains("<img"),"Imagen externa bloqueada");check(html.contains("omitida"),"Aviso imagen");}
        try(FolioHtmlSource s=FolioHtmlSource.open(ROOT.resolve("objeto.rtf").toString(),"auto")){String html=html(s);check(!html.contains("SECRETO")&&html.contains("despues"),"Objeto omitido");check(html.contains("Objetos"),"Aviso objeto");}
        rtf("{\\rtf1\\ansi {\\b A {\\i B} C} D\\par}","<strong>A </strong>",false);
        rtf("{\\rtf1\\ansi\\uc2 \\u241\\'3f?X\\par}","\u00f1X",false);
        rtf("{\\rtf1\\ansi\\ansicpg65001 \\'c3\\'b1\\par}","\u00f1",false);
        rtf("{\\rtf1 \\{llaves\\} \\\\ ruta\\par}","{llaves} \\ ruta",false);
        rtf("{\\rtf1{\\*\\ignorar \\bin4 {}xx}Visible\\par}","Visible",false);
        rtf("{\\rtf1\\uc99 X}","Fallback",true);
        rtf("{\\rtf1\\'zz}","Escape",true);
        rtf("{\\rtf1\\bin99 abc}","truncado",true);
        rtf("{\\rtf1\\pict\\pngblip abf}","incompleto",true);
        rtf("{\\rtf1 X}basura","despues",true);
        rtf("{\\rtf1\\uc0\\u-10179}","Unicode",true);
        rtf("{\\rtf1"+String.join("",Collections.nCopies(130,"{")),"anidado",true);
        Map<String,byte[]> parts=parts("propio.docx");String document=new String(parts.get("word/document.xml"),StandardCharsets.UTF_8);
        for(String xml:new String[]{"<!DOCTYPE x [<!ENTITY x 'boom'>]>"+document,"<x>"+String.join("",Collections.nCopies(70,"<x>"))}){
            parts.put("word/document.xml",xml.getBytes(StandardCharsets.UTF_16));Path p=variant(".docx",parts);try{fail(p,null);}finally{Files.delete(p);}
        }
        parts.put("word/document.xml",new byte[5*1024*1024]);Path huge=variant(".docx",parts);try{fail(huge,"limite");}finally{Files.delete(huge);}
        parts=parts("propio.docx");String block="<w:p><w:r><w:t>Contenido repetible &amp; seguro. "+String.join("",Collections.nCopies(200,"texto "))+"</w:t></w:r></w:p>";
        String multi="<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"+String.join("",Collections.nCopies(600,block))+"</w:body></w:document>";
        parts.put("word/document.xml",multi.getBytes(StandardCharsets.UTF_8));Path large=variant(".docx",parts);
        try(FolioHtmlSource s=FolioHtmlSource.open(large.toString(),"auto")){
            check(s.chapterCount()>2,"Particion por parrafos");for(int i=0;i<s.chapterCount();i++){String h=new String(s.chapter(i),StandardCharsets.UTF_8);check(h.endsWith("</p>"),"Capitulo completo");check(h.length()<210000,"Tamano capitulo");}
            AtomicInteger n=new AtomicInteger();try(FolioHtmlSource ignored=FolioHtmlSource.open(large.toString(),"auto",()->n.incrementAndGet()>20)){throw new AssertionError("Cancelacion durante parse");}catch(Exception e){check(messages(e).contains("Cancelado"),"Cancelacion en curso");}
        }finally{Files.delete(large);}
        for(String ext:new String[]{"doc","kfx","bin"})check(FolioDocumentDiagnostics.unsupported(ext).startsWith("Formato a\u00fan no compatible:"),"Diagnostico "+ext);
        fail(ROOT.resolve("ole-firma.doc"),"DOC binario");fail(ROOT.resolve("kfx-firma.kfx"),"KFX");fail(ROOT.resolve("desconocido.bin"),"no se reconoce");
        System.out.println("OK: "+checks+" comprobaciones de documentos");
    }
}
