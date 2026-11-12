package com.loklj.folioxr;

import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.channels.FileChannel;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Folio XR - Paginas de PDF con el PdfRenderer de Android.
 *
 * Todo el trabajo ocurre en UN hilo propio (PdfRenderer no es seguro entre hilos), para no bloquear
 * el hilo principal de Unity ni el render del visor.
 *
 * Unity pide un trabajo con una "clave", y luego consulta getStatus(clave):
 *   null          -> clave desconocida
 *   "PENDING"     -> en curso
 *   "OK:<datos>"  -> terminado
 *   "ERROR:<tipo>:<detalle>" -> fallo (tipo: PASSWORD, IO, MEMORY, PAGE, OTHER)
 *
 * Encargo 6 (revision de Claude, 11 nov 2026):
 * - Pixeles crudos en vez de PNG: cabecera "FXR1" + ancho + alto (enteros little-endian) y los bytes
 *   RGBA de abajo hacia arriba, como los espera Texture2D. Sin codificar ni decodificar PNG.
 * - Precarga en disco de la doble pagina siguiente (y la anterior al retroceder), maximo 4 archivos.
 * - Pagina demasiado grande o sin memoria: se reintenta con la mitad del ancho en vez de fallar.
 * - El archivo se abre con un descriptor: PdfRenderer lee por partes, nunca el PDF entero.
 */
public final class FolioPdfRenderer {

    private static final String TAG = "FolioXR";
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();
    private static final ConcurrentHashMap<String, String> STATUS = new ConcurrentHashMap<>();
    private static final int HEADER = 16, MAX_PREFETCH = 4;
    private static final long MAX_PIXELS = 3072L * 4400L;

    // Solo se usan desde el hilo WORKER.
    private static String openPath;
    private static ParcelFileDescriptor openDescriptor;
    private static PdfRenderer openRenderer;
    private static int lastRequested = -1;
    private static final LinkedHashMap<String, File> PREFETCHED = new LinkedHashMap<>(8, 0.75f, true);

    private FolioPdfRenderer() {
    }

    /** Pide el numero de paginas. Resultado: "OK:<numero>". */
    public static void requestPageCount(final String pdfPath, final String key) {
        STATUS.put(key, "PENDING");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    PdfRenderer renderer = ensureOpen(pdfPath);
                    STATUS.put(key, "OK:" + renderer.getPageCount());
                } catch (Throwable t) {
                    STATUS.put(key, describeError(t));
                }
            }
        });
    }

    /** Lectura opcional de marcadores: independiente de las paginas y del estado de PdfRenderer. */
    public static void requestContents(final String path,final String key) {
        STATUS.put(key,"PENDING");
        WORKER.execute(() -> {
            try(FolioPdfOutline reader=new FolioPdfOutline(path,()->!STATUS.containsKey(key))){
                STATUS.replace(key,"OK:"+FolioContents.json(reader.read()));
            }catch(Throwable e){STATUS.replace(key,"ERROR:TOC:"+e.getMessage());}
        });
    }

    /**
     * Pide dibujar una pagina (indice desde 0) con un ancho en pixeles.
     * Escribe pixeles crudos en outPath. Resultado: "OK:<ancho>x<alto>".
     */
    public static void requestRenderPage(final String pdfPath, final int pageIndex,
                                         final int targetWidth, final String outPath,
                                         final String key) {
        STATUS.put(key, "PENDING");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    PdfRenderer renderer = ensureOpen(pdfPath);
                    int count = renderer.getPageCount();
                    if (pageIndex < 0 || pageIndex >= count) {
                        STATUS.put(key, "ERROR:PAGE:pagina fuera de rango " + pageIndex);
                        return;
                    }
                    File out = new File(outPath);
                    File parent = out.getParentFile();
                    if (parent != null && !parent.exists() && !parent.mkdirs()) {
                        STATUS.put(key, "ERROR:IO:no se pudo crear " + parent.getPath());
                        return;
                    }
                    long start = SystemClock.elapsedRealtime();
                    String size;
                    File cached = PREFETCHED.remove(cacheKey(pageIndex, targetWidth));
                    if (cached != null && cached.exists() && (!out.exists() || out.delete()) && cached.renameTo(out)) {
                        size = readSize(out);
                    } else {
                        size = render(renderer, pageIndex, targetWidth, out);
                    }
                    STATUS.put(key, "OK:" + size);
                    Log.i(TAG, "[FolioXR] PDF: pagina " + (pageIndex + 1) + " lista en "
                            + (SystemClock.elapsedRealtime() - start) + " ms" + (cached != null ? " (precargada)" : ""));
                    boolean forward = pageIndex >= lastRequested;
                    lastRequested = pageIndex;
                    // Precarga: tras la pagina derecha de una doble pagina, las dos siguientes (o anteriores).
                    if (pageIndex % 2 == 1 || pageIndex == count - 1) {
                        int first = forward ? pageIndex + 1 : pageIndex - 3;
                        schedulePrefetch(pdfPath, first, first + 1, targetWidth, parent);
                    }
                } catch (Throwable t) {
                    STATUS.put(key, describeError(t));
                }
            }
        });
    }

    private static String cacheKey(int page, int width) {
        return openPath + "#" + page + "@" + width;
    }

    private static void schedulePrefetch(final String pdfPath, final int from, final int to, final int width,
                                         final File folder) {
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    if (!pdfPath.equals(openPath) || openRenderer == null || folder == null) {
                        return;
                    }
                    for (int page = Math.max(0, from); page <= to && page < openRenderer.getPageCount(); page++) {
                        String id = cacheKey(page, width);
                        if (PREFETCHED.containsKey(id)) {
                            continue;
                        }
                        File file = new File(folder, "precarga-" + page + "-" + width + ".raw");
                        render(openRenderer, page, width, file);
                        PREFETCHED.put(id, file);
                        trimPrefetch();
                    }
                } catch (Throwable t) {
                    Log.w(TAG, "[FolioXR] PDF: precarga omitida (" + t + ")");
                }
            }
        });
    }

    private static void trimPrefetch() {
        Iterator<Map.Entry<String, File>> it = PREFETCHED.entrySet().iterator();
        while (PREFETCHED.size() > MAX_PREFETCH && it.hasNext()) {
            File old = it.next().getValue();
            old.delete();
            it.remove();
        }
    }

    private static void clearPrefetch() {
        for (File file : PREFETCHED.values()) {
            file.delete();
        }
        PREFETCHED.clear();
    }

    /** Dibuja la pagina y escribe los pixeles crudos. Si no cabe en memoria, reintenta a la mitad. */
    private static String render(PdfRenderer renderer, int pageIndex, int targetWidth, File out) throws Exception {
        int width = Math.max(64, targetWidth);
        for (int attempt = 0; ; attempt++) {
            PdfRenderer.Page page = renderer.openPage(pageIndex);
            Bitmap bitmap = null;
            try {
                float scale = width / (float) Math.max(1, page.getWidth());
                int height = Math.max(64, Math.round(page.getHeight() * scale));
                // Paginas extremas (carteles, tiras muy largas): se limita el total de pixeles.
                if ((long) width * height > MAX_PIXELS) {
                    float shrink = (float) Math.sqrt(MAX_PIXELS / (double) ((long) width * height));
                    width = Math.max(64, (int) (width * shrink));
                    scale = width / (float) Math.max(1, page.getWidth());
                    height = Math.max(64, Math.round(page.getHeight() * scale));
                }
                bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
                // PdfRenderer dibuja sobre fondo transparente: se pinta blanco antes.
                bitmap.eraseColor(Color.WHITE);
                Matrix transform = new Matrix();
                transform.setScale(scale, scale);
                page.render(bitmap, null, transform, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                writeRaw(bitmap, out);
                return width + "x" + height;
            } catch (OutOfMemoryError memory) {
                if (attempt >= 2 || width <= 512) {
                    throw memory;
                }
                width /= 2;
                Log.w(TAG, "[FolioXR] PDF: sin memoria para la pagina " + (pageIndex + 1) + ", se reduce a " + width + " px");
            } finally {
                page.close();
                if (bitmap != null) {
                    bitmap.recycle();
                }
            }
        }
    }

    private static void writeRaw(Bitmap bitmap, File out) throws Exception {
        File tmp = new File(out.getPath() + ".tmp");
        ByteBuffer header = ByteBuffer.allocate(HEADER).order(ByteOrder.LITTLE_ENDIAN);
        header.put((byte) 'F').put((byte) 'X').put((byte) 'R').put((byte) '1');
        header.putInt(bitmap.getWidth()).putInt(bitmap.getHeight()).putInt(0);
        header.flip();
        ByteBuffer pixels = ByteBuffer.allocateDirect(bitmap.getByteCount());
        bitmap.copyPixelsToBuffer(pixels); // ARGB_8888 en memoria: bytes R, G, B, A.
        int rowBytes = bitmap.getRowBytes(), rows = bitmap.getHeight();
        FileOutputStream stream = new FileOutputStream(tmp);
        try {
            FileChannel channel = stream.getChannel();
            while (header.hasRemaining()) {
                channel.write(header);
            }
            // Filas de abajo hacia arriba: Texture2D espera la primera fila abajo.
            for (int row = rows - 1; row >= 0; row--) {
                pixels.limit((row + 1) * rowBytes);
                pixels.position(row * rowBytes);
                while (pixels.hasRemaining()) {
                    channel.write(pixels);
                }
            }
        } finally {
            stream.close();
        }
        // Temporal + renombrado: Unity nunca lee un archivo a medio escribir.
        if (out.exists() && !out.delete()) {
            tmp.delete();
            throw new java.io.IOException("no se pudo reemplazar " + out.getPath());
        }
        if (!tmp.renameTo(out)) {
            tmp.delete();
            throw new java.io.IOException("no se pudo renombrar " + tmp.getPath());
        }
    }

    private static String readSize(File file) throws Exception {
        java.io.FileInputStream in = new java.io.FileInputStream(file);
        try {
            byte[] head = new byte[HEADER];
            int n = 0;
            while (n < HEADER) {
                int r = in.read(head, n, HEADER - n);
                if (r < 0) {
                    break;
                }
                n += r;
            }
            ByteBuffer buffer = ByteBuffer.wrap(head).order(ByteOrder.LITTLE_ENDIAN);
            return buffer.getInt(4) + "x" + buffer.getInt(8);
        } finally {
            in.close();
        }
    }

    /** Estado de un trabajo (ver comentario de la clase). */
    public static String getStatus(String key) {
        return STATUS.get(key);
    }

    /** Olvida el estado de un trabajo ya leido. */
    public static void clearStatus(String key) {
        STATUS.remove(key);
    }

    /** Cierra el documento abierto (por ejemplo, al cambiar de libro o salir de la escena). */
    public static void close() {
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                closeCurrent();
            }
        });
    }

    // ---------------------------------------------------------------------
    // Solo desde el hilo WORKER.

    private static PdfRenderer ensureOpen(String pdfPath) throws Exception {
        if (openRenderer != null && pdfPath.equals(openPath)) {
            return openRenderer;
        }
        closeCurrent();
        File file = new File(pdfPath);
        if (!file.exists()) {
            throw new java.io.FileNotFoundException("no existe " + pdfPath);
        }
        ParcelFileDescriptor descriptor =
                ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY);
        try {
            openRenderer = new PdfRenderer(descriptor);
        } catch (Throwable t) {
            descriptor.close();
            throw t;
        }
        openDescriptor = descriptor;
        openPath = pdfPath;
        lastRequested = -1;
        Log.i(TAG, "PDF abierto: " + pdfPath + " (" + openRenderer.getPageCount() + " paginas)");
        return openRenderer;
    }

    private static void closeCurrent() {
        clearPrefetch();
        if (openRenderer != null) {
            try {
                openRenderer.close();
            } catch (Throwable ignored) {
            }
        }
        if (openDescriptor != null) {
            try {
                openDescriptor.close();
            } catch (Throwable ignored) {
            }
        }
        openRenderer = null;
        openDescriptor = null;
        openPath = null;
    }

    private static String describeError(Throwable t) {
        Log.e(TAG, "Error en PdfRenderer", t);
        String detail = t.getClass().getSimpleName() + ": " + t.getMessage();
        if (t instanceof SecurityException) {
            return "ERROR:PASSWORD:" + detail;
        }
        if (t instanceof OutOfMemoryError) {
            return "ERROR:MEMORY:" + detail;
        }
        if (t instanceof java.io.IOException) {
            return "ERROR:IO:" + detail;
        }
        return "ERROR:OTHER:" + detail;
    }
}
