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
    private final List<FolioContents.Entry> toc=new ArrayList<>();
    private final List<Long> tocOffsets=new ArrayList<>();
    private String tocError;
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
            try { readContents(header,base); } catch(InterruptedIOException stopped){throw stopped;} catch(IOException invalid){toc.clear();tocOffsets.clear();tocError="Indice MOBI no compatible o danado";}

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
        String html=new String(withAnchors(index,data),charset);
        html=rewrite(html,Pattern.compile("(?i)recindex\\s*=\\s*['\"]?(\\d+)['\"]?"),false);
        html=rewrite(html,Pattern.compile("(?i)kindle:embed:([0-9a-v]+)(?:\\?[^'\" >]*)?"),true);
        Matcher flow=Pattern.compile("(?i)kindle:flow:([0-9a-v]+)\\?mime=([^'\" >]+)").matcher(html); StringBuffer out=new StringBuffer();
        while(flow.find()) { String extension=flow.group(2).contains("css")?"css":"svg"; flow.appendReplacement(out,"https://folio.invalid/flow/"+Integer.parseInt(flow.group(1),32)+"."+extension); } flow.appendTail(out);
        return out.toString().getBytes(StandardCharsets.UTF_8);
    }
    private long mappedOffset(int chapter,long offset)throws IOException {
        if(skeletons==null){long[] c=chapters.get(chapter);return offset>=c[0]&&offset<c[1]?offset-c[0]:-1;}
        FolioMobiIndex.Entry skel=skeletons.get(chapter);long start=skel.value(6,0),length=skel.value(6,1),pointer=start+length;
        long mapped=offset>=start&&offset<pointer?offset-start:-1;
        int first=fragmentStart[chapter],count=(int)skel.value(1,0);
        for(int i=first;i<first+count;i++){
            FolioMobiIndex.Entry frag=fragments.get(i);long at;
            try{at=Long.parseLong(frag.label)-start;}catch(NumberFormatException e){throw new IOException(e);}
            long n=frag.value(6,1);
            if(mapped>=at)mapped+=n;
            if(offset>=pointer&&offset<pointer+n)mapped=at+offset-pointer;
            pointer+=n;
        }
        return mapped;
    }
    private void addContents(String title,long offset,int depth)throws IOException {
        if(toc.size()>=FolioContents.LIMIT)return;
        for(int i=0;i<chapterCount();i++){
            if(cancelled.getAsBoolean())throw new InterruptedIOException("Cancelado");
            if(mappedOffset(i,offset)>=0){
            toc.add(new FolioContents.Entry(title,i,"folio-pos-"+toc.size(),depth));tocOffsets.add(offset);return;
        }}
    }
    private static long vli(byte[] data,int[] at)throws IOException {
        long n=0;for(int i=0;i<5;i++){range(data,at[0],1);int b=data[at[0]++]&255;n=(n<<7)|(b&127);if((b&128)!=0)return n;}throw new IOException("CNCX invalido");
    }
    private void readContents(byte[] header,int base)throws IOException {
        long ncx=u32(header,20)>=232?u32(header,244):0xffffffffL;
        if(ncx!=0&&ncx!=0xffffffffL){
            int index=base+(int)ncx;byte[] meta=record(index);int sections=(int)u32(meta,24), strings=(int)u32(meta,52);
            int lastString=-1;byte[] stringData=null;
            for(FolioMobiIndex.Entry item:FolioMobiIndex.read(this::record,index)){
                if(toc.size()>=FolioContents.LIMIT)break;
                long label=item.value(3,0);int recordNumber=(int)(label>>>16);if(recordNumber>=strings)throw new IOException("CNCX ausente");
                if(recordNumber!=lastString){stringData=record(index+sections+1+recordNumber);lastString=recordNumber;}
                byte[] data=stringData;int[] at={(int)(label&65535)};int length=(int)vli(data,at);range(data,at[0],length);
                String title=new String(data,at[0],length,StandardCharsets.UTF_8);
                addContents(title,item.value(1,0),item.values.containsKey(4)?(int)item.value(4,0):0);
            }
            return;
        }
        // MOBI antiguo: guia type=toc y enlaces filepos de su indice HTML.
        String start=new String(span(0,Math.min(text.length(),262144),262144),charset);
        Matcher refs=Pattern.compile("(?is)<reference\\b([^>]*)>").matcher(start);long begin=-1;
        while(refs.find())if("toc".equalsIgnoreCase(attribute(refs.group(1),"type"))){try{begin=Long.parseLong(attribute(refs.group(1),"filepos"));}catch(NumberFormatException ignored){}break;}
        if(begin<0||begin>=text.length())return;
        String html=new String(span(begin,Math.min(text.length()-begin,524288),524288),charset);
        Matcher links=Pattern.compile("(?is)<a\\b([^>]*)>(.*?)</a>").matcher(html);
        while(links.find()&&toc.size()<FolioContents.LIMIT){
            String target=attribute(links.group(1),"filepos");if(target.isEmpty())continue;
            String title=links.group(2).replaceAll("<[^>]*>","").replace("&amp;","&").replace("&lt;","<").replace("&gt;",">");
            try{addContents(title,Long.parseLong(target),0);}catch(NumberFormatException ignored){}
        }
    }
    private static String attribute(String text,String name){
        Matcher m=Pattern.compile("(?i)\\b"+name+"\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))").matcher(text);
        return m.find()?(m.group(1)!=null?m.group(1):m.group(2)!=null?m.group(2):m.group(3)):"";
    }
    private byte[] withAnchors(int chapter,byte[] data)throws IOException {
        TreeMap<Integer,List<String>> anchors=new TreeMap<>();
        for(int i=0;i<toc.size();i++)if(toc.get(i).chapter==chapter){
            int pos=(int)mappedOffset(chapter,tocOffsets.get(i));if(pos<0||pos>=data.length)continue;
            while(pos<data.length&&(data[pos]&192)==128)pos++; // No cortar UTF-8.
            int before=pos-1;while(before>=0&&data[before]!='<'&&data[before]!='>')before--;
            if(pos<data.length&&data[pos]=='<' || before>=0&&data[before]=='<')while(pos<data.length&&data[pos++]!='>'){}
            anchors.computeIfAbsent(pos,k->new ArrayList<>()).add(toc.get(i).fragment);
        }
        ByteArrayOutputStream result=new ByteArrayOutputStream();int last=0;
        for(Map.Entry<Integer,List<String>> a:anchors.entrySet()){
            result.write(data,last,a.getKey()-last);for(String id:a.getValue())result.write(("<span id=\""+id+"\"></span>").getBytes(StandardCharsets.US_ASCII));last=a.getKey();
        }
        result.write(data,last,data.length-last);return result.toByteArray();
    }
    public List<FolioContents.Entry> contents()throws IOException {if(tocError!=null)throw new IOException(tocError);return new ArrayList<>(toc);}
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
