package com.loklj.folioxr;

import java.io.*;
import java.util.*;
import javax.xml.parsers.*;
import org.xml.sax.*;
import org.xml.sax.ext.DefaultHandler2;

/** Subconjunto propio OOXML/ODF, sin macros, OLE, red ni estilos ejecutables. */
final class FolioOfficeXml {
    private final FolioDocumentSource out;
    private final Map<String,Node> styles=new HashMap<>();
    private final Map<String,String> relationships=new HashMap<>(), numbers=new HashMap<>();
    private boolean odt;
    FolioOfficeXml(FolioDocumentSource out){this.out=out;}
    static final class Node {
        final String name,uri;
        final Map<String,String> attrs=new HashMap<>();
        final List<Object> children=new ArrayList<>();
        Node(String name,String uri){this.name=name;this.uri=uri;}
        String a(String name){return attrs.getOrDefault(name,"");}
        Node child(String name){for(Object c:children)if(c instanceof Node && ((Node)c).name.equals(name))return (Node)c;return null;}
        List<Node> all(String name){List<Node> found=new ArrayList<>();collect(this,name,found);return found;}
        static void collect(Node n,String name,List<Node> found){if(n.name.equals(name))found.add(n);for(Object c:n.children)if(c instanceof Node)collect((Node)c,name,found);}
    }
    Node xml(String path)throws Exception {
        SAXParserFactory factory=SAXParserFactory.newInstance();factory.setNamespaceAware(true);
        XMLReader reader=factory.newSAXParser().getXMLReader();
        for(String feature:new String[]{"http://xml.org/sax/features/external-general-entities","http://xml.org/sax/features/external-parameter-entities"})
            try{reader.setFeature(feature,false);}catch(SAXException unsupported){/* Android: el manejador rechaza DTD y entidades. */}
        final Deque<Node> stack=new ArrayDeque<>();final Node[] root={null};
        DefaultHandler2 handler=new DefaultHandler2(){
            int nodes,chars;
            public void startDTD(String name,String publicId,String systemId)throws SAXException{throw new SAXException("DTD no permitido");}
            public InputSource resolveEntity(String publicId,String systemId)throws SAXException{throw new SAXException("Entidad externa no permitida");}
            public InputSource resolveEntity(String name,String publicId,String base,String systemId)throws SAXException{throw new SAXException("Entidad externa no permitida");}
            public void startElement(String uri,String local,String q,Attributes attrs)throws SAXException{
                try{out.check();}catch(IOException e){throw new SAXException(e);}
                if(++nodes>60000 || stack.size()>=64 || attrs.getLength()>64)throw new SAXException("XML excede limites de complejidad");
                Node node=new Node(local,uri);
                for(int i=0;i<attrs.getLength();i++)node.attrs.put(attrs.getLocalName(i),attrs.getValue(i));
                if(stack.isEmpty())root[0]=node;else stack.peek().children.add(node);stack.push(node);
            }
            public void endElement(String uri,String local,String q){stack.pop();}
            public void characters(char[] c,int start,int length)throws SAXException{
                chars+=length;if(chars>FolioDocumentSource.XML_LIMIT)throw new SAXException("XML demasiado grande");
                if(!stack.isEmpty())stack.peek().children.add(new String(c,start,length));
            }
            public void error(SAXParseException e)throws SAXException{throw e;}
            public void fatalError(SAXParseException e)throws SAXException{throw e;}
        };
        reader.setContentHandler(handler);reader.setEntityResolver(handler);reader.setErrorHandler(handler);
        // Obligatorio: si un parser no ofrece LexicalHandler, falla cerrado ante posibles DTD.
        reader.setProperty("http://xml.org/sax/properties/lexical-handler",handler);
        try(InputStream in=out.entry(path,FolioDocumentSource.XML_LIMIT)){reader.parse(new InputSource(in));}
        if(root[0]==null)throw new IOException("XML vacio");return root[0];
    }
    void convert(boolean odt)throws Exception {
        this.odt=odt;
        Node root,body;
        if(odt){
            if(!out.has("mimetype"))throw new IOException("ODT sin mimetype");
            try(InputStream in=out.entry("mimetype",128)){
                ByteArrayOutputStream b=new ByteArrayOutputStream();int c;while((c=in.read())!=-1)b.write(c);
                if(!"application/vnd.oasis.opendocument.text".equals(b.toString("UTF-8").trim()))throw new IOException("No es un documento ODT");
            }
            if(out.has("META-INF/manifest.xml") && !xml("META-INF/manifest.xml").all("encryption-data").isEmpty())
                throw new IOException("Formato a\u00fan no compatible: ODT cifrado; abre una copia sin proteccion.");
            if(out.has("styles.xml"))loadStyles(xml("styles.xml"));
            root=xml("content.xml");loadStyles(root);
            if(!root.uri.equals("urn:oasis:names:tc:opendocument:xmlns:office:1.0"))throw new IOException("Espacio XML ODT invalido");
            body=root.child("body");body=body==null?null:body.child("text");
        }else{
            if(!out.has("[Content_Types].xml"))throw new IOException("DOCX sin tipos de contenido");
            Node types=xml("[Content_Types].xml");boolean main=false;
            for(Node type:types.all("Override"))if(type.a("PartName").equals("/word/document.xml") && type.a("ContentType").equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"))main=true;
            if(!main)throw new IOException("Formato a\u00fan no compatible: paquete Word no DOCX o con macros.");
            if(out.has("word/_rels/document.xml.rels"))for(Node rel:xml("word/_rels/document.xml.rels").all("Relationship")){
                if(rel.a("TargetMode").equalsIgnoreCase("External"))continue;
                String path=rel.a("Target");if(path.startsWith("/word/"))path=path.substring(1);else path="word/"+path;
                if(rel.a("Type").endsWith("/image"))relationships.put(rel.a("Id"),path);
            }
            if(out.has("word/styles.xml"))loadStyles(xml("word/styles.xml"));
            if(out.has("word/numbering.xml")){
                Node numbering=xml("word/numbering.xml");Map<String,Node> abstractNums=new HashMap<>();
                for(Node n:numbering.all("abstractNum"))abstractNums.put(n.a("abstractNumId"),n);
                for(Node n:numbering.all("num")){
                    Node id=n.child("abstractNumId"), definition=id==null?null:abstractNums.get(id.a("val"));
                    if(definition!=null)for(Node level:definition.all("lvl")){
                        Node fmt=level.child("numFmt");numbers.put(n.a("numId")+":"+level.a("ilvl"),fmt!=null&&fmt.a("val").equals("bullet")?"ul":"ol");}
                }
            }
            root=xml("word/document.xml");
            if(!(root.uri.equals("http://schemas.openxmlformats.org/wordprocessingml/2006/main") || root.uri.equals("http://purl.oclc.org/ooxml/wordprocessingml/main")))throw new IOException("Espacio XML DOCX invalido");
            body=root.child("body");
        }
        if(body==null)throw new IOException("Documento sin cuerpo");
        emitBlocks(body);
    }
    private void loadStyles(Node root){for(Node n:root.all("style"))styles.put(n.a(odt?"name":"styleId"),n);
        if(odt)for(Node n:root.all("list-style"))styles.put(n.a("name"),n);
    }
    private void emitBlocks(Node parent)throws IOException {
        String list=null;StringBuilder group=new StringBuilder();
        for(Object item:parent.children){if(!(item instanceof Node))continue;Node node=(Node)item;
            String type=odt?null:listType(node);
            if(!Objects.equals(type,list) || group.length()>150000){
                if(list!=null){group.append("</").append(list).append('>');out.block(group.toString());group.setLength(0);}list=type;
                if(list!=null)group.append('<').append(list).append('>');
            }
            String html=render(node,0);
            if(list!=null){group.append("<li>").append(html).append("</li>");if(group.length()>FolioDocumentSource.BLOCK_LIMIT)throw new IOException("Lista demasiado grande");}
            else if(!html.trim().isEmpty())out.block(html);
        }
        if(list!=null){group.append("</").append(list).append('>');out.block(group.toString());}
    }
    private String listType(Node n){
        if(!n.name.equals("p"))return null;Node props=n.child("pPr"), num=props==null?null:props.child("numPr");
        if(num==null)return null;Node id=num.child("numId"), level=num.child("ilvl");
        if(id==null||id.a("val").equals("0"))return null;
        if(level!=null&&!level.a("val").equals("0"))out.warn("Listas multinivel simplificadas.");
        return numbers.getOrDefault(id.a("val")+":"+(level==null?"0":level.a("val")),"ul");
    }
    private String children(Node n,int depth)throws IOException {
        StringBuilder b=new StringBuilder();
        for(Object c:n.children){
            if(c instanceof Node)b.append(render((Node)c,depth+1));
            else if(odt || n.name.equals("t"))b.append(FolioDocumentSource.escape((String)c));
            if(b.length()>FolioDocumentSource.BLOCK_LIMIT)throw new IOException("Bloque XML demasiado grande");
        }
        return b.toString();
    }
    private String wrap(String tag,String content){return "<"+tag+">"+content+"</"+tag+">";}
    private String render(Node n,int depth)throws IOException {
        out.check();if(depth>64)throw new IOException("Documento demasiado anidado");
        String name=n.name;
        if(name.equals("object")||name.equals("object-ole")||name.equals("altChunk")||name.equals("oleObject")||name.equals("OLEObject")) {out.warn("Objetos incrustados omitidos; exporta a PDF para verlos.");return "";}
        if(odt)return renderOdt(n,depth);
        if(name.endsWith("Pr") || name.equals("sectPr") || name.equals("instrText") || name.equals("del"))return "";
        if(name.equals("blip"))return out.image(relationships.get(n.a("embed")));
        if(name.equals("imagedata"))return out.image(relationships.get(n.a("id")));
        if(name.equals("tab"))return "&#9;";
        if(name.equals("br")||name.equals("cr"))return "<br>";
        if(name.equals("p")){
            Node props=n.child("pPr"), style=props==null?null:props.child("pStyle"), outline=props==null?null:props.child("outlineLvl");
            int heading=heading(style==null?"":style.a("val"));
            if(outline!=null)heading=level(outline.a("val"),0)+1;
            return wrap(heading>=1&&heading<=6?"h"+heading:"p",children(n,depth));
        }
        if(name.equals("r")){
            Node props=n.child("rPr");String value=children(n,depth);
            boolean[] format={false,false};Node style=props==null?null:props.child("rStyle");applyStyle(style==null?"":style.a("val"),format,new HashSet<>());applyProps(props,format);
            if(format[0])value=wrap("strong",value);if(format[1])value=wrap("em",value);return value;
        }
        if(name.equals("tbl"))return wrap("table",children(n,depth));
        if(name.equals("tr"))return wrap("tr",children(n,depth));
        if(name.equals("tc"))return wrap("td",children(n,depth));
        if(name.equals("footnoteReference")||name.equals("endnoteReference")){out.warn("Notas al pie/final omitidas.");return "";}
        return children(n,depth);
    }
    private int heading(String id){
        Set<String> seen=new HashSet<>();
        while(!id.isEmpty()&&seen.add(id)&&seen.size()<32){
            if(id.matches("(?i)heading[1-6]"))return id.charAt(id.length()-1)-'0';
            Node style=styles.get(id);if(style==null)break;Node p=style.child("pPr"), outline=p==null?null:p.child("outlineLvl");
            if(outline!=null)return level(outline.a("val"),0)+1;
            Node parent=style.child("basedOn");id=parent==null?"":parent.a("val");
        }return 0;
    }
    private void applyStyle(String id,boolean[] format,Set<String> seen){
        if(!seen.add(id)||seen.size()>32)return;Node style=styles.get(id);if(style==null)return;
        Node parent=style.child("basedOn");applyStyle(odt?style.a("parent-style-name"):parent==null?"":parent.a("val"),format,seen);
        applyProps(style.child(odt?"text-properties":"rPr"),format);
    }
    private void applyProps(Node props,boolean[] format){
        if(props==null)return;
        if(odt){if(!props.a("font-weight").isEmpty())format[0]=props.a("font-weight").equals("bold");if(!props.a("font-style").isEmpty())format[1]=props.a("font-style").equals("italic");}
        else for(int i=0;i<2;i++){Node n=props.child(i==0?"b":"i");if(n!=null)format[i]=!Arrays.asList("0","false","off").contains(n.a("val"));}
    }
    private static int level(String value,int fallback){try{return Integer.parseInt(value);}catch(NumberFormatException e){return fallback;}}
    private String renderOdt(Node n,int depth)throws IOException {
        String name=n.name;
        if(name.equals("annotation")||name.equals("note")||name.equals("tracked-changes")){out.warn("Notas y anotaciones omitidas.");return "";}
        if(name.equals("image"))return out.image(n.a("href").replaceFirst("^\\./",""));
        if(name.equals("binary-data")){out.warn("Imagen ODT inline no compatible; usa una imagen PNG/JPEG del paquete.");return "";}
        if(name.equals("s")){int count=level(n.a("c"),1);if(count<1||count>1000)throw new IOException("Repeticion ODT excesiva");StringBuilder b=new StringBuilder();for(int i=0;i<count;i++)b.append("&#160;");return b.toString();}
        if(name.equals("tab"))return "&#9;";
        if(name.equals("line-break"))return "<br>";
        String value=children(n,depth);
        if(name.equals("span")||name.equals("p")||name.equals("h")){
            boolean[] format={false,false};applyStyle(n.a("style-name"),format,new HashSet<>());
            if(format[0])value=wrap("strong",value);if(format[1])value=wrap("em",value);
        }
        if(name.equals("p"))return wrap("p",value);
        if(name.equals("h"))return wrap("h"+Math.max(1,Math.min(6,level(n.a("outline-level"),1))),value);
        if(name.equals("list")){
            // El nivel exacto depende del estilo; el HTML mantiene la anidacion del contenido.
            Node style=styles.get(n.a("style-name"));
            return wrap(style!=null&&!style.all("list-level-style-number").isEmpty()?"ol":"ul",value);
        }
        if(name.equals("list-item"))return wrap("li",value);
        if(name.equals("table"))return wrap("table",value);
        if(name.equals("table-row")){if(level(n.a("number-rows-repeated"),1)>1)out.warn("Filas repetidas mostradas una sola vez.");return wrap("tr",value);}
        if(name.equals("table-cell")){if(level(n.a("number-columns-repeated"),1)>1)out.warn("Columnas repetidas mostradas una sola vez.");return wrap("td",value);}
        if(name.equals("covered-table-cell"))return "";
        return value;
    }
}
