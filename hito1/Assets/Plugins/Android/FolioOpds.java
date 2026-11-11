package com.loklj.folioxr;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URI;
import java.net.URL;
import java.security.KeyStore;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
import javax.xml.parsers.DocumentBuilder;
import javax.xml.parsers.DocumentBuilderFactory;

/**
 * Folio XR - Catalogos OPDS 1.2 y 2.0 (encargo 14, escrito por Claude).
 *
 * Servidores guardados en SharedPreferences; la contrasena se cifra con una clave AES-GCM del
 * Android Keystore (nunca en texto plano). Unity pide paginas del catalogo y descargas por clave
 * y consulta el estado por sondeo, igual que los renderizadores. Las adquisiciones con DRM
 * (Adobe ADEPT, Readium LCP o indirectas hacia ellas) se muestran como no legibles y no se descargan.
 */
public final class FolioOpds {

    private static final String TAG = "FolioXR";
    private static final String PREFS = "folio_opds", KEY_ALIAS = "folioxr_opds";
    private static final int MAX_FEED = 4 * 1024 * 1024, MAX_REDIRECTS = 5;
    private static final ExecutorService WORKER = Executors.newFixedThreadPool(2);
    private static final ConcurrentHashMap<String, String> STATUS = new ConcurrentHashMap<>();
    private static Context app;

    private FolioOpds() {
    }

    public static void init(Context context) {
        if (app == null && context != null) {
            app = context.getApplicationContext();
        }
    }

    // ------------------------------------------------------------------ Servidores

    private static SharedPreferences prefs() {
        return app.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private static JSONArray stored() {
        try {
            return new JSONArray(prefs().getString("servers", "[]"));
        } catch (Throwable t) {
            return new JSONArray();
        }
    }

    /** Lista para el menu: id, nombre y URL (sin credenciales). */
    public static String listServersJson() {
        try {
            JSONArray out = new JSONArray();
            if (app != null) {
                JSONArray all = stored();
                for (int i = 0; i < all.length(); i++) {
                    JSONObject s = all.getJSONObject(i);
                    out.put(new JSONObject().put("id", s.getString("id")).put("name", s.getString("name"))
                            .put("url", s.getString("url")));
                }
            }
            return new JSONObject().put("servers", out).toString();
        } catch (Throwable t) {
            return "{\"servers\":[]}";
        }
    }

    /** Agrega un servidor. Devuelve "" si salio bien o el motivo del rechazo. */
    public static synchronized String addServer(String name, String url, String user, String password) {
        try {
            if (app == null) {
                return "La app no esta lista.";
            }
            url = url == null ? "" : url.trim();
            URI uri = new URI(url);
            String scheme = uri.getScheme() == null ? "" : uri.getScheme().toLowerCase(Locale.US);
            if (!("http".equals(scheme) || "https".equals(scheme)) || uri.getHost() == null) {
                return "La dirección debe empezar con http:// o https://";
            }
            JSONObject server = new JSONObject().put("id", UUID.randomUUID().toString())
                    .put("name", name == null || name.trim().length() == 0 ? uri.getHost() : name.trim())
                    .put("url", url).put("user", user == null ? "" : user.trim());
            if (password != null && password.length() > 0) {
                Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
                cipher.init(Cipher.ENCRYPT_MODE, key());
                server.put("iv", Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP));
                server.put("secret", Base64.encodeToString(cipher.doFinal(password.getBytes("UTF-8")), Base64.NO_WRAP));
            }
            JSONArray all = stored();
            all.put(server);
            prefs().edit().putString("servers", all.toString()).apply();
            Log.i(TAG, "[FolioXR] OPDS: servidor agregado " + server.getString("name"));
            return "";
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] OPDS: no se pudo agregar el servidor", t);
            return "No se pudo guardar el servidor.";
        }
    }

    public static synchronized boolean removeServer(String id) {
        JSONArray all = stored(), kept = new JSONArray();
        boolean removed = false;
        for (int i = 0; i < all.length(); i++) {
            JSONObject s = all.optJSONObject(i);
            if (s == null) {
                continue;
            }
            if (id.equals(s.optString("id"))) {
                removed = true;
            } else {
                kept.put(s);
            }
        }
        prefs().edit().putString("servers", kept.toString()).apply();
        return removed;
    }

    private static SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(KEY_ALIAS)) {
            return ((KeyStore.SecretKeyEntry) store.getEntry(KEY_ALIAS, null)).getSecretKey();
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256).build());
        return generator.generateKey();
    }

    private static JSONObject server(String id) throws Exception {
        JSONArray all = stored();
        for (int i = 0; i < all.length(); i++) {
            JSONObject s = all.getJSONObject(i);
            if (id.equals(s.getString("id"))) {
                return s;
            }
        }
        throw new Exception("El servidor ya no existe.");
    }

    private static String authorization(JSONObject server) throws Exception {
        String user = server.optString("user", "");
        if (user.length() == 0) {
            return null;
        }
        String password = "";
        if (server.has("secret")) {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key(),
                    new GCMParameterSpec(128, Base64.decode(server.getString("iv"), Base64.NO_WRAP)));
            password = new String(cipher.doFinal(Base64.decode(server.getString("secret"), Base64.NO_WRAP)), "UTF-8");
        }
        return "Basic " + Base64.encodeToString((user + ":" + password).getBytes("UTF-8"), Base64.NO_WRAP);
    }

    // ------------------------------------------------------------------ Estado por clave

    public static String getStatus(String key) {
        return STATUS.get(key);
    }

    public static void clearStatus(String key) {
        STATUS.remove(key);
    }

    // ------------------------------------------------------------------ Catalogo

    /** Pide una pagina del catalogo (url vacia = raiz del servidor). Resultado: "OK:" + JSON normalizado. */
    public static void requestFeed(final String serverId, final String url, final String key) {
        STATUS.put(key, "PENDING");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    JSONObject server = server(serverId);
                    String target = url == null || url.length() == 0 ? server.getString("url") : url;
                    HttpURLConnection connection = open(target, authorization(server), "application/opds+json, "
                            + "application/atom+xml;profile=opds-catalog, application/atom+xml, */*;q=0.5");
                    byte[] body;
                    String finalUrl;
                    try {
                        body = readLimited(connection.getInputStream(), MAX_FEED);
                        finalUrl = connection.getURL().toString();
                    } finally {
                        connection.disconnect();
                    }
                    STATUS.put(key, "OK:" + parse(body, finalUrl).toString());
                } catch (Throwable t) {
                    STATUS.put(key, "ERROR:" + describe(t));
                    Log.w(TAG, "[FolioXR] OPDS: no se pudo leer el catalogo (" + t + ")");
                }
            }
        });
    }

    private static HttpURLConnection open(String target, String auth, String accept) throws Exception {
        String current = target;
        for (int i = 0; i <= MAX_REDIRECTS; i++) {
            HttpURLConnection connection = (HttpURLConnection) new URL(current).openConnection();
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(10000);
            connection.setReadTimeout(30000);
            connection.setRequestProperty("User-Agent", "FolioXR/1.0 (Meta Quest)");
            connection.setRequestProperty("Accept", accept);
            // Las credenciales solo viajan al mismo servidor que se configuro.
            if (auth != null && new URL(current).getHost().equalsIgnoreCase(new URL(target).getHost())) {
                connection.setRequestProperty("Authorization", auth);
            }
            int code = connection.getResponseCode();
            if (code >= 300 && code < 400 && connection.getHeaderField("Location") != null) {
                current = new URL(new URL(current), connection.getHeaderField("Location")).toString();
                connection.disconnect();
                continue;
            }
            if (code == 401 || code == 403) {
                connection.disconnect();
                throw new Exception("AUTH");
            }
            if (code >= 400) {
                connection.disconnect();
                throw new Exception("HTTP " + code);
            }
            return connection;
        }
        throw new Exception("Demasiadas redirecciones");
    }

    private static String describe(Throwable t) {
        String text = String.valueOf(t.getMessage());
        if ("AUTH".equals(text)) {
            return "Usuario o contraseña incorrectos.";
        }
        if (text.contains("Cleartext")) {
            return "El servidor usa http sin cifrar y el sistema lo bloqueó.";
        }
        if (t instanceof java.net.UnknownHostException || t instanceof java.net.ConnectException
                || t instanceof java.net.SocketTimeoutException) {
            return "No se pudo conectar con el servidor. Revisa la dirección y la red Wi-Fi.";
        }
        if (text.startsWith("HTTP ")) {
            return "El servidor respondió con un error (" + text + ").";
        }
        return "No se pudo leer el catálogo.";
    }

    private static byte[] readLimited(InputStream in, int limit) throws Exception {
        try {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[16384];
            int n, total = 0;
            while ((n = in.read(buffer)) >= 0) {
                total += n;
                if (total > limit) {
                    throw new Exception("Catálogo demasiado grande");
                }
                out.write(buffer, 0, n);
            }
            return out.toByteArray();
        } finally {
            in.close();
        }
    }

    /** Formato normalizado para Unity: {title, entries:[{title, author, nav, books:[{href,type,drm}]}], next, prev}. */
    static JSONObject parse(byte[] body, String base) throws Exception {
        int start = 0;
        while (start < body.length && (body[start] == ' ' || body[start] == '\n' || body[start] == '\r'
                || body[start] == '\t' || (body[start] & 0xff) == 0xEF || (body[start] & 0xff) == 0xBB
                || (body[start] & 0xff) == 0xBF)) {
            start++;
        }
        if (start < body.length && body[start] == '{') {
            return parseJson(new JSONObject(new String(body, start, body.length - start, "UTF-8")), base);
        }
        return parseAtom(body, base);
    }

    private static String resolve(String base, String href) {
        try {
            return new URL(new URL(base), href).toString();
        } catch (Throwable t) {
            return href;
        }
    }

    private static boolean drmType(String type) {
        String t = type == null ? "" : type.toLowerCase(Locale.US);
        return t.contains("adobe.adept") || t.contains("readium.lcp") || t.contains("vnd.adobe.adept");
    }

    private static boolean readableType(String type) {
        String t = type == null ? "" : type.toLowerCase(Locale.US);
        return t.contains("epub") || t.contains("pdf") || t.contains("cbz") || t.contains("comicbook")
                || t.contains("cbr") || t.contains("mobipocket") || t.contains("x-mobi") || t.contains("azw")
                || t.startsWith("text/plain") || t.contains("application/zip");
    }

    private static JSONObject parseAtom(byte[] body, String base) throws Exception {
        DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
        factory.setNamespaceAware(true);
        if (new String(body, "UTF-8").contains("<!ENTITY")) {
            throw new Exception("Entidades XML no admitidas");
        }
        DocumentBuilder builder = factory.newDocumentBuilder();
        Document doc = builder.parse(new ByteArrayInputStream(body));
        Element feed = doc.getDocumentElement();
        JSONObject out = new JSONObject();
        out.put("title", childText(feed, "title"));
        JSONArray entries = new JSONArray();
        NodeList children = feed.getChildNodes();
        for (int i = 0; i < children.getLength(); i++) {
            Node node = children.item(i);
            if (!(node instanceof Element)) {
                continue;
            }
            Element element = (Element) node;
            String local = localName(element);
            if ("link".equals(local)) {
                String rel = element.getAttribute("rel");
                String href = resolve(base, element.getAttribute("href"));
                if ("next".equals(rel)) {
                    out.put("next", href);
                } else if ("previous".equals(rel) || "prev".equals(rel)) {
                    out.put("prev", href);
                } else if ("search".equals(rel)) {
                    out.put("search", href);
                }
            } else if ("entry".equals(local)) {
                JSONObject entry = new JSONObject();
                entry.put("title", childText(element, "title"));
                Element author = firstChild(element, "author");
                entry.put("author", author != null ? childText(author, "name") : "");
                JSONArray books = new JSONArray();
                NodeList links = element.getChildNodes();
                for (int j = 0; j < links.getLength(); j++) {
                    if (!(links.item(j) instanceof Element) || !"link".equals(localName((Element) links.item(j)))) {
                        continue;
                    }
                    Element link = (Element) links.item(j);
                    String rel = link.getAttribute("rel"), type = link.getAttribute("type");
                    String href = resolve(base, link.getAttribute("href"));
                    if (rel.startsWith("http://opds-spec.org/acquisition")) {
                        boolean drm = drmType(type);
                        NodeList indirect = link.getElementsByTagNameNS("*", "indirectAcquisition");
                        for (int k = 0; k < indirect.getLength(); k++) {
                            drm |= drmType(((Element) indirect.item(k)).getAttribute("type"));
                        }
                        if (drm || readableType(type)) {
                            books.put(new JSONObject().put("href", href).put("type", type).put("drm", drm));
                        }
                    } else if (!entry.has("nav") && (type.contains("opds-catalog") || "subsection".equals(rel)
                            || rel.length() == 0 && type.contains("atom+xml"))) {
                        entry.put("nav", href);
                    }
                }
                entry.put("books", books);
                if (entry.has("nav") || books.length() > 0) {
                    entries.put(entry);
                }
            }
        }
        out.put("entries", entries);
        return out;
    }

    private static JSONObject parseJson(JSONObject feed, String base) throws Exception {
        JSONObject out = new JSONObject();
        JSONObject metadata = feed.optJSONObject("metadata");
        out.put("title", metadata != null ? metadata.optString("title", "") : "");
        JSONArray links = feed.optJSONArray("links");
        for (int i = 0; links != null && i < links.length(); i++) {
            JSONObject link = links.getJSONObject(i);
            String rel = relOf(link);
            if ("next".equals(rel)) {
                out.put("next", resolve(base, link.optString("href")));
            } else if ("previous".equals(rel) || "prev".equals(rel)) {
                out.put("prev", resolve(base, link.optString("href")));
            } else if ("search".equals(rel)) {
                out.put("search", resolve(base, link.optString("href")));
            }
        }
        JSONArray entries = new JSONArray();
        addJsonEntries(feed, base, entries);
        JSONArray groups = feed.optJSONArray("groups");
        for (int i = 0; groups != null && i < groups.length(); i++) {
            addJsonEntries(groups.getJSONObject(i), base, entries);
        }
        out.put("entries", entries);
        return out;
    }

    private static String relOf(JSONObject link) {
        Object rel = link.opt("rel");
        if (rel instanceof JSONArray) {
            return ((JSONArray) rel).optString(0, "");
        }
        return rel == null ? "" : rel.toString();
    }

    private static void addJsonEntries(JSONObject container, String base, JSONArray entries) throws Exception {
        JSONArray navigation = container.optJSONArray("navigation");
        for (int i = 0; navigation != null && i < navigation.length(); i++) {
            JSONObject nav = navigation.getJSONObject(i);
            entries.put(new JSONObject().put("title", nav.optString("title", "")).put("author", "")
                    .put("nav", resolve(base, nav.optString("href"))).put("books", new JSONArray()));
        }
        JSONArray publications = container.optJSONArray("publications");
        for (int i = 0; publications != null && i < publications.length(); i++) {
            JSONObject publication = publications.getJSONObject(i);
            JSONObject metadata = publication.optJSONObject("metadata");
            String author = "";
            if (metadata != null) {
                Object a = metadata.opt("author");
                if (a instanceof JSONObject) {
                    author = ((JSONObject) a).optString("name", "");
                } else if (a instanceof JSONArray && ((JSONArray) a).length() > 0) {
                    Object first = ((JSONArray) a).opt(0);
                    author = first instanceof JSONObject ? ((JSONObject) first).optString("name", "") : String.valueOf(first);
                } else if (a != null) {
                    author = a.toString();
                }
            }
            JSONArray books = new JSONArray();
            JSONArray links = publication.optJSONArray("links");
            for (int j = 0; links != null && j < links.length(); j++) {
                JSONObject link = links.getJSONObject(j);
                String rel = relOf(link), type = link.optString("type", "");
                if (!rel.startsWith("http://opds-spec.org/acquisition")) {
                    continue;
                }
                boolean drm = drmType(type);
                JSONObject properties = link.optJSONObject("properties");
                if (properties != null && properties.has("indirectAcquisition")) {
                    drm |= properties.optJSONArray("indirectAcquisition") != null
                            && properties.optJSONArray("indirectAcquisition").toString().toLowerCase(Locale.US).matches(".*(adept|lcp).*");
                }
                if (drm || readableType(type)) {
                    books.put(new JSONObject().put("href", resolve(base, link.optString("href"))).put("type", type)
                            .put("drm", drm));
                }
            }
            entries.put(new JSONObject().put("title", metadata != null ? metadata.optString("title", "") : "")
                    .put("author", author).put("books", books));
        }
    }

    private static String localName(Element element) {
        String local = element.getLocalName();
        return local != null ? local : element.getTagName();
    }

    private static Element firstChild(Element parent, String local) {
        NodeList children = parent.getChildNodes();
        for (int i = 0; i < children.getLength(); i++) {
            if (children.item(i) instanceof Element && local.equals(localName((Element) children.item(i)))) {
                return (Element) children.item(i);
            }
        }
        return null;
    }

    private static String childText(Element parent, String local) {
        Element child = firstChild(parent, local);
        return child == null ? "" : child.getTextContent().trim();
    }

    // ------------------------------------------------------------------ Descarga

    /** Descarga una adquisicion a la biblioteca. Estado: "PENDING:copiado/total", "OK:mensaje" o "ERROR:mensaje". */
    public static void requestDownload(final String serverId, final String href, final String type, final String title,
                                       final String key) {
        STATUS.put(key, "PENDING:0/0");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                File part = null;
                try {
                    if (drmType(type)) {
                        STATUS.put(key, "ERROR:Este libro tiene DRM y no se puede abrir.");
                        return;
                    }
                    JSONObject server = server(serverId);
                    HttpURLConnection connection = open(href, authorization(server), "*/*");
                    try {
                        long total = connection.getContentLengthLong();
                        if (total > 0 && FolioLibrary.freeSpace() < total + FolioLibrary.SPACE_MARGIN) {
                            STATUS.put(key, "ERROR:No hay espacio suficiente para «" + title + "».");
                            return;
                        }
                        part = FolioLibrary.newPartFile();
                        long written = 0;
                        InputStream in = connection.getInputStream();
                        FileOutputStream out = new FileOutputStream(part);
                        try {
                            byte[] buffer = new byte[1 << 20];
                            int n;
                            while ((n = in.read(buffer)) >= 0) {
                                out.write(buffer, 0, n);
                                written += n;
                                STATUS.put(key, "PENDING:" + written + "/" + Math.max(0, total));
                            }
                            out.flush();
                            out.getFD().sync();
                        } finally {
                            out.close();
                            in.close();
                        }
                        if (total > 0 && written != total) {
                            STATUS.put(key, "ERROR:La descarga de «" + title + "» quedó incompleta.");
                            return;
                        }
                        String name = fileName(title, type, connection);
                        boolean added = FolioLibrary.ingest(part, name, written);
                        STATUS.put(key, "OK:" + (added ? "«" + title + "» ya está en la biblioteca."
                                : "«" + title + "» ya estaba en la biblioteca."));
                        Log.i(TAG, "[FolioXR] OPDS: descargado " + name + " (" + FolioLibrary.megabytesText(written) + ")");
                    } finally {
                        connection.disconnect();
                    }
                } catch (FolioLibrary.ImportException e) {
                    STATUS.put(key, "ERROR:" + e.getMessage());
                } catch (Throwable t) {
                    STATUS.put(key, "ERROR:" + describe(t));
                    Log.w(TAG, "[FolioXR] OPDS: fallo la descarga (" + t + ")");
                } finally {
                    if (part != null && part.exists()) {
                        part.delete();
                    }
                }
            }
        });
    }

    private static String fileName(String title, String type, HttpURLConnection connection) {
        String disposition = connection.getHeaderField("Content-Disposition");
        if (disposition != null) {
            int at = disposition.toLowerCase(Locale.US).indexOf("filename=");
            if (at >= 0) {
                String name = disposition.substring(at + 9).replace("\"", "").trim();
                int semi = name.indexOf(';');
                if (semi >= 0) {
                    name = name.substring(0, semi);
                }
                if (name.length() > 0) {
                    return new File(name).getName();
                }
            }
        }
        String t = type == null ? "" : type.toLowerCase(Locale.US);
        String extension = t.contains("epub") ? ".epub" : t.contains("pdf") ? ".pdf" : t.contains("rar") || t.contains("cbr")
                ? ".cbr" : t.contains("cbz") || t.contains("comicbook+zip") || t.contains("zip") ? ".cbz"
                : t.contains("mobi") ? ".mobi" : t.contains("azw") ? ".azw3" : t.startsWith("text/plain") ? ".txt" : "";
        String safe = (title == null || title.length() == 0 ? "libro" : title).replaceAll("[\\\\/:*?\"<>|]", "_");
        return safe + extension;
    }
}
