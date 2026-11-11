package com.loklj.folioxr;

import java.io.*;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.zip.*;
import javax.xml.parsers.*;
import org.w3c.dom.*;

/** Contenedor propio: nunca extrae rutas al disco ni concatena todo el libro. */
public final class FolioEpubContainer implements Closeable {
    final ZipFile zip;
    final List<String> spine = new ArrayList<>();
    final Map<String, String> navigation = new LinkedHashMap<>();
    final Set<String> fonts = new HashSet<>();
    static final int MAX_RESOURCE = 8 * 1024 * 1024;
    static final class DrmException extends IOException {
        DrmException() { super("Este libro tiene DRM y no se puede abrir"); }
    }
    FolioEpubContainer(String path) throws Exception {
        zip = new ZipFile(path);
        try {
            // Algunos EPUB reales omiten "mimetype"; basta con que tengan container.xml.
            if (zip.getEntry("mimetype") != null &&
                !"application/epub+zip".equals(new String(read("mimetype", 128), StandardCharsets.US_ASCII).trim()))
                throw new IOException("No es un EPUB");
            if (zip.getEntry("META-INF/container.xml") == null) throw new IOException("No es un EPUB (falta container.xml)");
            if (zip.getEntry("META-INF/rights.xml") != null) throw new DrmException();
            if (zip.getEntry("META-INF/encryption.xml") != null) {
                NodeList methods = xml("META-INF/encryption.xml").getElementsByTagNameNS("*", "EncryptionMethod");
                if (methods.getLength() == 0) throw new IOException("Proteccion no identificada");
                for (int i = 0; i < methods.getLength(); i++) {
                    String algorithm = ((Element) methods.item(i)).getAttribute("Algorithm");
                    if (!algorithm.equals("http://www.idpf.org/2008/embedding") &&
                        !algorithm.equals("http://ns.adobe.com/pdf/enc#RC")) throw new DrmException();
                }
                // No se desofuscan fuentes: siempre se usan las del sistema.
            }
            NodeList roots = xml("META-INF/container.xml").getElementsByTagNameNS("*", "rootfile");
            if (roots.getLength() == 0) throw new IOException("Falta OPF");
            String opf = resolve("", ((Element)roots.item(0)).getAttribute("full-path"));
            Document packageDoc = xml(opf);
            Map<String, String> items = new HashMap<>();
            String nav = null, ncx = null;
            NodeList manifest = packageDoc.getElementsByTagNameNS("*", "item");
            for (int i = 0; i < manifest.getLength(); i++) {
                Element item = (Element) manifest.item(i);
                String resource = resolve(opf, item.getAttribute("href"));
                items.put(item.getAttribute("id"), resource);
                String type = item.getAttribute("media-type");
                if (type.contains("font") || resource.matches("(?i).*\\.(ttf|otf|woff2?)$")) fonts.add(resource);
                if (Arrays.asList(item.getAttribute("properties").split("\\s+")).contains("nav")) nav = resource;
                if (type.equals("application/x-dtbncx+xml")) ncx = resource;
            }
            NodeList refs = packageDoc.getElementsByTagNameNS("*", "itemref");
            if (refs.getLength() > 10000) throw new IOException("Demasiados capitulos");
            for (int i = 0; i < refs.getLength(); i++) {
                Element ref = (Element)refs.item(i);
                if ("no".equals(ref.getAttribute("linear"))) continue;
                String resource = items.get(ref.getAttribute("idref"));
                if (resource == null || zip.getEntry(resource) == null) throw new IOException("Spine incompleto");
                spine.add(resource);
            }
            if (spine.isEmpty()) throw new IOException("EPUB sin capitulos");
            if (nav != null) readNavigation(nav, "a", "href");
            else if (ncx != null) readNavigation(ncx, "content", "src");
        } catch (Exception e) { zip.close(); throw e; }
    }
    private void readNavigation(String file, String tag, String attribute) throws Exception {
        NodeList nodes = xml(file).getElementsByTagNameNS("*", tag);
        for (int i = 0; i < nodes.getLength(); i++) {
            Element node = (Element)nodes.item(i);
            String target = resolve(file, node.getAttribute(attribute));
            navigation.put(target, (tag.equals("content") ? node.getParentNode() : node).getTextContent().trim());
        }
    }
    static String resolve(String base, String relative) throws IOException {
        try {
            URI root = new URI("https", "folio.invalid", "/" + base, null);
            URI uri = root.resolve(relative).normalize();
            if (!"https".equals(uri.getScheme()) || !"folio.invalid".equals(uri.getHost()) || uri.getQuery() != null)
                throw new IOException("Recurso externo no permitido");
            String path = uri.getPath().substring(1);
            if (path.contains("\\") || Arrays.asList(path.split("/")).contains("..")) throw new IOException("Ruta no valida");
            return path;
        } catch (java.net.URISyntaxException e) { throw new IOException(e); }
    }
    synchronized byte[] read(String path, int limit) throws IOException {
        ZipEntry entry = zip.getEntry(path);
        if (entry == null) throw new FileNotFoundException("Recurso EPUB ausente");
        if (entry.getSize() > limit) throw new IOException("Recurso EPUB supera el limite de " + limit + " bytes");
        try (InputStream in = zip.getInputStream(entry); ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192]; int n, total = 0;
            while ((n = in.read(buffer)) != -1) {
                total += n;
                if (total > limit) throw new IOException("Recurso EPUB demasiado grande");
                out.write(buffer, 0, n);
            }
            return out.toByteArray();
        }
    }
    Document xml(String path) throws Exception {
        DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        // Revision de Claude: el DocumentBuilderFactory de Android solo admite las caracteristicas
        // "namespaces" y "validation"; cualquier otra lanza ParserConfigurationException y ningun EPUB
        // abriria. Igual se rechazan las declaraciones <!ENTITY> mas abajo y KXml no resuelve externas.
        try {
            factory.setFeature("http://xml.org/sax/features/external-general-entities", false);
            factory.setFeature("http://xml.org/sax/features/external-parameter-entities", false);
        } catch (ParserConfigurationException unsupported) {
            // Android: sin estas opciones; la defensa es el rechazo de <!ENTITY>.
        }
        DocumentBuilder builder = factory.newDocumentBuilder();
        builder.setEntityResolver((publicId, systemId) -> new org.xml.sax.InputSource(new StringReader("")));
        byte[] data = read(path, 2 * 1024 * 1024);
        // Rechazar declaraciones internas evita expansion de entidades recursivas.
        String declaration = new String(data, StandardCharsets.UTF_8).replace("\u0000", "");
        if (declaration.contains("<!ENTITY")) throw new IOException("Entidades XML no admitidas");
        return builder.parse(new ByteArrayInputStream(data));
    }
    public synchronized void close() throws IOException { zip.close(); }
}
