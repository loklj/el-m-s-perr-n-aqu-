package com.loklj.folioxr;

import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Folio XR - Hito 1.
 *
 * Renderiza paginas de un PDF a archivos PNG usando el PdfRenderer de Android.
 * Todo el trabajo ocurre en UN hilo propio (PdfRenderer no es seguro entre hilos),
 * para no bloquear el hilo principal de Unity ni el render del visor.
 *
 * Unity pide un trabajo con una "clave", y luego consulta getStatus(clave):
 *   null          -> clave desconocida
 *   "PENDING"     -> en curso
 *   "OK:<datos>"  -> terminado
 *   "ERROR:<tipo>:<detalle>" -> fallo (tipo: PASSWORD, IO, MEMORY, PAGE, OTHER)
 *
 * Limitaciones conocidas (se resuelven en hitos siguientes):
 * - PdfRenderer no abre PDF protegidos con contrasena en las versiones de Android
 *   que usa Horizon OS; se informa como ERROR:PASSWORD.
 * - Se mantiene abierto un solo documento a la vez.
 */
public final class FolioPdfRenderer {

    private static final String TAG = "FolioXR";
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor();
    private static final ConcurrentHashMap<String, String> STATUS = new ConcurrentHashMap<>();

    // Solo se usan desde el hilo WORKER.
    private static String openPath;
    private static ParcelFileDescriptor openDescriptor;
    private static PdfRenderer openRenderer;

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

    /**
     * Pide dibujar una pagina (indice desde 0) con un ancho en pixeles.
     * Escribe un PNG en outPngPath. Resultado: "OK:<ancho>x<alto>".
     */
    public static void requestRenderPage(final String pdfPath, final int pageIndex,
                                         final int targetWidth, final String outPngPath,
                                         final String key) {
        STATUS.put(key, "PENDING");
        WORKER.execute(new Runnable() {
            @Override
            public void run() {
                Bitmap bitmap = null;
                PdfRenderer.Page page = null;
                try {
                    PdfRenderer renderer = ensureOpen(pdfPath);
                    if (pageIndex < 0 || pageIndex >= renderer.getPageCount()) {
                        STATUS.put(key, "ERROR:PAGE:pagina fuera de rango " + pageIndex);
                        return;
                    }
                    page = renderer.openPage(pageIndex);

                    int width = Math.max(64, targetWidth);
                    float scale = width / (float) page.getWidth();
                    int height = Math.max(64, Math.round(page.getHeight() * scale));

                    bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
                    // PdfRenderer dibuja sobre fondo transparente: se pinta blanco antes.
                    bitmap.eraseColor(Color.WHITE);

                    Matrix transform = new Matrix();
                    transform.setScale(scale, scale);
                    page.render(bitmap, null, transform, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                    page.close();
                    page = null;

                    // Se escribe primero a un temporal y luego se renombra,
                    // para que Unity nunca lea un PNG a medio escribir.
                    File out = new File(outPngPath);
                    File tmp = new File(outPngPath + ".tmp");
                    File parent = out.getParentFile();
                    if (parent != null && !parent.exists() && !parent.mkdirs()) {
                        STATUS.put(key, "ERROR:IO:no se pudo crear " + parent.getPath());
                        return;
                    }
                    FileOutputStream stream = new FileOutputStream(tmp);
                    boolean written;
                    try {
                        written = bitmap.compress(Bitmap.CompressFormat.PNG, 100, stream);
                    } finally {
                        stream.close();
                    }
                    if (!written) {
                        STATUS.put(key, "ERROR:IO:no se pudo codificar el PNG");
                        return;
                    }
                    if (out.exists() && !out.delete()) {
                        STATUS.put(key, "ERROR:IO:no se pudo reemplazar " + out.getPath());
                        return;
                    }
                    if (!tmp.renameTo(out)) {
                        STATUS.put(key, "ERROR:IO:no se pudo renombrar " + tmp.getPath());
                        return;
                    }
                    STATUS.put(key, "OK:" + width + "x" + height);
                } catch (Throwable t) {
                    STATUS.put(key, describeError(t));
                } finally {
                    if (page != null) {
                        try {
                            page.close();
                        } catch (Throwable ignored) {
                        }
                    }
                    if (bitmap != null) {
                        bitmap.recycle();
                    }
                }
            }
        });
    }

    /** Estado de un trabajo (ver comentario de la clase). */
    public static String getStatus(String key) {
        return STATUS.get(key);
    }

    /** Olvida el estado de un trabajo ya leido. */
    public static void clearStatus(String key) {
        STATUS.remove(key);
    }

    /** Cierra el documento abierto (por ejemplo, al salir de la escena). */
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
        Log.i(TAG, "PDF abierto: " + pdfPath + " (" + openRenderer.getPageCount() + " paginas)");
        return openRenderer;
    }

    private static void closeCurrent() {
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
