package com.loklj.folioxr;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.Enumeration;
import java.util.Locale;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

/**
 * Folio XR - Comics CBZ (revision del 11 de noviembre de 2026, escrita por Claude).
 *
 * Mismo protocolo que FolioPdfRenderer: Unity pide un trabajo con una clave y consulta
 * getStatus(clave). Un CBZ es un ZIP de imagenes; cada imagen es una pagina, en orden natural
 * de nombre ("pagina 2" antes que "pagina 10"). Se decodifica con submuestreo para no cargar
 * imagenes enormes completas y se escribe un JPEG temporal (Unity lo lee con LoadImage).
 */
public final class FolioComicRenderer {

    private static final String TAG = "FolioXR";
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();
    private static final ConcurrentHashMap<String, String> STATUS = new ConcurrentHashMap<>();

    // Solo se usan desde el hilo WORKER.
    private static String openPath;
    private static ZipFile openZip;
    private static ArrayList<ZipEntry> pages;

    private FolioComicRenderer() {
    }

    public static void requestPageCount(final String path, final String key) {
        STATUS.put(key, "PENDING");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    ensureOpen(path);
                    if (pages.isEmpty()) {
                        STATUS.put(key, "ERROR:IO:el archivo no contiene imagenes (file not in comic format or corrupted)");
                        return;
                    }
                    STATUS.put(key, "OK:" + pages.size());
                } catch (Throwable t) {
                    STATUS.put(key, describeError(t));
                }
            }
        });
    }

    public static void requestRenderPage(final String path, final int pageIndex, final int targetWidth,
                                         final String outPath, final String key) {
        STATUS.put(key, "PENDING");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                Bitmap decoded = null, scaled = null;
                try {
                    ensureOpen(path);
                    if (pageIndex < 0 || pageIndex >= pages.size()) {
                        STATUS.put(key, "ERROR:PAGE:pagina fuera de rango " + pageIndex);
                        return;
                    }
                    ZipEntry entry = pages.get(pageIndex);
                    BitmapFactory.Options bounds = new BitmapFactory.Options();
                    bounds.inJustDecodeBounds = true;
                    InputStream in = openZip.getInputStream(entry);
                    try {
                        BitmapFactory.decodeStream(in, null, bounds);
                    } finally {
                        in.close();
                    }
                    if (bounds.outWidth <= 0 || bounds.outHeight <= 0) {
                        STATUS.put(key, "ERROR:IO:imagen ilegible " + entry.getName() + " (corrupted)");
                        return;
                    }
                    int width = Math.max(64, targetWidth);
                    BitmapFactory.Options options = new BitmapFactory.Options();
                    options.inPreferredConfig = Bitmap.Config.ARGB_8888;
                    options.inSampleSize = 1;
                    while (bounds.outWidth / (options.inSampleSize * 2) >= width) {
                        options.inSampleSize *= 2;
                    }
                    in = openZip.getInputStream(entry);
                    try {
                        decoded = BitmapFactory.decodeStream(in, null, options);
                    } finally {
                        in.close();
                    }
                    if (decoded == null) {
                        STATUS.put(key, "ERROR:IO:no se pudo decodificar " + entry.getName() + " (corrupted)");
                        return;
                    }
                    int height = Math.max(64, Math.round(decoded.getHeight() * (width / (float) decoded.getWidth())));
                    scaled = decoded.getWidth() == width ? decoded : Bitmap.createScaledBitmap(decoded, width, height, true);

                    File out = new File(outPath);
                    File tmp = new File(outPath + ".tmp");
                    File parent = out.getParentFile();
                    if (parent != null && !parent.exists() && !parent.mkdirs()) {
                        STATUS.put(key, "ERROR:IO:no se pudo crear " + parent.getPath());
                        return;
                    }
                    FileOutputStream stream = new FileOutputStream(tmp);
                    boolean written;
                    try {
                        // JPEG: mucho mas rapido de codificar que PNG para ilustraciones grandes.
                        written = scaled.compress(Bitmap.CompressFormat.JPEG, 92, stream);
                    } finally {
                        stream.close();
                    }
                    if (!written || (out.exists() && !out.delete()) || !tmp.renameTo(out)) {
                        STATUS.put(key, "ERROR:IO:no se pudo escribir la pagina");
                        return;
                    }
                    STATUS.put(key, "OK:" + width + "x" + height);
                } catch (Throwable t) {
                    STATUS.put(key, describeError(t));
                } finally {
                    if (scaled != null && scaled != decoded) {
                        scaled.recycle();
                    }
                    if (decoded != null) {
                        decoded.recycle();
                    }
                }
            }
        });
    }

    public static String getStatus(String key) {
        return STATUS.get(key);
    }

    public static void clearStatus(String key) {
        STATUS.remove(key);
    }

    public static void close() {
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                closeOpen();
            }
        });
    }

    private static void ensureOpen(String path) throws Exception {
        if (path.equals(openPath) && openZip != null) {
            return;
        }
        closeOpen();
        ZipFile zip = new ZipFile(path);
        ArrayList<ZipEntry> images = new ArrayList<ZipEntry>();
        Enumeration<? extends ZipEntry> entries = zip.entries();
        while (entries.hasMoreElements()) {
            ZipEntry entry = entries.nextElement();
            String name = entry.getName();
            String lower = name.toLowerCase(Locale.US);
            if (entry.isDirectory() || lower.startsWith("__macosx/") || lower.contains("/.")
                    || new File(name).getName().startsWith(".")) {
                continue;
            }
            if (lower.endsWith(".jpg") || lower.endsWith(".jpeg") || lower.endsWith(".png")
                    || lower.endsWith(".webp") || lower.endsWith(".gif") || lower.endsWith(".bmp")) {
                images.add(entry);
            }
        }
        Collections.sort(images, new Comparator<ZipEntry>() {
            @Override
            public int compare(ZipEntry a, ZipEntry b) {
                return naturalCompare(a.getName(), b.getName());
            }
        });
        openZip = zip;
        openPath = path;
        pages = images;
        Log.i(TAG, "[FolioXR] Comic: " + images.size() + " paginas en " + new File(path).getName());
    }

    private static void closeOpen() {
        try {
            if (openZip != null) {
                openZip.close();
            }
        } catch (Throwable ignored) {
            // Ya cerrado.
        }
        openZip = null;
        openPath = null;
        pages = null;
    }

    /** Orden natural: compara los numeros como numeros ("2" antes que "10"). */
    static int naturalCompare(String a, String b) {
        a = a.toLowerCase(Locale.US);
        b = b.toLowerCase(Locale.US);
        int i = 0, j = 0;
        while (i < a.length() && j < b.length()) {
            char ca = a.charAt(i), cb = b.charAt(j);
            if (Character.isDigit(ca) && Character.isDigit(cb)) {
                int si = i, sj = j;
                while (i < a.length() && Character.isDigit(a.charAt(i))) {
                    i++;
                }
                while (j < b.length() && Character.isDigit(b.charAt(j))) {
                    j++;
                }
                String na = a.substring(si, i).replaceFirst("^0+(?=.)", "");
                String nb = b.substring(sj, j).replaceFirst("^0+(?=.)", "");
                if (na.length() != nb.length()) {
                    return na.length() - nb.length();
                }
                int c = na.compareTo(nb);
                if (c != 0) {
                    return c;
                }
            } else {
                if (ca != cb) {
                    return ca - cb;
                }
                i++;
                j++;
            }
        }
        return (a.length() - i) - (b.length() - j);
    }

    private static String describeError(Throwable t) {
        String detail = t.getClass().getSimpleName() + ": " + t.getMessage();
        Log.w(TAG, "[FolioXR] Comic: " + detail);
        if (t instanceof OutOfMemoryError) {
            return "ERROR:MEMORY:" + detail;
        }
        if (t instanceof java.util.zip.ZipException) {
            return "ERROR:IO:" + detail + " (corrupted)";
        }
        if (t instanceof java.io.IOException) {
            return "ERROR:IO:" + detail;
        }
        return "ERROR:OTHER:" + detail;
    }
}
