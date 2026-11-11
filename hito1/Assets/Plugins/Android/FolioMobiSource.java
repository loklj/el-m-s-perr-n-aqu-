package com.loklj.folioxr;
import java.io.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;
import static com.loklj.folioxr.FolioMobiCodec.*;

/** Parser propio PalmDB/MOBI/EXTH/KF8. Sin libmobi ni codigo de descifrado. */
public final class FolioMobiSource implements FolioHtmlSource {
    private RandomAccessFile pdb,text;
    private File cache;
    private long[] records;
    private final List<long[]> chapters=new ArrayList<>();
    private final List<long[]> flows=new ArrayList<>();
    private List<FolioMobiIndex.Entry> skeletons,fragments;
    private int[] fragmentStart;
    private int imageStart;
    private Charset charset;
    private static final int CHAPTER_LIMIT=4*1024*1024;
    private final java.util.function.BooleanSupplier cancelled;
    FolioMobiSource(String file) throws Exception { this(file, () -> false); }
    FolioMobiSource(String file, java.util.function.BooleanSupplier cancelled) throws Exception {
        this.cancelled=cancelled;
        try {
            pdb=new RandomAccessFile(file,"r");
            if(pdb.length()<78)throw new IOException("PalmDB truncado");
            pdb.seek(60); byte[] signature=new byte[8]; pdb.readFully(signature); magic(signature,0,"BOOKMOBI");
            pdb.seek(76); int count=pdb.readUnsignedShort();
            if(count<2 || pdb.length()<78L+8L*count)throw new IOException("Tabla PalmDB invalida");
            records=new long[count+1];
            for(int i=0;i<count;i++) { records[i]=pdb.readInt()&0xffffffffL; pdb.skipBytes(4); }
            records[count]=pdb.length();
            if(records[0]<78L+8L*count)throw new IOException("Registro sobre la cabecera");
            for(int i=0;i<count;i++) if(records[i]>=records[i+1])throw new IOException("Offsets PalmDB no ordenados");
            byte[] header=record(0); checkHeader(header); int base=0;
            Map<Long,byte[]> exth=exth(header);
            if(u32(header,36)<8 && exth.containsKey(121L)) {
                long boundary=u32(exth.get(121L),0);
                if(boundary!=0xffffffffL) {
                    if(boundary>=count)throw new IOException("Limite KF8 fuera de rango");
                    base=(int)boundary; header=record(base);
                    if(header.length>=8 && new String(header,0,8,StandardCharsets.US_ASCII).equals("BOUNDARY"))header=record(++base);
                    checkHeader(header); exth(header);
                }
            }
            int compression=u16(header,0), textRecords=u16(header,8), version=(int)u32(header,36);
            long textLength=u32(header,4), encoding=u32(header,28);
            if(encoding!=65001 && encoding!=1252)throw new IOException("Codificacion MOBI no compatible");
            charset=Charset.forName(encoding==65001?"UTF-8":"Windows-1252");
            imageStart=(int)u32(header,108); // Los recursos de un hibrido pueden preceder al encabezado KF8.
            int flags=version>=5 && u32(header,20)>=228?u16(header,242):0;
            FolioMobiCodec.Huff huff=null;
            if(compression==17480) {
                int h=(int)u32(header,112)+base, n=(int)u32(header,116);
                if(n<2 || n>1024)throw new IOException("Numero de registros HUFF invalido");
                huff=new FolioMobiCodec.Huff(record(h));
                for(int i=1;i<n;i++)huff.dictionary(record(h+i));
            } else if(compression!=1 && compression!=2)throw new IOException("Compresion MOBI no compatible");
            if(base+textRecords>=count)throw new IOException("Registros de texto truncados");
            cache=File.createTempFile("folio-mobi-", ".cache"); text=new RandomAccessFile(cache,"rw");
            for(int i=1;i<=textRecords;i++) {
                if(cancelled.getAsBoolean() || Thread.currentThread().isInterrupted())throw new InterruptedIOException();
                byte[] compressed=trim(record(base+i),flags);
                byte[] decoded=compression==1?compressed:compression==2?palm(compressed):huff.decode(compressed);
                if(decoded.length>LIMIT)throw new IOException("Registro de texto demasiado grande");
                long remaining=textLength-text.length(); if(remaining<0)throw new IOException("Longitud de texto invalida");
                text.write(decoded,0,(int)Math.min(decoded.length,remaining));
            }
            if(text.length()!=textLength)throw new IOException("Texto MOBI incompleto");
            if(version>=8) {
                long fdst=header.length>=196?u32(header,192):0xffffffffL;
                if(fdst!=0xffffffffL && fdst>0) readFlows(record(base+(int)fdst));
                long frag=header.length>=252?u32(header,248):0xffffffffL, skel=header.length>=256?u32(header,252):0xffffffffL;
                if(skel!=0xffffffffL) {
                    skeletons=FolioMobiIndex.read(this::record,base+(int)skel);
                    fragments=FolioMobiIndex.read(this::record,base+(int)frag);
                    fragmentStart=new int[skeletons.size()]; int next=0;
                    for(int i=0;i<skeletons.size();i++) {
                        fragmentStart[i]=next; long n=skeletons.get(i).value(1,0);
                        if(n>fragments.size()-next)throw new IOException("Fragmentos KF8 incompletos"); next+=(int)n;
                    }
                    if(next!=fragments.size() || skeletons.isEmpty())throw new IOException("Spine KF8 inconsistente");
                }
            }
            if(skeletons==null) indexHtml(flows.isEmpty()?text.length():flows.get(0)[1]);
        } catch(Exception e) { close(); throw e; }
    }
    private void checkHeader(byte[] h) throws IOException {
        range(h,0,116); if(u16(h,12)!=0)throw new FolioEpubContainer.DrmException(); magic(h,16,"MOBI");
        long length=u32(h,20); if(length<100 || length>h.length-16)throw new IOException("Encabezado MOBI truncado");
    }
    private Map<Long,byte[]> exth(byte[] h) throws IOException {
        Map<Long,byte[]> result=new HashMap<>();
        if(u32(h,20)<116 || h.length<132 || (u32(h,128)&64)==0)return result;
        int start=16+(int)u32(h,20); magic(h,start,"EXTH"); int size=(int)u32(h,start+4), count=(int)u32(h,start+8);
        range(h,start,size); if(count<0 || count>10000)throw new IOException("EXTH demasiado grande");
        int at=start+12;
        for(int i=0;i<count;i++) {
            long type=u32(h,at); int length=(int)u32(h,at+4);
            if(length<8 || at>start+size-length)throw new IOException("EXTH truncado");
            result.put(type,Arrays.copyOfRange(h,at+8,at+length)); at+=length;
        }
        return result;
    }
    private synchronized byte[] record(int index) throws IOException {
        if(index<0 || index>=records.length-1)throw new IOException("Registro fuera de rango");
        long length=records[index+1]-records[index]; if(length>8*1024*1024)throw new IOException("Registro excede 8 MiB");
        byte[] data=new byte[(int)length]; pdb.seek(records[index]); pdb.readFully(data); return data;
    }
    private void readFlows(byte[] data) throws IOException {
        magic(data,0,"FDST"); int count=(int)u32(data,8); if(count<1 || count>10000)throw new IOException("FDST invalido");
        range(data,12,count*8);
        for(int i=0;i<count;i++) { long a=u32(data,12+i*8), b=u32(data,16+i*8); if(a>b || b>text.length())throw new IOException("Flujo KF8 truncado"); flows.add(new long[]{a,b}); }
    }
    private void indexHtml(long end) throws IOException {
        long start=0, at=0; int value; boolean tag=false; int quote=0;
        try(InputStream input=new BufferedInputStream(new FileInputStream(cache))) {
            while(at<end && (value=input.read())!=-1) {
                at++;
                if((at&8191)==0 && cancelled.getAsBoolean())throw new InterruptedIOException("Cancelado");
                if(tag && quote!=0) { if(value==quote)quote=0; }
                else if(tag && (value=='\'' || value=='"'))quote=value;
                else if(value=='<')tag=true;
                else if(value=='>')tag=false;
                if(at-start>=200000 && !tag && value=='>') { chapters.add(new long[]{start,at}); start=at; }
                if(at-start>CHAPTER_LIMIT)throw new IOException("Bloque MOBI excede 4 MiB sin corte HTML");
            }
        }
        if(start<end)chapters.add(new long[]{start,end});
        if(chapters.isEmpty())throw new IOException("MOBI sin texto");
    }
    private byte[] span(long start,long length,int limit) throws IOException {
        if(start<0 || length<0 || length>limit || start>text.length()-length)throw new IOException("Tramo KF8 fuera de rango");
        byte[] data=new byte[(int)length]; text.seek(start); text.readFully(data); return data;
    }
    public int chapterCount() { return skeletons!=null?skeletons.size():chapters.size(); }
    public String chapterName(int index) { return "mobi/"+index+".html"; }
    public synchronized byte[] chapter(int index) throws IOException {
        if(index<0 || index>=chapterCount())throw new IOException("Capitulo MOBI fuera de rango");
        byte[] data;
        if(skeletons==null) { long[] range=chapters.get(index); data=span(range[0],range[1]-range[0],CHAPTER_LIMIT); }
        else {
            FolioMobiIndex.Entry skel=skeletons.get(index); long start=skel.value(6,0), length=skel.value(6,1), pointer=start+length;
            data=span(start,length,CHAPTER_LIMIT);
            int first=fragmentStart[index], count=(int)skel.value(1,0);
            for(int f=first;f<first+count;f++) {
                FolioMobiIndex.Entry frag=fragments.get(f);
                if(frag.value(3,0)!=index)throw new IOException("Fragmento de otro capitulo KF8");
                long position;
                try { position=Long.parseLong(frag.label)-start; } catch(NumberFormatException e) { throw new IOException("Posicion KF8 invalida",e); }
                long bytes=frag.value(6,1);
                if(position<0 || position>data.length || bytes>CHAPTER_LIMIT-data.length)throw new IOException("Insercion KF8 fuera de rango");
                byte[] insert=span(pointer,bytes,CHAPTER_LIMIT), next=new byte[data.length+insert.length]; pointer+=bytes;
                System.arraycopy(data,0,next,0,(int)position); System.arraycopy(insert,0,next,(int)position,insert.length);
                System.arraycopy(data,(int)position,next,(int)position+insert.length,data.length-(int)position); data=next;
            }
        }
        String html=new String(data,charset);
        html=rewrite(html,Pattern.compile("(?i)recindex\\s*=\\s*['\"]?(\\d+)['\"]?"),false);
        html=rewrite(html,Pattern.compile("(?i)kindle:embed:([0-9a-v]+)(?:\\?[^'\" >]*)?"),true);
        Matcher flow=Pattern.compile("(?i)kindle:flow:([0-9a-v]+)\\?mime=([^'\" >]+)").matcher(html); StringBuffer out=new StringBuffer();
        while(flow.find()) { String extension=flow.group(2).contains("css")?"css":"svg"; flow.appendReplacement(out,"https://folio.invalid/flow/"+Integer.parseInt(flow.group(1),32)+"."+extension); } flow.appendTail(out);
        return out.toString().getBytes(StandardCharsets.UTF_8);
    }
    private String rewrite(String html,Pattern pattern,boolean base32) throws IOException {
        Matcher m=pattern.matcher(html); StringBuffer out=new StringBuffer();
        while(m.find()) {
            int index;
            try { index=Integer.parseInt(m.group(1),base32?32:10); } catch(NumberFormatException e) { throw new IOException("Recurso MOBI invalido",e); }
            String url="https://folio.invalid/image/"+index;
            m.appendReplacement(out,Matcher.quoteReplacement(base32?url:"src=\""+url+"\""));
        }
        m.appendTail(out); return out.toString();
    }
    public synchronized byte[] resource(String name,int limit) throws IOException {
        try {
            if(name.startsWith("image/")) {
                int index=Integer.parseInt(name.substring(6)); if(index<1 || imageStart<0)throw new IOException("Imagen ausente");
                byte[] bytes=record(imageStart+index-1); if(bytes.length>limit)throw new IOException("Imagen demasiado grande"); return bytes;
            }
            if(name.startsWith("flow/")) {
                int index=Integer.parseInt(name.substring(5,name.indexOf('.'))); if(index<1 || index>=flows.size())throw new IOException("Flujo ausente");
                long[] flow=flows.get(index); return span(flow[0],flow[1]-flow[0],limit);
            }
        } catch(NumberFormatException e) { throw new IOException(e); }
        throw new FileNotFoundException();
    }
    public boolean isFont(String name) { return false; }
    public void checkDrm() { } // Campo de cifrado comprobado en cada encabezado antes de leer texto.
    public void close() throws IOException {
        try { if(pdb!=null)pdb.close(); } finally { try { if(text!=null)text.close(); } finally { if(cache!=null)cache.delete(); } }
    }
}
