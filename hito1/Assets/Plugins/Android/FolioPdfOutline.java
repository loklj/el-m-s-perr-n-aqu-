package com.loklj.folioxr;

import java.io.*;
import java.nio.charset.*;
import java.util.*;
import java.util.function.BooleanSupplier;
import java.util.zip.InflaterInputStream;

/** Metadatos PDF propios: xref clasico/stream, ObjStm y outlines. Nunca dibuja ni descifra. */
public final class FolioPdfOutline implements Closeable {
    private static final int MAX=4*1024*1024, MAX_XREF=300000;
    private final RandomAccessFile file;
    private final BooleanSupplier cancelled;
    private final Map<Integer,long[]> xref=new HashMap<>();
    private final Set<Integer> resolving=new HashSet<>();
    private final LinkedHashMap<Integer,Object> cache=new LinkedHashMap<Integer,Object>(64,.75f,true){protected boolean removeEldestEntry(Map.Entry<Integer,Object> e){return size()>64;}};
    private final Map<Integer,Integer> pages=new HashMap<>();
    private final Map<String,Object> names=new HashMap<>();
    private Object root;
    private boolean encrypted;
    static final class Ref {final int id,gen;Ref(int id,int gen){this.id=id;this.gen=gen;}}
    static final class Name {final String value;Name(String v){value=v;}}
    static final class Stream {final Map<String,Object> dict;final byte[] bytes;Stream(Map<String,Object>d,byte[]b){dict=d;bytes=b;}}
    interface Data {int read()throws IOException;long pos()throws IOException;void seek(long pos)throws IOException;}
    final class FileData implements Data {public int read()throws IOException{return file.read();}public long pos()throws IOException{return file.getFilePointer();}public void seek(long p)throws IOException{file.seek(p);}}
    static final class Bytes implements Data {final byte[] bytes;int at;Bytes(byte[] b){bytes=b;}public int read(){return at<bytes.length?bytes[at++]&255:-1;}public long pos(){return at;}public void seek(long p)throws IOException{if(p<0||p>bytes.length)throw new IOException("Offset de stream invalido");at=(int)p;}}
    final class Tokens {
        final Data in;final long start,limit;int nodes;
        Tokens(Data in,long limit)throws IOException{this.in=in;this.start=in.pos();this.limit=limit;}
        int read()throws IOException{check();if(in.pos()-start>limit)throw error("metadatos demasiado grandes");return in.read();}
        void back(int c)throws IOException{if(c>=0)in.seek(in.pos()-1);}
        int next()throws IOException{int c;while((c=read())>=0){if(c=='%'){while((c=read())>=0&&c!='\n'&&c!='\r'){}continue;}if(c>32)return c;}return -1;}
        String word()throws IOException{StringBuilder b=new StringBuilder();int c=next();while(c>=0&&!delimiter(c)){if(b.length()>128)throw error("token excesivo");b.append((char)c);c=read();}back(c);return b.toString();}
        Object value(int depth)throws IOException{
            if(depth>64||++nodes>100000)throw error("estructura excesiva");int c=next();
            if(c=='/'){ByteArrayOutputStream b=new ByteArrayOutputStream();while((c=read())>=0&&!delimiter(c)){if(c=='#'){int a=Character.digit((char)read(),16),d=Character.digit((char)read(),16);if(a<0||d<0)throw error("nombre invalido");c=a*16+d;}b.write(c);if(b.size()>1024)throw error("nombre demasiado largo");}back(c);return new Name(new String(b.toByteArray(),StandardCharsets.ISO_8859_1));}
            if(c=='('){ByteArrayOutputStream b=new ByteArrayOutputStream();int nested=1;
                while(nested>0){c=read();if(c<0)throw error("cadena truncada");if(c=='\\'){
                    c=read();if(c=='\r'){int n=read();if(n!='\n')back(n);continue;}if(c=='\n')continue;
                    if(c>='0'&&c<='7'){int n=c-'0';for(int i=0;i<2;i++){int d=read();if(d<'0'||d>'7'){back(d);break;}n=n*8+d-'0';}c=n&255;}
                    else {String esc="nrtbf";int i=esc.indexOf(c);if(i>=0)c=new int[]{10,13,9,8,12}[i];}
                }else if(c=='('){if(++nested>32)throw error("cadena anidada");}else if(c==')'&&--nested==0)break;
                b.write(c);if(b.size()>65536)throw error("cadena excesiva");}return b.toByteArray();}
            if(c=='<'){
                int n=read();if(n=='<'){Map<String,Object> d=new LinkedHashMap<>();while(true){int end=next();if(end=='>'){if(read()!='>')throw error("diccionario sin cierre");return d;}back(end);Object key=value(depth+1);if(!(key instanceof Name)||d.size()>=16384)throw error("diccionario invalido");d.put(((Name)key).value,value(depth+1));}}
                back(n);ByteArrayOutputStream b=new ByteArrayOutputStream();int high=-1;
                while((n=read())!='>'){if(n<0)throw error("hexadecimal truncado");if(n<=32)continue;int h=Character.digit((char)n,16);if(h<0)throw error("hexadecimal invalido");if(high<0)high=h;else{b.write(high*16+h);high=-1;}if(b.size()>65536)throw error("cadena excesiva");}if(high>=0)b.write(high*16);return b.toByteArray();
            }
            if(c=='['){List<Object> a=new ArrayList<>();while(true){int end=next();if(end==']')return a;if(end<0||a.size()>=100000)throw error("array invalido");back(end);a.add(value(depth+1));}}
            back(c);String token=word();if(token.isEmpty())throw error("valor truncado");
            if(token.matches("[+-]?\\d+")){
                long number;try{number=Long.parseLong(token);}catch(NumberFormatException e){throw error("numero excesivo");}
                long after=in.pos();String second=word();if(number>=0&&number<=Integer.MAX_VALUE&&second.matches("\\d{1,5}")&&word().equals("R"))return new Ref((int)number,Integer.parseInt(second));in.seek(after);return number;
            }
            if(token.matches("[+-]?(?:\\d*\\.\\d+|\\d+\\.\\d*)"))return Double.parseDouble(token);
            if(token.equals("null"))return null;if(token.equals("true"))return true;if(token.equals("false"))return false;throw error("token no reconocido");
        }
    }
    public FolioPdfOutline(String path,BooleanSupplier cancelled)throws IOException {this.file=new RandomAccessFile(path,"r");this.cancelled=cancelled;}
    private static boolean delimiter(int c){return c<=32||"()<>[]{}/%".indexOf(c)>=0;}
    private IOException error(String reason){return new IOException("Indice PDF no disponible: "+reason);}
    private void check()throws IOException{if(cancelled.getAsBoolean()||Thread.currentThread().isInterrupted())throw new InterruptedIOException("Cancelado");}
    private static long num(Object o,long fallback){return o instanceof Number?((Number)o).longValue():fallback;}
    @SuppressWarnings("unchecked") private Map<String,Object> dict(Object o)throws IOException{o=resolve(o);if(o instanceof Stream)o=((Stream)o).dict;if(!(o instanceof Map))throw error("se esperaba diccionario");return (Map<String,Object>)o;}
    @SuppressWarnings("unchecked") private List<Object> array(Object o)throws IOException{o=resolve(o);if(!(o instanceof List))throw error("se esperaba array");return (List<Object>)o;}
    private String name(Object o){return o instanceof Name?((Name)o).value:"";}
    private Object object(long offset)throws IOException{
        if(offset<0||offset>=file.length())throw error("offset fuera del archivo");file.seek(offset);Tokens t=new Tokens(new FileData(),MAX);t.word();t.word();if(!t.word().equals("obj"))throw error("objeto ausente");
        Object value=t.value(0);long after=file.getFilePointer();String next=t.word();
        if(value instanceof Map&&next.equals("stream")){
            int c=file.read();if(c=='\r'){int n=file.read();if(n!='\n')file.seek(file.getFilePointer()-1);}else if(c!='\n')throw error("inicio de stream invalido");
            long start=file.getFilePointer();Map<String,Object>d=dict(value);long length=num(resolve(d.get("Length")),-1);
            if(length<0||length>MAX||start>file.length()-length)throw error("stream excesivo o truncado");file.seek(start);byte[] bytes=new byte[(int)length];file.readFully(bytes);return new Stream(d,bytes);
        }
        file.seek(after);return value;
    }
    private Object resolve(Object value)throws IOException{
        if(!(value instanceof Ref))return value;Ref ref=(Ref)value;if(cache.containsKey(ref.id))return cache.get(ref.id);
        if(!resolving.add(ref.id)||resolving.size()>64)throw error("referencia ciclica");
        try{
            long[] x=xref.get(ref.id);if(x==null||x[0]==0)throw error("referencia ausente");Object result;
            if(x[0]==1){if(x[2]!=ref.gen)throw error("generacion invalida");result=object(x[1]);}
            else{
                Object container=resolve(new Ref((int)x[1],0));if(!(container instanceof Stream))throw error("ObjStm ausente");Stream stream=(Stream)container;byte[] decoded=decode(stream);int count=(int)num(stream.dict.get("N"),-1),first=(int)num(stream.dict.get("First"),-1);
                if(count<0||count>10000||first<0||first>decoded.length||x[2]>=count)throw error("ObjStm invalido");
                Bytes b=new Bytes(decoded);Tokens t=new Tokens(b,MAX);long at=-1;
                for(int i=0;i<count;i++){int id=Integer.parseInt(t.word());long pos=Long.parseLong(t.word());if(i==x[2]){if(id!=ref.id)throw error("ObjStm referencia inconsistente");at=pos;}}
                b.seek(first+at);result=new Tokens(b,MAX).value(0);
            }
            if(weight(result,0)<65536)cache.put(ref.id,result);return result;
        }catch(NumberFormatException e){throw error("numero invalido");}finally{resolving.remove(ref.id);}
    }
    private int weight(Object value,int depth){
        if(depth>64)return 65536;long n=64;
        if(value instanceof Stream)n+=((Stream)value).bytes.length;
        else if(value instanceof byte[])n+=((byte[])value).length;
        else if(value instanceof Map){for(Object v:((Map<?,?>)value).values()){n+=weight(v,depth+1);if(n>=65536)break;}}
        else if(value instanceof List){for(Object v:(List<?>)value){n+=weight(v,depth+1);if(n>=65536)break;}}
        return (int)Math.min(n,65536);
    }
    private byte[] decode(Stream stream)throws IOException{
        Object filter=stream.dict.get("Filter");if(filter==null)return stream.bytes;
        if(filter instanceof List){List<Object> a=array(filter);if(a.size()!=1)throw error("filtros multiples");filter=a.get(0);}
        if(!name(filter).equals("FlateDecode"))throw error("filtro de metadatos no compatible");
        ByteArrayOutputStream b=new ByteArrayOutputStream();try(InputStream in=new InflaterInputStream(new ByteArrayInputStream(stream.bytes))){byte[] buf=new byte[8192];int n;while((n=in.read(buf))!=-1){check();if(b.size()+n>MAX)throw error("expansion excesiva");b.write(buf,0,n);}}
        byte[] data=b.toByteArray();Object params=stream.dict.get("DecodeParms");if(params==null)return data;if(params instanceof List)params=array(params).get(0);Map<String,Object>d=dict(params);int predictor=(int)num(d.get("Predictor"),1);
        if(predictor==1)return data;int columns=(int)num(d.get("Columns"),1),colors=(int)num(d.get("Colors"),1),bits=(int)num(d.get("BitsPerComponent"),8);
        if(columns<1||columns>10000||colors!=1||bits!=8||predictor<10||predictor>15)throw error("predictor no compatible");
        if(data.length%(columns+1)!=0)throw error("predictor truncado");byte[] result=new byte[data.length/(columns+1)*columns];int pos=0,output=0;
        while(pos<data.length){int type=data[pos++]&255;if(type>4)throw error("predictor invalido");for(int col=0;col<columns;col++){
            int left=col==0?0:result[output-1]&255,up=output<columns?0:result[output-columns]&255,corner=output<columns||col==0?0:result[output-columns-1]&255;
            int add=0;if(type==1)add=left;else if(type==2)add=up;else if(type==3)add=(left+up)/2;else if(type==4){int p=left+up-corner,a=Math.abs(p-left),u=Math.abs(p-up),c=Math.abs(p-corner);add=a<=u&&a<=c?left:u<=c?up:corner;}
            result[output++]=(byte)((data[pos++]&255)+add);
        }}return result;
    }
    private void index(long offset,Set<Long> seen)throws IOException{
        check();if(offset<=0||offset>=file.length()||!seen.add(offset)||seen.size()>32)throw error("cadena xref invalida");
        file.seek(offset);Tokens t=new Tokens(new FileData(),32L*1024*1024);String first=t.word();Map<String,Object> trailer;
        if(first.equals("xref")){
            while(true){String token=t.word();if(token.equals("trailer"))break;int begin,count;try{begin=Integer.parseInt(token);count=Integer.parseInt(t.word());}catch(NumberFormatException e){throw error("xref truncado");}
                if(begin<0||count<0||count>MAX_XREF||begin>(long)Integer.MAX_VALUE-count)throw error("xref excesivo");
                for(int i=0;i<count;i++){long pos;int gen;try{pos=Long.parseLong(t.word());gen=Integer.parseInt(t.word());}catch(NumberFormatException e){throw error("xref invalido");}String flag=t.word();if(!flag.equals("n")&&!flag.equals("f"))throw error("xref invalido");put(begin+i,new long[]{flag.equals("n")?1:0,pos,gen});}
            }trailer=dict(t.value(0));
        }else{
            Object obj=object(offset);if(!(obj instanceof Stream))throw error("xref stream ausente");Stream stream=(Stream)obj;trailer=stream.dict;
            if(!name(trailer.get("Type")).equals("XRef"))throw error("xref no reconocido");
            List<Object>w=array(trailer.get("W"));if(w.size()!=3)throw error("anchos xref invalidos");int[] widths=new int[3];for(int i=0;i<3;i++){widths[i]=(int)num(w.get(i),-1);if(widths[i]<0||widths[i]>8)throw error("ancho xref excesivo");}
            List<Object> ranges=trailer.containsKey("Index")?array(trailer.get("Index")):Arrays.asList(0L,trailer.get("Size"));if(ranges.size()%2!=0)throw error("rango xref invalido");byte[] data=decode(stream);int at=0;
            for(int i=0;i<ranges.size();i+=2){int begin=(int)num(ranges.get(i),-1),count=(int)num(ranges.get(i+1),-1);if(begin<0||count<0||count>MAX_XREF||begin>(long)Integer.MAX_VALUE-count)throw error("xref excesivo");
                for(int j=0;j<count;j++){long[] row=new long[3];for(int k=0;k<3;k++){for(int n=0;n<widths[k];n++){if(at>=data.length)throw error("xref stream truncado");row[k]=(row[k]<<8)|(data[at++]&255);}if(row[k]<0)throw error("offset excesivo");}if(widths[0]==0)row[0]=1;if(row[0]>2)throw error("tipo xref desconocido");put(begin+j,row);}
            }
        }
        if(root==null)root=trailer.get("Root");if(trailer.get("Encrypt")!=null)encrypted=true;
        long hybrid=num(trailer.get("XRefStm"),0),prev=num(trailer.get("Prev"),0);
        if(hybrid>0&&!seen.contains(hybrid))index(hybrid,seen);if(prev>0&&!seen.contains(prev))index(prev,seen);
    }
    private void put(int id,long[] entry)throws IOException{if(!xref.containsKey(id)){if(xref.size()>=MAX_XREF)throw error("demasiadas referencias");xref.put(id,entry);}}
    private void pageTree(Object node,Set<Integer> seen,int depth)throws IOException{
        check();if(depth>64||pages.size()>=100000)throw error("arbol de paginas excesivo");if(!(node instanceof Ref)||!seen.add(((Ref)node).id))throw error("arbol de paginas ciclico");
        Map<String,Object> d=dict(node);if(name(d.get("Type")).equals("Page")){pages.put(((Ref)node).id,pages.size());return;}
        for(Object kid:array(d.get("Kids")))pageTree(kid,seen,depth+1);
    }
    private static String text(Object value){
        if(value instanceof Name)return ((Name)value).value;if(!(value instanceof byte[]))return "";byte[] b=(byte[])value;
        if(b.length>=2&&b[0]==(byte)254&&b[1]==(byte)255)return new String(b,2,b.length-2,StandardCharsets.UTF_16BE);
        if(b.length>=3&&b[0]==(byte)239&&b[1]==(byte)187&&b[2]==(byte)191)return new String(b,3,b.length-3,StandardCharsets.UTF_8);
        // PDFDocEncoding (ISO 32000, anexo D), distinto de Windows-1252.
        String low="\u02d8\u02c7\u02c6\u02d9\u02dd\u02db\u02da\u02dc";
        String high="\u2022\u2020\u2021\u2026\u2014\u2013\u0192\u2044\u2039\u203a\u2212\u2030\u201e\u201c\u201d\u2018\u2019\u201a\u2122\ufb01\ufb02\u0141\u0152\u0160\u0178\u017d\u0131\u0142\u0153\u0161\u017e";
        StringBuilder valueText=new StringBuilder();for(byte raw:b){int c=raw&255;valueText.append(c>=24&&c<=31?low.charAt(c-24):c>=128&&c<=158?high.charAt(c-128):c==160?'\u20ac':c==127||c==159||c==173?'\ufffd':(char)c);}return valueText.toString();
    }
    private static String destinationKey(Object value){return value instanceof byte[]?new String((byte[])value,StandardCharsets.ISO_8859_1):value instanceof Name?((Name)value).value:"";}
    private void nameTree(Object node,Set<Integer> seen,int depth)throws IOException{
        if(depth>32||names.size()>10000)throw error("destinos excesivos");if(node instanceof Ref&&!seen.add(((Ref)node).id))throw error("destinos ciclicos");Map<String,Object>d=dict(node);
        if(d.containsKey("Names")){List<Object>a=array(d.get("Names"));if(a.size()%2!=0)throw error("destinos incompletos");for(int i=0;i<a.size();i+=2){names.put(destinationKey(a.get(i)),a.get(i+1));if(names.size()>10000)throw error("destinos excesivos");}}
        if(d.containsKey("Kids"))for(Object kid:array(d.get("Kids")))nameTree(kid,seen,depth+1);
    }
    private int destination(Object d,int depth)throws IOException{
        if(depth>16)return -1;d=resolve(d);if(d instanceof byte[]||d instanceof Name)return destination(names.get(destinationKey(d)),depth+1);
        if(d instanceof Map)return destination(dict(d).get("D"),depth+1);
        if(!(d instanceof List))return -1;List<Object>a=array(d);if(a.isEmpty())return -1;Object page=a.get(0);
        if(page instanceof Ref)return pages.getOrDefault(((Ref)page).id,-1);long n=num(page,-1);return n>=0&&n<pages.size()?(int)n:-1;
    }
    private void outlines(Object item,int depth,Set<Integer> seen,List<FolioContents.Entry> result)throws IOException{
        while(item!=null){check();if(depth>32||result.size()>=FolioContents.LIMIT)throw error("demasiados marcadores");if(!(item instanceof Ref)||!seen.add(((Ref)item).id))throw error("marcadores ciclicos");Map<String,Object>d=dict(item);
            Object dest=d.get("Dest");if(dest==null&&d.get("A")!=null){Map<String,Object>a=dict(d.get("A"));if(name(a.get("S")).equals("GoTo"))dest=a.get("D");}
            FolioContents.Entry e=new FolioContents.Entry(text(resolve(d.get("Title"))),-1,"",depth);e.page=destination(dest,0);result.add(e);
            if(d.get("First")!=null)outlines(d.get("First"),depth+1,seen,result);item=d.get("Next");
        }
    }
    public List<FolioContents.Entry> read()throws IOException{
        check();long start=Math.max(0,file.length()-65536);file.seek(start);byte[] tail=new byte[(int)(file.length()-start)];file.readFully(tail);String end=new String(tail,StandardCharsets.ISO_8859_1);int at=end.lastIndexOf("startxref");if(at<0)throw error("falta startxref");
        try{long offset=Long.parseLong(end.substring(at+9).trim().split("\\s+")[0]);index(offset,new HashSet<>());}catch(NumberFormatException e){throw error("startxref invalido");}
        if(encrypted)throw error("PDF cifrado; no se leen sus metadatos protegidos");Map<String,Object>catalog=dict(root);List<FolioContents.Entry> result=new ArrayList<>();if(catalog.get("Outlines")==null)return result;
        pageTree(catalog.get("Pages"),new HashSet<>(),0);
        if(catalog.get("Dests")!=null)names.putAll(dict(catalog.get("Dests")));
        if(catalog.get("Names")!=null){Map<String,Object>n=dict(catalog.get("Names"));if(n.get("Dests")!=null)nameTree(n.get("Dests"),new HashSet<>(),0);}
        outlines(dict(catalog.get("Outlines")).get("First"),0,new HashSet<>(),result);return result;
    }
    public void close()throws IOException{file.close();}
}
