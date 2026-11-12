package com.loklj.folioxr;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.function.BooleanSupplier;
import java.util.zip.*;

/** Documentos simplificados: capitulos en disco y recursos locales acotados. */
public final class FolioDocumentSource implements FolioHtmlSource {
    static final int XML_LIMIT=4*1024*1024, IMAGE_LIMIT=8*1024*1024, BLOCK_LIMIT=1024*1024;
    private ZipFile zip;
    private File cache;
    private RandomAccessFile pages;
    private final List<long[]> chapters=new ArrayList<>();
    private final Map<String,String> images=new HashMap<>();
    private final Map<String,byte[]> embedded=new HashMap<>();
    private final Set<String> warnings=new LinkedHashSet<>();
    private final StringBuilder pending=new StringBuilder();
    private long imageBytes, htmlBytes;
    final BooleanSupplier cancelled;

    FolioDocumentSource(String path,BooleanSupplier cancelled) throws Exception {
        this.cancelled=cancelled;
        try {
            cache=File.createTempFile("folio-document-", ".html"); pages=new RandomAccessFile(cache,"rw");
            if(path.toLowerCase(Locale.ROOT).endsWith(".rtf")) {
                try(InputStream in=new BufferedInputStream(new FileInputStream(path))) { new FolioRtfReader(this).read(in); }
            } else {
                zip=new ZipFile(path);
                if(zip.size()>10000)throw new IOException("Documento con demasiadas entradas ZIP");
                new FolioOfficeXml(this).convert(path.toLowerCase(Locale.ROOT).endsWith(".odt"));
            }
            flush();
            if(chapters.isEmpty())throw new IOException("Documento sin texto ni imagenes compatibles");
        } catch(Exception e) { close(); throw e; }
    }
    void check() throws IOException {
        if(cancelled.getAsBoolean() || Thread.currentThread().isInterrupted())throw new InterruptedIOException("Cancelado");
    }
    boolean has(String name) { return zip.getEntry(name)!=null; }
    InputStream entry(String name,int limit) throws IOException {
        check();
        ZipEntry entry=zip.getEntry(name);
        if(entry==null)throw new IOException("Parte de documento ausente: "+name);
        if(entry.getSize()>limit)throw new IOException("Parte de documento excede limite: "+name);
        return new FilterInputStream(zip.getInputStream(entry)) {
            long count;
            public int read() throws IOException { check(); int b=super.read(); if(b>=0 && ++count>limit)throw new IOException("Recurso demasiado grande");return b; }
            public int read(byte[] b,int off,int len)throws IOException { check(); int n=in.read(b,off,len);if(n>0 && (count+=n)>limit)throw new IOException("Recurso demasiado grande");return n; }
        };
    }
    void block(String html) throws IOException {
        check();
        int size=html.getBytes(StandardCharsets.UTF_8).length;
        if(size>BLOCK_LIMIT)throw new IOException("Bloque de documento excede 1 MiB; exporta a PDF");
        htmlBytes+=size; if(htmlBytes>16L*1024*1024)throw new IOException("Documento convertido excede 16 MiB; exporta a PDF");
        if(pending.length()+html.length()>200000)flush();
        pending.append(html);
    }
    private void flush() throws IOException {
        if(pending.length()==0)return;
        byte[] bytes=pending.toString().getBytes(StandardCharsets.UTF_8);
        long start=pages.length(); pages.seek(start); pages.write(bytes);chapters.add(new long[]{start,bytes.length});pending.setLength(0);
    }
    void warn(String message) { warnings.add(message); }
    static String escape(String s) { return s.replace("&","&amp;").replace("<","&lt;").replace(">","&gt;").replace("\"","&quot;"); }
    String image(String path) throws IOException {
        if(path==null || path.isEmpty()) { warn("Imagen ausente o externa omitida.");return ""; }
        if(!path.matches("[A-Za-z0-9_./ -]+") || path.startsWith("/") || Arrays.asList(path.split("/")).contains("..")) {
            warn("Imagen externa o ruta no compatible omitida.");return "";
        }
        String lower=path.toLowerCase(Locale.ROOT);
        if(!(lower.endsWith(".png")||lower.endsWith(".jpg")||lower.endsWith(".jpeg"))) { warn("Solo se muestran imagenes PNG/JPEG.");return ""; }
        if(!has(path)){warn("Imagen ausente omitida.");return "";}
        if(zip.getEntry(path).getSize()>IMAGE_LIMIT){warn("Imagen demasiado grande omitida.");return "";}
        String key="document-images/"+images.size()+(lower.endsWith(".png")?".png":".jpg");images.put(key,path);
        return imageTag(key);
    }
    String image(byte[] data,String extension) throws IOException {
        imageBytes+=data.length;
        if(data.length>IMAGE_LIMIT || imageBytes>16L*1024*1024)throw new IOException("Imagenes RTF exceden cuota");
        boolean png=data.length>=8 && data[0]==(byte)137 && data[1]==80 && data[2]==78 && data[3]==71;
        boolean jpg=data.length>=3 && data[0]==(byte)255 && data[1]==(byte)216 && data[2]==(byte)255;
        if(!(extension.equals("png")&&png || extension.equals("jpg")&&jpg))throw new IOException("Imagen RTF danada");
        String key="document-images/"+embedded.size()+"."+extension;embedded.put(key,data);return imageTag(key);
    }
    private String imageTag(String key){return "<img alt=\"Imagen del documento\" src=\"https://folio.invalid/"+key+"\">";}
    public int chapterCount(){return chapters.size();}
    public String chapterName(int index){return "document/"+index+".html";}
    public synchronized byte[] chapter(int index)throws IOException {
        check();if(index<0||index>=chapters.size())throw new IOException("Capitulo fuera de rango");
        long[] span=chapters.get(index);byte[] bytes=new byte[(int)span[1]];pages.seek(span[0]);pages.readFully(bytes);
        String prefix="<style>table{border-collapse:collapse}td,th{border:1px solid #777;padding:4px}p{white-space:pre-wrap}</style>";
        if(index==0){prefix+="<p><small>Vista simplificada: la distribucion puede diferir del original.</small></p>";
            for(String warning:warnings)prefix+="<p><small>"+escape(warning)+"</small></p>";}
        return (prefix+new String(bytes,StandardCharsets.UTF_8)).getBytes(StandardCharsets.UTF_8);
    }
    public synchronized byte[] resource(String name,int limit)throws IOException {
        check();if(embedded.containsKey(name)){byte[] b=embedded.get(name);if(b.length>limit)throw new IOException("Imagen demasiado grande");return b;}
        String path=images.get(name);if(path==null)throw new FileNotFoundException();
        try(InputStream in=entry(path,Math.min(limit,IMAGE_LIMIT));ByteArrayOutputStream out=new ByteArrayOutputStream()){
            byte[] buffer=new byte[8192];int n;while((n=in.read(buffer))!=-1)out.write(buffer,0,n);return out.toByteArray();}
    }
    public boolean isFont(String name){return false;}
    public void checkDrm(){} // ODT cifrado se rechaza antes de leer content.xml; no se descifra Office.
    public void close()throws IOException {
        try{if(zip!=null)zip.close();}finally{try{if(pages!=null)pages.close();}finally{if(cache!=null)cache.delete();embedded.clear();}}
    }
}
