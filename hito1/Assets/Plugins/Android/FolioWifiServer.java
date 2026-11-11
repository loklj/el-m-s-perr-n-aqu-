package com.loklj.folioxr;

import android.util.Log;

import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URLDecoder;
import java.security.SecureRandom;
import java.util.Enumeration;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Folio XR - Carga desde el PC por Wi-Fi (encargo 12, escrito por Claude).
 *
 * Servidor HTTP minimo sobre ServerSocket, sin dependencias. Solo existe mientras la opcion esta
 * activa en el menu. Seguridad: acepta unicamente direcciones de red local, exige un codigo de
 * 6 digitos en cada subida, se detiene tras 8 codigos errados y no sirve ningun archivo: solo
 * la pagina de subida y PUT /upload. El cuerpo se copia por streaming (1 MB) a la misma copia
 * privada transaccional de la importacion local (FolioLibrary.ingest).
 */
public final class FolioWifiServer {

    private static final String TAG = "FolioXR";
    private static final int FIRST_PORT = 8080, LAST_PORT = 8099, MAX_BAD_CODES = 8;
    private static final ExecutorService CLIENTS = Executors.newFixedThreadPool(3);

    private static final Object LOCK = new Object();
    private static ServerSocket server;
    private static String code = "", url = "", lastMessage = "";
    private static int received, badCodes, serial;
    private static volatile boolean running;

    private FolioWifiServer() {
    }

    /** Abre el servidor. Devuelve {"url","code"} o {"error"}. */
    public static String start() {
        synchronized (LOCK) {
            stopLocked();
            try {
                if (!FolioLibrary.ready()) {
                    return "{\"error\":\"La biblioteca no está lista.\"}";
                }
                String address = localAddress();
                if (address == null) {
                    return "{\"error\":\"El visor no está conectado a una red Wi-Fi.\"}";
                }
                ServerSocket socket = null;
                for (int port = FIRST_PORT; port <= LAST_PORT && socket == null; port++) {
                    try {
                        socket = new ServerSocket(port);
                    } catch (Exception busy) {
                        socket = null;
                    }
                }
                if (socket == null) {
                    return "{\"error\":\"No hay un puerto libre para recibir archivos.\"}";
                }
                server = socket;
                code = String.format(Locale.US, "%06d", new SecureRandom().nextInt(1000000));
                url = "http://" + address + ":" + socket.getLocalPort();
                received = badCodes = 0;
                lastMessage = "";
                running = true;
                serial++;
                final ServerSocket accepting = socket;
                Thread thread = new Thread(new Runnable() {
                    @Override
                    public void run() {
                        acceptLoop(accepting);
                    }
                }, "FolioXR-WiFi");
                thread.setDaemon(true);
                thread.start();
                Log.i(TAG, "[FolioXR] Wi-Fi: servidor abierto en " + url);
                return new JSONObject().put("url", url).put("code", code).toString();
            } catch (Throwable t) {
                Log.e(TAG, "[FolioXR] Wi-Fi: no se pudo abrir el servidor", t);
                stopLocked();
                return "{\"error\":\"No se pudo abrir el servidor. Revisa el permiso de red de la app.\"}";
            }
        }
    }

    public static void stop() {
        synchronized (LOCK) {
            stopLocked();
        }
    }

    private static void stopLocked() {
        running = false;
        if (server != null) {
            try {
                server.close();
            } catch (Throwable ignored) {
                // Ya cerrado.
            }
            server = null;
            Log.i(TAG, "[FolioXR] Wi-Fi: servidor cerrado");
        }
        serial++;
    }

    public static String statusJson() {
        synchronized (LOCK) {
            try {
                return new JSONObject().put("running", running).put("url", url).put("code", code)
                        .put("received", received).put("message", lastMessage).put("serial", serial).toString();
            } catch (Throwable t) {
                return "{\"running\":false}";
            }
        }
    }

    private static String localAddress() throws Exception {
        Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
        while (interfaces != null && interfaces.hasMoreElements()) {
            NetworkInterface network = interfaces.nextElement();
            if (!network.isUp() || network.isLoopback()) {
                continue;
            }
            Enumeration<InetAddress> addresses = network.getInetAddresses();
            while (addresses.hasMoreElements()) {
                InetAddress address = addresses.nextElement();
                if (address instanceof Inet4Address && address.isSiteLocalAddress()) {
                    return address.getHostAddress();
                }
            }
        }
        return null;
    }

    private static void acceptLoop(ServerSocket socket) {
        while (running && !socket.isClosed()) {
            try {
                final Socket client = socket.accept();
                InetAddress from = client.getInetAddress();
                if (from == null || !(from.isSiteLocalAddress() || from.isLoopbackAddress())) {
                    client.close(); // Solo red local.
                    continue;
                }
                CLIENTS.execute(new Runnable() {
                    @Override
                    public void run() {
                        try {
                            client.setSoTimeout(30000);
                            handle(client);
                        } catch (Throwable t) {
                            Log.w(TAG, "[FolioXR] Wi-Fi: conexion interrumpida (" + t + ")");
                        } finally {
                            try {
                                client.close();
                            } catch (Throwable ignored) {
                                // Ya cerrada.
                            }
                        }
                    }
                });
            } catch (Throwable t) {
                if (running) {
                    Log.w(TAG, "[FolioXR] Wi-Fi: fallo al aceptar (" + t + ")");
                }
            }
        }
    }

    private static String readLine(InputStream in) throws Exception {
        StringBuilder line = new StringBuilder();
        int c;
        while ((c = in.read()) >= 0) {
            if (c == '\n') {
                break;
            }
            if (c != '\r') {
                line.append((char) c);
            }
            if (line.length() > 8192) {
                throw new Exception("cabecera demasiado larga");
            }
        }
        return c < 0 && line.length() == 0 ? null : line.toString();
    }

    private static void handle(Socket client) throws Exception {
        InputStream in = new BufferedInputStream(client.getInputStream(), 1 << 16);
        OutputStream out = client.getOutputStream();
        String request = readLine(in);
        if (request == null) {
            return;
        }
        String[] parts = request.split(" ");
        long length = -1;
        String header;
        while ((header = readLine(in)) != null && header.length() > 0) {
            int colon = header.indexOf(':');
            if (colon > 0 && header.substring(0, colon).trim().equalsIgnoreCase("Content-Length")) {
                length = Long.parseLong(header.substring(colon + 1).trim());
            }
        }
        if (parts.length < 2) {
            respond(out, 400, "text/plain", "Solicitud no valida");
            return;
        }
        String method = parts[0], target = parts[1];
        String path = target, query = "";
        int mark = target.indexOf('?');
        if (mark >= 0) {
            path = target.substring(0, mark);
            query = target.substring(mark + 1);
        }
        if ("GET".equals(method) && "/".equals(path)) {
            respond(out, 200, "text/html; charset=utf-8", PAGE);
        } else if ("PUT".equals(method) && "/upload".equals(path)) {
            upload(in, out, query, length);
        } else if ("POST".equals(method) && "/opds".equals(path)) {
            addOpds(in, out, length);
        } else {
            respond(out, 404, "text/plain", "No existe");
        }
    }

    private static String param(String query, String name) throws Exception {
        for (String pair : query.split("&")) {
            int eq = pair.indexOf('=');
            if (eq > 0 && pair.substring(0, eq).equals(name)) {
                return URLDecoder.decode(pair.substring(eq + 1), "UTF-8");
            }
        }
        return "";
    }

    private static boolean sameCode(String given) {
        String expected;
        synchronized (LOCK) {
            expected = code;
        }
        if (given.length() != expected.length() || expected.length() == 0) {
            return false;
        }
        int diff = 0;
        for (int i = 0; i < expected.length(); i++) {
            diff |= given.charAt(i) ^ expected.charAt(i);
        }
        return diff == 0;
    }

    private static void upload(InputStream in, OutputStream out, String query, long length) throws Exception {
        if (!sameCode(param(query, "code"))) {
            boolean close;
            synchronized (LOCK) {
                badCodes++;
                close = badCodes >= MAX_BAD_CODES;
                if (close) {
                    lastMessage = "Se cerró la carga por Wi-Fi: demasiados códigos incorrectos.";
                    stopLocked();
                }
            }
            respond(out, 403, "text/plain; charset=utf-8", "Código incorrecto. Míralo en el visor.");
            return;
        }
        String name = new File(param(query, "name")).getName(); // Sin rutas: solo el nombre.
        if (name.length() == 0) {
            name = "libro";
        }
        if (length <= 0) {
            respond(out, 411, "text/plain; charset=utf-8", "Falta el tamaño del archivo.");
            return;
        }
        long free = FolioLibrary.freeSpace();
        if (free < length + FolioLibrary.SPACE_MARGIN) {
            respond(out, 507, "text/plain; charset=utf-8", "No hay espacio suficiente en el visor: necesita "
                    + FolioLibrary.megabytesText(length) + " y quedan " + FolioLibrary.megabytesText(free) + ".");
            return;
        }
        File part = FolioLibrary.newPartFile();
        String result;
        int status = 200;
        try {
            long written = 0;
            FileOutputStream file = new FileOutputStream(part);
            try {
                byte[] buffer = new byte[1 << 20];
                while (written < length) {
                    if (!running) {
                        throw new Exception("servidor cerrado");
                    }
                    int n = in.read(buffer, 0, (int) Math.min(buffer.length, length - written));
                    if (n < 0) {
                        break;
                    }
                    file.write(buffer, 0, n);
                    written += n;
                }
                file.flush();
                file.getFD().sync();
            } finally {
                file.close();
            }
            if (written != length) {
                throw new FolioLibrary.ImportException("La subida de «" + name + "» quedó incompleta.");
            }
            boolean added = FolioLibrary.ingest(part, name, written);
            result = added ? "Listo: «" + name + "» ya está en la biblioteca."
                    : "«" + name + "» ya estaba en la biblioteca.";
            synchronized (LOCK) {
                if (added) {
                    received++;
                }
                lastMessage = result;
                serial++;
            }
            Log.i(TAG, "[FolioXR] Wi-Fi: recibido " + name + " (" + FolioLibrary.megabytesText(written) + ")");
        } catch (FolioLibrary.ImportException e) {
            status = 422;
            result = e.getMessage();
        } catch (Throwable t) {
            status = 500;
            result = "No se pudo guardar «" + name + "».";
            Log.w(TAG, "[FolioXR] Wi-Fi: fallo al recibir " + name, t);
        } finally {
            if (part.exists()) {
                part.delete();
            }
        }
        respond(out, status, "text/plain; charset=utf-8", result);
    }

    /** Formulario del PC para agregar un catalogo OPDS (encargo 14): asi no hay que escribir en el visor. */
    private static void addOpds(InputStream in, OutputStream out, long length) throws Exception {
        if (length <= 0 || length > 8192) {
            respond(out, 413, "text/plain; charset=utf-8", "Datos no v\u00e1lidos.");
            return;
        }
        byte[] data = new byte[(int) length];
        int offset = 0;
        while (offset < data.length) {
            int n = in.read(data, offset, data.length - offset);
            if (n < 0) {
                break;
            }
            offset += n;
        }
        String form = new String(data, 0, offset, "UTF-8");
        if (!sameCode(param(form, "code"))) {
            synchronized (LOCK) {
                badCodes++;
                if (badCodes >= MAX_BAD_CODES) {
                    lastMessage = "Se cerr\u00f3 la carga por Wi-Fi: demasiados c\u00f3digos incorrectos.";
                    stopLocked();
                }
            }
            respond(out, 403, "text/plain; charset=utf-8", "C\u00f3digo incorrecto. M\u00edralo en el visor.");
            return;
        }
        String error = FolioOpds.addServer(param(form, "name"), param(form, "url"), param(form, "user"),
                param(form, "pass"));
        if (error.length() > 0) {
            respond(out, 422, "text/plain; charset=utf-8", error);
            return;
        }
        synchronized (LOCK) {
            lastMessage = "Cat\u00e1logo agregado: " + param(form, "name");
            serial++;
        }
        respond(out, 200, "text/plain; charset=utf-8",
                "Listo: el cat\u00e1logo aparece en el visor, en Importar y nubes.");
    }

    private static void respond(OutputStream out, int status, String type, String body) throws Exception {
        byte[] data = body.getBytes("UTF-8");
        String head = "HTTP/1.1 " + status + " X\r\nContent-Type: " + type + "\r\nContent-Length: " + data.length
                + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        out.write(head.getBytes("US-ASCII"));
        out.write(data);
        out.flush();
    }

    private static final String PAGE = "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Folio XR</title>"
            + "<style>body{font-family:system-ui,sans-serif;max-width:640px;margin:40px auto;padding:0 16px;"
            + "background:#0e1524;color:#e8eefc}input,button{font-size:18px;padding:10px;border-radius:8px;"
            + "border:1px solid #47608f;background:#17223a;color:#e8eefc}button{background:#2b6fe0;border:0;"
            + "cursor:pointer}#code{width:7em;letter-spacing:.2em;text-align:center}.row{margin:16px 0}"
            + ".item{margin:10px 0;padding:10px;background:#17223a;border-radius:8px}progress{width:100%}"
            + "</style></head><body><h1>Folio XR</h1><p>Escribe el código de 6 dígitos que muestra el visor"
            + " y elige tus libros. Se copian solo al visor, por tu red local.</p>"
            + "<div class=\"row\"><input id=\"code\" maxlength=\"6\" inputmode=\"numeric\" placeholder=\"código\"></div>"
            + "<div class=\"row\"><input id=\"files\" type=\"file\" multiple></div>"
            + "<div class=\"row\"><button id=\"go\">Enviar al visor</button></div><div id=\"list\"></div>"
            + "<script>const $=i=>document.getElementById(i);$('go').onclick=async()=>{const c=$('code').value.trim();"
            + "for(const f of $('files').files){const d=document.createElement('div');d.className='item';"
            + "const p=document.createElement('progress');p.max=1;p.value=0;const t=document.createElement('div');"
            + "t.textContent=f.name;d.append(t,p);$('list').append(d);await new Promise(r=>{const x=new XMLHttpRequest();"
            + "x.open('PUT','/upload?code='+encodeURIComponent(c)+'&name='+encodeURIComponent(f.name));"
            + "x.upload.onprogress=e=>{if(e.lengthComputable)p.value=e.loaded/e.total};"
            + "x.onload=()=>{t.textContent=x.responseText;p.value=x.status==200?1:0;r()};"
            + "x.onerror=()=>{t.textContent='Se cortó la conexión con el visor: '+f.name;r()};x.send(f)})}};"
            + "</script>"
            + "<h2>Agregar un cat\u00e1logo (OPDS)</h2><p>Calibre-Web, Kavita, Komga u otro servidor OPDS."
            + " El usuario y la contrase\u00f1a son opcionales y se guardan cifrados en el visor.</p>"
            + "<div class=\"row\"><input id=\"oname\" placeholder=\"nombre (p. ej. Mi Calibre)\"></div>"
            + "<div class=\"row\"><input id=\"ourl\" style=\"width:95%\" placeholder=\"http://192.168.1.10:8083/opds\"></div>"
            + "<div class=\"row\"><input id=\"ouser\" placeholder=\"usuario\"> <input id=\"opass\" type=\"password\""
            + " placeholder=\"contrase\u00f1a\"></div><div class=\"row\"><button id=\"oadd\">Agregar al visor</button>"
            + " <span id=\"ores\"></span></div><script>$('oadd').onclick=async()=>{const b=new URLSearchParams();"
            + "b.set('code',$('code').value.trim());b.set('name',$('oname').value);b.set('url',$('ourl').value);"
            + "b.set('user',$('ouser').value);b.set('pass',$('opass').value);try{const r=await fetch('/opds',"
            + "{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:b.toString()});"
            + "$('ores').textContent=await r.text()}catch(e){$('ores').textContent='Sin conexi\u00f3n con el visor.'}};"
            + "</script></body></html>";
}
