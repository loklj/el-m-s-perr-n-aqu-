package com.loklj.folioxr;
import java.util.*;

/**
 * Busqueda de texto dentro del libro abierto (encargo 15, parte de busqueda).
 * Busca sobre el mismo DOM que pagina el WebView: la pagina de cada coincidencia
 * sale de la columna donde quedo dibujada, asi coincide con la paginacion real
 * (fuente, interlineado y margen actuales). Ignora mayusculas y tildes.
 * PDF y comics se muestran como imagen: no hay capa de texto que recorrer.
 */
public final class FolioBookSearch {
    private FolioBookSearch() { }
    public static final int MAX_LIMIT = 50, MAX_QUERY = 120, SNIPPET = 40;

    /** Nucleo puro (sin DOM) para poder probarlo fuera de Android. */
    public static final String CORE =
        "function folioFold(s){return s.normalize('NFD').replace(/[\\u0300-\\u036f]/g,'').toLowerCase();}\n" +
        "function folioFind(texts,query,skip,limit){\n" +
        " var q=folioFold(query).replace(/\\s+/g,' ').trim();var out={hits:[],more:false};if(!q)return out;\n" +
        " var f=[],map=[],orig=[],base=0;\n" +
        " for(var t=0;t<texts.length;t++){var s=texts[t];orig.push(s);\n" +
        "  for(var i=0;i<s.length;){var cp=s.codePointAt(i),n=cp>0xffff?2:1,g=folioFold(String.fromCodePoint(cp));\n" +
        "   if(/\\s/.test(g))g=' ';\n" +
        "   if(g===' '&&(f.length===0||f[f.length-1]===' ')){i+=n;continue;}\n" +
        "   for(var k=0;k<g.length;k++){f.push(g[k]);map.push([t,i,i+n,base+i,base+i+n]);}\n" +
        "   i+=n;}\n" +
        "  base+=s.length;}\n" +
        " var all=f.join(''),text=orig.join(''),pos=0,seen=0;\n" +
        " while((pos=all.indexOf(q,pos))>=0){\n" +
        "  if(seen>=skip){if(out.hits.length>=limit){out.more=true;break;}\n" +
        "   var a=map[pos],b=map[pos+q.length-1],from=Math.max(0,a[3]-" + SNIPPET + "),to=Math.min(text.length,b[4]+" + SNIPPET + ");\n" +
        "   var snip=text.slice(from,to).replace(/\\s+/g,' ').trim();\n" +
        "   out.hits.push({t0:a[0],i0:a[1],t1:b[0],i1:b[2],text:(from>0?'\\u2026':'')+snip+(to<text.length?'\\u2026':'')});}\n" +
        "  seen++;pos+=q.length;}\n" +
        " return out;}\n";

    /** Script completo para evaluateJavascript: devuelve {hits:[{page,text}],more}. */
    public static String script(String query, int skip, int limit) {
        return "(function(){" + CORE +
            "var keep=function(n){for(var p=n.parentNode;p&&p!==document.body;p=p.parentNode){var t=p.nodeName;" +
            "if(t==='STYLE'||t==='SCRIPT'||t==='TITLE')return NodeFilter.FILTER_REJECT;}return NodeFilter.FILTER_ACCEPT;};" +
            "var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT,{acceptNode:keep}),nodes=[],texts=[];" +
            "while(w.nextNode()){nodes.push(w.currentNode);texts.push(w.currentNode.nodeValue);}" +
            "document.body.style.transform='none';" +
            "var r=folioFind(texts," + FolioContents.quote(clip(query)) + "," + Math.max(0, skip) + "," + clamp(limit) + ");" +
            "var hits=r.hits.map(function(h){var g=document.createRange(),page=-1;" +
            "try{g.setStart(nodes[h.t0],h.i0);g.setEnd(nodes[h.t1],h.i1);var rs=g.getClientRects();" +
            "if(rs.length)page=Math.max(0,Math.floor(rs[0].left/596));}catch(e){}" +
            "return {page:page,text:h.text};});" +
            "return {hits:hits,more:r.more};})()";
    }

    public static String clip(String query) {
        String q = query == null ? "" : query.trim();
        return q.length() > MAX_QUERY ? q.substring(0, MAX_QUERY) : q;
    }
    public static int clamp(int limit) { return Math.max(1, Math.min(MAX_LIMIT, limit)); }

    /** Cursor "capitulo:saltar" para «Más resultados»; vacio = desde el principio. */
    public static int[] cursor(String text) {
        int[] c = { 0, 0 };
        if (text == null || text.isEmpty()) return c;
        String[] parts = text.split(":");
        try {
            c[0] = Math.max(0, Integer.parseInt(parts[0].trim()));
            if (parts.length > 1) c[1] = Math.max(0, Integer.parseInt(parts[1].trim()));
        } catch (NumberFormatException e) { c[0] = 0; c[1] = 0; }
        return c;
    }

    /** PDF y comics no tienen texto que el lector pueda recorrer en Android 14. */
    public static boolean searchable(String path) {
        String p = path == null ? "" : path.toLowerCase(Locale.ROOT);
        return p.endsWith(".epub") || p.endsWith(".txt") || p.endsWith(".mobi") || p.endsWith(".azw") ||
            p.endsWith(".azw3") || p.endsWith(".docx") || p.endsWith(".odt") || p.endsWith(".rtf");
    }

    public static final class Hit {
        public final int page; public final String text;
        public Hit(int page, String text) { this.page = page; this.text = text == null ? "" : text; }
    }

    /** Respuesta para Unity (JsonUtility): {"hits":[{"page":n,"text":"…"}],"next":"c:s","message":"…"}. */
    public static String json(List<Hit> hits, String next, String message) {
        StringBuilder b = new StringBuilder("{\"hits\":[");
        for (int i = 0; i < hits.size(); i++) {
            Hit h = hits.get(i); if (i > 0) b.append(',');
            b.append("{\"page\":").append(h.page).append(",\"text\":").append(FolioContents.quote(h.text)).append('}');
        }
        return b.append("],\"next\":").append(FolioContents.quote(next == null ? "" : next))
            .append(",\"message\":").append(FolioContents.quote(message == null ? "" : message)).append('}').toString();
    }
}
