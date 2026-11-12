package com.loklj.folioxr;
import java.util.*;

/** Destino semantico de una fuente HTML y resultado paginado del protocolo JNI. */
public final class FolioContents {
    public static final int LIMIT=2048;
    public static final class Entry {
        public final String title,fragment;
        public final int chapter,depth;
        public int page=-1;
        public Entry(String title,int chapter,String fragment,int depth){this.title=FolioReadingSettings.label(title);this.chapter=chapter;this.fragment=fragment==null?"":fragment;this.depth=Math.max(0,Math.min(depth,16));}
    }
    public static String quote(String text){
        StringBuilder b=new StringBuilder("\"");
        for(int i=0;i<text.length();i++){char c=text.charAt(i);if(c=='"'||c=='\\')b.append('\\').append(c);else if(c<32||c=='\u2028'||c=='\u2029')b.append(String.format(java.util.Locale.ROOT,"\\u%04x",(int)c));else b.append(c);}
        return b.append('"').toString();
    }
    public static String json(List<Entry> list){
        StringBuilder b=new StringBuilder("{\"entries\":[");boolean first=true;
        for(Entry e:list){if(!first)b.append(',');first=false;b.append("{\"title\":").append(quote(e.title)).append(",\"page\":").append(e.page).append(",\"depth\":").append(e.depth).append('}');}
        return b.append("],\"message\":").append(quote(list.size()>=LIMIT?"Indice limitado a 2048 entradas.":"")).append('}').toString();
    }
}
