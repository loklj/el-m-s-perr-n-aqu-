package com.loklj.folioxr;

import java.io.*;
import java.nio.charset.*;
import java.util.*;

/** Parser propio de grupos/control words RTF 1. No ejecuta campos, OLE ni objetos. */
final class FolioRtfReader {
    private final FolioDocumentSource out;
    private PushbackInputStream in;
    private long read;
    private int fallback;
    private char highSurrogate;
    private State state=new State();
    private final Deque<State> stack=new ArrayDeque<>();
    private final ByteArrayOutputStream bytes=new ByteArrayOutputStream();
    private final StringBuilder paragraph=new StringBuilder(),row=new StringBuilder(),cell=new StringBuilder();
    private final Map<Integer,Integer> headings=new HashMap<>();
    private int heading;
    private boolean list,table,rootSeen;
    private static final Set<String> SKIP=new HashSet<>(Arrays.asList("fonttbl","colortbl","info","filetbl","listtable","listoverridetable","revtbl","generator","header","headerl","headerr","footer","footerl","footerr","footnote","annotation","object","objdata","fldinst","datafield","themedata","datastore","xmlnstbl","nonshppict"));
    private static final class Picture {
        final ByteArrayOutputStream data=new ByteArrayOutputStream();int high=-1;String type="";
        void put(int b)throws IOException{if(data.size()>=FolioDocumentSource.IMAGE_LIMIT)throw new IOException("Imagen RTF demasiado grande");data.write(b);}
    }
    private static final class State {
        boolean bold,italic,skip,star,stylesheet;int uc=1,style=-1;String charset="windows-1252";Picture picture;boolean owner;
        State copy(){State s=new State();s.bold=bold;s.italic=italic;s.skip=skip;s.uc=uc;s.charset=charset;s.picture=picture;s.stylesheet=stylesheet;s.style=style;return s;}
    }
    FolioRtfReader(FolioDocumentSource out){this.out=out;}
    private int next()throws IOException {out.check();int c=in.read();if(c>=0&&++read>32L*1024*1024)throw new IOException("RTF excede 32 MiB; exporta a PDF");return c;}
    private void back(int c)throws IOException{if(c>=0){in.unread(c);read--;}}
    void read(InputStream source)throws IOException {
        in=new PushbackInputStream(source,1);
        int c=next();if(c!='{')throw new IOException("Cabecera RTF invalida");back(c);
        while((c=next())!=-1){
            if(c=='{') {flush();if(stack.size()>=128)throw new IOException("RTF demasiado anidado");stack.push(state);state=state.copy();}
            else if(c=='}'){
                flush();if(stack.isEmpty())throw new IOException("Llave RTF sin apertura");
                if(state.owner && !state.skip){
                    Picture p=state.picture;if(p.high!=-1)throw new IOException("Hexadecimal de imagen RTF incompleto");
                    if(p.type.isEmpty())out.warn("Imagen RTF no PNG/JPEG omitida.");else append(out.image(p.data.toByteArray(),p.type));
                }
                state=stack.pop();
                if(stack.isEmpty()){int tail;while((tail=next())!=-1)if(!Character.isWhitespace(tail))throw new IOException("Datos despues del grupo RTF");break;}
            } else if(c=='\\')control();
            else if(c!='\r'&&c!='\n')literal(c);
        }
        flush();if(highSurrogate!=0)throw new IOException("Unicode RTF incompleto");
        if(!rootSeen||!stack.isEmpty())throw new IOException("RTF truncado o sin cabecera");
        finishParagraph();if(cell.length()>0||row.length()>0)throw new IOException("Tabla RTF incompleta");
    }
    private void literal(int c)throws IOException {
        if(fallback>0){fallback--;return;}
        if(state.skip)return;
        if(state.picture!=null){
            if(Character.isWhitespace(c))return;int h=Character.digit((char)c,16);
            if(h<0)throw new IOException("Imagen RTF hexadecimal invalida");
            if(state.picture.high<0)state.picture.high=h;else{state.picture.put(state.picture.high*16+h);state.picture.high=-1;}return;
        }
        bytes.write(c);if(bytes.size()>FolioDocumentSource.BLOCK_LIMIT)throw new IOException("Texto RTF demasiado grande sin separacion");
    }
    private void flush()throws IOException {
        if(bytes.size()==0)return;
        try{styled(Charset.forName(state.charset).newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(java.nio.ByteBuffer.wrap(bytes.toByteArray())).toString());}
        catch(IllegalArgumentException|CharacterCodingException e){throw new IOException("Codificacion RTF no compatible o texto danado",e);}finally{bytes.reset();}
    }
    private void styled(String text)throws IOException{
        if(state.skip||state.picture!=null)return;
        if(highSurrogate!=0){
            if(text.isEmpty()||!Character.isLowSurrogate(text.charAt(0)))throw new IOException("Par sustituto RTF invalido");
            text=String.valueOf(highSurrogate)+text;highSurrogate=0;
        }
        if(text.length()==1&&Character.isHighSurrogate(text.charAt(0))){highSurrogate=text.charAt(0);return;}
        String html=FolioDocumentSource.escape(text);if(state.bold)html="<strong>"+html+"</strong>";if(state.italic)html="<em>"+html+"</em>";append(html);
    }
    private void append(String html)throws IOException{
        if(paragraph.length()+html.length()>FolioDocumentSource.BLOCK_LIMIT)throw new IOException("Parrafo RTF demasiado grande");paragraph.append(html);
    }
    private void finishParagraph()throws IOException {
        flush();if(paragraph.length()==0)return;
        String tag=heading>=1&&heading<=6?"h"+heading:"p",html="<"+tag+">"+paragraph+"</"+tag+">";paragraph.setLength(0);
        if(list)html="<ul><li>"+html+"</li></ul>";
        if(table){cell.append(html);if(cell.length()>FolioDocumentSource.BLOCK_LIMIT)throw new IOException("Celda RTF demasiado grande");}
        else out.block(html);
    }
    private void endCell()throws IOException{
        table=true;finishParagraph();row.append("<td>").append(cell).append("</td>");cell.setLength(0);
        if(row.length()>FolioDocumentSource.BLOCK_LIMIT)throw new IOException("Fila RTF demasiado grande");
    }
    private void control()throws IOException {
        int c=next();if(c<0)throw new IOException("Control RTF truncado");
        if(c=='\''){
            int a=Character.digit((char)next(),16),b=Character.digit((char)next(),16);if(a<0||b<0)throw new IOException("Escape RTF invalido");literal(a*16+b);return;
        }
        if(!letter(c)){
            if(c=='\\'||c=='{'||c=='}'){literal(c);return;}
            flush();if(c=='*')state.star=true;
            else if(c=='~'||c=='_'||c=='-'){if(fallback>0)fallback--;else styled(c=='~'?"\u00a0":c=='_'?"\u2011":"\u00ad");}
            return;
        }
        flush();StringBuilder word=new StringBuilder();
        do{if(word.length()>64)throw new IOException("Control RTF demasiado largo");word.append((char)c);c=next();}while(letter(c));
        boolean negative=c=='-',has=false;long value=0;if(negative)c=next();
        while(c>='0'&&c<='9'){has=true;value=value*10+c-'0';if(value>2147483647L)throw new IOException("Parametro RTF excesivo");c=next();}
        if(c!=' ')back(c);int n=(int)(negative?-value:value);String key=word.toString();
        if(key.equals("bin")){
            if(!has||n<0||n>FolioDocumentSource.IMAGE_LIMIT)throw new IOException("Binario RTF excesivo");
            for(int i=0;i<n;i++){int b=next();if(b<0)throw new IOException("Binario RTF truncado");if(state.picture!=null&&!state.skip)state.picture.put(b);}return;
        }
        if(key.equals("rtf")){if(rootSeen||stack.size()!=1||n!=1)throw new IOException("Version RTF no compatible");rootSeen=true;return;}
        if(!rootSeen)throw new IOException("Falta cabecera RTF");
        if(key.equals("stylesheet")){state.stylesheet=true;state.skip=true;return;}
        if(state.stylesheet){if(key.equals("s"))state.style=n;if(key.equals("outlinelevel")&&state.style>=0)headings.put(state.style,n+1);return;}
        if(state.star){state.star=false;if(!key.equals("shppict")){state.skip=true;return;}}
        if(SKIP.contains(key)){if(key.equals("object")||key.equals("footnote"))out.warn("Objetos o notas RTF omitidos.");state.skip=true;return;}
        if(key.equals("listtext")||key.equals("pntext")){list=true;state.skip=true;return;}
        if(state.skip)return;
        if(key.equals("pict")){state.picture=new Picture();state.owner=true;return;}
        if(state.picture!=null){if(key.equals("pngblip"))state.picture.type="png";if(key.equals("jpegblip"))state.picture.type="jpg";return;}
        switch(key){
            case "b":state.bold=!has||n!=0;break;
            case "i":state.italic=!has||n!=0;break;
            case "plain":state.bold=state.italic=false;break;
            case "ansicpg":if(!has||n<=0)throw new IOException("Pagina de codigo RTF invalida");state.charset=n==65001?"UTF-8":"windows-"+n;break;
            case "mac":state.charset="MacRoman";break;
            case "pc":state.charset="IBM437";break;
            case "pca":state.charset="IBM850";break;
            case "uc":if(!has||n<0||n>16)throw new IOException("Fallback Unicode RTF invalido");state.uc=n;break;
            case "u":if(!has||n<-32768||n>65535)throw new IOException("Unicode RTF invalido");styled(String.valueOf((char)n));fallback=state.uc;break;
            case "par":finishParagraph();break;
            case "line":append("<br>");break;
            case "tab":styled("\t");break;
            case "emdash":styled("\u2014");break;
            case "endash":styled("\u2013");break;
            case "bullet":styled("\u2022");break;
            case "lquote":styled("\u2018");break;
            case "rquote":styled("\u2019");break;
            case "ldblquote":styled("\u201c");break;
            case "rdblquote":styled("\u201d");break;
            case "pard":heading=0;list=false;table=false;break;
            case "outlinelevel":heading=n<6&&n>=0?n+1:0;break;
            case "s":heading=headings.getOrDefault(n,0);break;
            case "ls":list=n>0;out.warn("Numeracion de listas RTF simplificada a vinetas.");break;
            case "intbl":table=!has||n!=0;break;
            case "trowd":if(row.length()>0)throw new IOException("Fila RTF sin cierre");break;
            case "cell":endCell();break;
            case "row":if(paragraph.length()>0||cell.length()>0)endCell();out.block("<table><tr>"+row+"</tr></table>");row.setLength(0);table=false;break;
            case "page":finishParagraph();break;
            default:break; // Controles de distribucion no representan codigo ejecutable.
        }
    }
    private static boolean letter(int c){return c>='a'&&c<='z'||c>='A'&&c<='Z';}
}
