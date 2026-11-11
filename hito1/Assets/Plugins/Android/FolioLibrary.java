package com.loklj.folioxr;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.pdf.PdfRenderer;
import android.net.Uri;
import android.os.Bundle;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.RandomAccessFile;
import java.security.MessageDigest;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Date;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Folio XR - Biblioteca persistente e importacion local (encargo 5, escrito por Claude).
 *
 * - Base SQLite del sistema (android.database.sqlite). No se usa Room: necesita procesador de
 *   anotaciones y dependencias de Gradle, que complican el build de Unity; SQLite no agrega ninguna.
 * - Importacion con el selector del sistema (SAF, ACTION_OPEN_DOCUMENT, varios archivos) mediante un
 *   Fragment sin interfaz: no hace falta modificar la actividad de Unity.
 * - Copia privada por streaming de 1 MB: archivo temporal, fsync, verificacion de tamano, renombrado
 *   y recien ahi el registro en la base. Un diario por archivo permite reconciliar si la app muere
 *   entre el renombrado y el registro. Nunca se carga el archivo completo en memoria.
 * - Unity consulta el avance con importStatusJson(); no se usa UnitySendMessage.
 */
public final class FolioLibrary {

    private static final String TAG = "FolioXR";
    private static final String DB_NAME = "biblioteca.db";
    private static final int SCHEMA = 1;
    private static final int REQUEST_PICK = 0x4F58;
    private static final int BUFFER = 1 << 20;
    static final long SPACE_MARGIN = 64L * 1024 * 1024;
    private static final int MAX_BACKUPS = 5;
    private static final long BACKUP_INTERVAL_MS = 20L * 60 * 60 * 1000;
    private static final String[] KNOWN = {"pdf", "epub", "cbz", "zip", "cbr", "cb7", "mobi", "azw", "azw3",
            "txt", "rtf", "docx", "odt", "fb2", "djvu"};

    private static final ExecutorService DB_WORKER = Executors.newSingleThreadExecutor();
    private static final ExecutorService IMPORT_WORKER = Executors.newSingleThreadExecutor();

    private static Context app;
    private static Helper helper;
    private static File booksDir, tempDir, coversDir, backupsDir;

    // Estado de la importacion; Unity lo lee por sondeo.
    private static final Object STATUS_LOCK = new Object();
    private static String state = "idle", file = "", message = "";
    private static int index, count, imported, serial;
    private static long copied, total;
    private static volatile boolean cancel;

    private FolioLibrary() {
    }

    // ------------------------------------------------------------------ Base de datos

    private static final class Helper extends SQLiteOpenHelper {
        Helper(Context context) {
            super(context, DB_NAME, null, SCHEMA);
        }

        @Override
        public void onCreate(SQLiteDatabase db) {
            db.execSQL("CREATE TABLE books (id TEXT PRIMARY KEY, title TEXT NOT NULL, author TEXT,"
                    + " format TEXT NOT NULL, path TEXT NOT NULL, cover TEXT, size INTEGER NOT NULL,"
                    + " pages INTEGER NOT NULL DEFAULT 0, position INTEGER NOT NULL DEFAULT 0,"
                    + " added INTEGER NOT NULL, last_read INTEGER NOT NULL DEFAULT 0, state INTEGER NOT NULL)");
            db.execSQL("CREATE TABLE bookmarks (book_id TEXT NOT NULL, position INTEGER NOT NULL, label TEXT,"
                    + " created INTEGER NOT NULL, PRIMARY KEY (book_id, position))");
        }

        @Override
        public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
            // Migraciones de esquema, una por version. SQLiteOpenHelper las ejecuta en una transaccion.
            for (int version = oldVersion; version < newVersion; version++) {
                switch (version) {
                    default:
                        // La version 1 es la primera; las siguientes agregan aqui su "case".
                        break;
                }
            }
        }
    }

    /** Abre la base, reconcilia importaciones interrumpidas y programa el respaldo. */
    public static synchronized boolean init(Context context) {
        if (helper != null) {
            return true;
        }
        try {
            app = context.getApplicationContext();
            File root = app.getFilesDir();
            booksDir = new File(root, "libros");
            tempDir = new File(booksDir, ".tmp");
            coversDir = new File(root, "portadas");
            backupsDir = new File(root, "respaldos");
            booksDir.mkdirs();
            tempDir.mkdirs();
            coversDir.mkdirs();
            backupsDir.mkdirs();
            Helper opened = new Helper(app);
            opened.getWritableDatabase(); // Crea o migra ahora; un fallo deja la biblioteca desactivada.
            helper = opened;
            recover();
            DB_WORKER.execute(new Runnable() {
                @Override
                public void run() {
                    backup();
                }
            });
            Log.i(TAG, "[FolioXR] Biblioteca: lista en " + booksDir);
            return true;
        } catch (Throwable t) {
            Log.e(TAG, "[FolioXR] Biblioteca: no se pudo abrir la base", t);
            helper = null;
            return false;
        }
    }

    /** {"books":[...]} ordenados por ultima lectura. */
    public static String listBooksJson() {
        JSONArray books = new JSONArray();
        try {
            Cursor c = helper.getReadableDatabase().rawQuery("SELECT id, title, author, format, path, cover,"
                    + " size, pages, position, last_read FROM books WHERE state = 1"
                    + " ORDER BY last_read DESC, added DESC", null);
            try {
                while (c.moveToNext()) {
                    JSONObject b = new JSONObject();
                    b.put("id", c.getString(0));
                    b.put("title", c.getString(1));
                    b.put("author", c.isNull(2) ? "" : c.getString(2));
                    b.put("format", c.getString(3));
                    b.put("path", c.getString(4));
                    b.put("cover", c.isNull(5) ? "" : c.getString(5));
                    b.put("size", c.getLong(6));
                    b.put("pages", c.getInt(7));
                    b.put("position", c.getInt(8));
                    b.put("lastRead", c.getLong(9));
                    books.put(b);
                }
            } finally {
                c.close();
            }
            return new JSONObject().put("books", books).toString();
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] Biblioteca: no se pudo listar", t);
            return "{\"books\":[]}";
        }
    }

    /** Guarda la pagina. sync=true escribe en el hilo que llama (al pausar la app). */
    public static void setProgress(final String id, final int position, final int pages, boolean sync) {
        Runnable work = new Runnable() {
            @Override
            public void run() {
                try {
                    ContentValues values = new ContentValues();
                    values.put("position", position);
                    if (pages > 0) {
                        values.put("pages", pages);
                    }
                    values.put("last_read", System.currentTimeMillis());
                    helper.getWritableDatabase().update("books", values, "id = ?", new String[]{id});
                } catch (Throwable t) {
                    Log.w(TAG, "[FolioXR] Biblioteca: no se pudo guardar el progreso", t);
                }
            }
        };
        if (sync) {
            work.run();
        } else {
            DB_WORKER.execute(work);
        }
    }

    /** Quita la ficha y sus marcadores; la copia privada y la portada se borran en segundo plano. */
    public static boolean removeBook(String id) {
        try {
            SQLiteDatabase db = helper.getWritableDatabase();
            String path = null, cover = null;
            Cursor c = db.rawQuery("SELECT path, cover FROM books WHERE id = ?", new String[]{id});
            try {
                if (c.moveToFirst()) {
                    path = c.getString(0);
                    cover = c.isNull(1) ? null : c.getString(1);
                }
            } finally {
                c.close();
            }
            db.beginTransaction();
            try {
                db.delete("bookmarks", "book_id = ?", new String[]{id});
                db.delete("books", "id = ?", new String[]{id});
                db.setTransactionSuccessful();
            } finally {
                db.endTransaction();
            }
            final String deletePath = path, deleteCover = cover;
            DB_WORKER.execute(new Runnable() {
                @Override
                public void run() {
                    deleteQuietly(deletePath);
                    deleteQuietly(deleteCover);
                }
            });
            Log.i(TAG, "[FolioXR] Biblioteca: quitado " + id);
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] Biblioteca: no se pudo quitar " + id, t);
            return false;
        }
    }

    /** Pone o quita el marcador de esa pagina. Devuelve true si quedo marcada. */
    public static boolean toggleBookmark(String id, int position) {
        try {
            SQLiteDatabase db = helper.getWritableDatabase();
            String[] where = {id, String.valueOf(position)};
            if (db.delete("bookmarks", "book_id = ? AND position = ?", where) > 0) {
                return false;
            }
            ContentValues values = new ContentValues();
            values.put("book_id", id);
            values.put("position", position);
            values.put("created", System.currentTimeMillis());
            db.insert("bookmarks", null, values);
            return true;
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] Biblioteca: no se pudo cambiar el marcador", t);
            return false;
        }
    }

    /** {"positions":[...]} en orden de pagina. */
    public static String bookmarksJson(String id) {
        JSONArray positions = new JSONArray();
        try {
            Cursor c = helper.getReadableDatabase().rawQuery(
                    "SELECT position FROM bookmarks WHERE book_id = ? ORDER BY position", new String[]{id});
            try {
                while (c.moveToNext()) {
                    positions.put(c.getInt(0));
                }
            } finally {
                c.close();
            }
            return new JSONObject().put("positions", positions).toString();
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] Biblioteca: no se pudieron leer los marcadores", t);
            return "{\"positions\":[]}";
        }
    }

    // ------------------------------------------------------------------ Recuperacion y respaldo

    /** Borra parciales y registra los archivos que quedaron renombrados pero sin ficha. */
    private static void recover() {
        File[] leftovers = tempDir.listFiles();
        if (leftovers == null) {
            return;
        }
        int parts = 0, recovered = 0;
        for (File item : leftovers) {
            String name = item.getName();
            if (name.endsWith(".part")) {
                if (item.delete()) {
                    parts++;
                }
            } else if (name.endsWith(".json")) {
                try {
                    JSONObject journal = new JSONObject(readSmallText(item));
                    File dest = new File(journal.getString("path"));
                    if (dest.exists() && !hasBook(journal.getString("id"))) {
                        insertBook(journal.getString("id"), journal.getString("title"), journal.getString("format"),
                                dest, journal.getLong("size"));
                        recovered++;
                    }
                } catch (Throwable t) {
                    Log.w(TAG, "[FolioXR] Biblioteca: diario ilegible " + name, t);
                }
                item.delete();
            }
        }
        if (parts > 0 || recovered > 0) {
            Log.i(TAG, "[FolioXR] Biblioteca: recuperacion, " + parts + " parciales borrados y " + recovered
                    + " libros registrados");
        }
    }

    /** Copia coherente de la base (VACUUM INTO), como maximo una cada 20 horas y cinco en total. */
    private static void backup() {
        try {
            File[] existing = backupsDir.listFiles();
            if (existing == null) {
                existing = new File[0];
            }
            Arrays.sort(existing);
            if (existing.length > 0
                    && System.currentTimeMillis() - existing[existing.length - 1].lastModified() < BACKUP_INTERVAL_MS) {
                return;
            }
            String stamp = new SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US).format(new Date());
            File target = new File(backupsDir, "biblioteca-v" + SCHEMA + "-" + stamp + ".db");
            helper.getWritableDatabase().execSQL("VACUUM INTO '" + target.getAbsolutePath().replace("'", "''") + "'");
            for (int i = 0; i < existing.length + 1 - MAX_BACKUPS; i++) {
                existing[i].delete();
            }
            Log.i(TAG, "[FolioXR] Biblioteca: respaldo " + target.getName());
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] Biblioteca: no se pudo respaldar la base", t);
        }
    }

    private static boolean hasBook(String id) {
        Cursor c = helper.getReadableDatabase().rawQuery("SELECT 1 FROM books WHERE id = ?", new String[]{id});
        try {
            return c.moveToFirst();
        } finally {
            c.close();
        }
    }

    private static void insertBook(String id, String title, String format, File dest, long size) {
        ContentValues values = new ContentValues();
        values.put("id", id);
        values.put("title", title);
        values.put("format", format);
        values.put("path", dest.getAbsolutePath());
        values.put("size", size);
        values.put("added", System.currentTimeMillis());
        values.put("state", 1);
        SQLiteDatabase db = helper.getWritableDatabase();
        db.beginTransaction();
        try {
            db.insertOrThrow("books", null, values);
            db.setTransactionSuccessful();
        } finally {
            db.endTransaction();
        }
    }

    // ------------------------------------------------------------------ Importacion

    /** Abre el selector del sistema. El resultado llega al Fragment y la copia corre en otro hilo. */
    public static void pickAndImport(final Activity activity) {
        synchronized (STATUS_LOCK) {
            if ("copying".equals(state)) {
                return;
            }
            setStatus("picking", "", "");
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    activity.getFragmentManager().beginTransaction()
                            .add(new PickerFragment(), "folio-importar").commitAllowingStateLoss();
                } catch (Throwable t) {
                    Log.e(TAG, "[FolioXR] Importar: no se pudo abrir el selector", t);
                    finish("unavailable", "El selector de archivos no est\u00e1 disponible en este visor.");
                }
            }
        });
    }

    public static void cancelImport() {
        cancel = true;
    }

    public static String importStatusJson() {
        synchronized (STATUS_LOCK) {
            try {
                return new JSONObject().put("state", state).put("file", file).put("message", message)
                        .put("index", index).put("count", count).put("imported", imported)
                        .put("copied", copied).put("total", total).put("serial", serial).toString();
            } catch (Throwable t) {
                return "{\"state\":\"idle\"}";
            }
        }
    }

    private static void setStatus(String newState, String newFile, String newMessage) {
        synchronized (STATUS_LOCK) {
            state = newState;
            file = newFile;
            message = newMessage;
            serial++;
        }
    }

    private static void finish(String newState, String newMessage) {
        setStatus(newState, "", newMessage);
        Log.i(TAG, "[FolioXR] Importar: " + newState + " - " + newMessage);
    }

    /** Fragment sin interfaz que recibe el resultado del selector. */
    public static final class PickerFragment extends Fragment {
        public PickerFragment() {
        }

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            if (savedInstanceState != null) {
                return; // Recreado por el sistema: no volver a abrir el selector.
            }
            try {
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                startActivityForResult(intent, REQUEST_PICK);
                Log.i(TAG, "[FolioXR] Importar: selector del sistema abierto");
            } catch (ActivityNotFoundException e) {
                Log.e(TAG, "[FolioXR] Importar: Horizon OS no ofrece un selector de archivos (ACTION_OPEN_DOCUMENT)", e);
                finish("unavailable", "El selector de archivos no est\u00e1 disponible en este visor.");
                removeSelf();
            } catch (Throwable t) {
                Log.e(TAG, "[FolioXR] Importar: fallo al abrir el selector", t);
                finish("unavailable", "El selector de archivos no est\u00e1 disponible en este visor.");
                removeSelf();
            }
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            if (requestCode != REQUEST_PICK) {
                return;
            }
            final ArrayList<Uri> uris = new ArrayList<Uri>();
            if (resultCode == Activity.RESULT_OK && data != null) {
                ClipData clip = data.getClipData();
                if (clip != null) {
                    for (int i = 0; i < clip.getItemCount(); i++) {
                        if (clip.getItemAt(i).getUri() != null) {
                            uris.add(clip.getItemAt(i).getUri());
                        }
                    }
                } else if (data.getData() != null) {
                    uris.add(data.getData());
                }
            }
            removeSelf();
            Log.i(TAG, "[FolioXR] Importar: el selector devolvio " + uris.size() + " archivo(s), resultado " + resultCode);
            if (uris.isEmpty()) {
                finish("cancelled", "No se eligi\u00f3 ning\u00fan archivo.");
                return;
            }
            if (app == null) {
                finish("error", "La biblioteca no est\u00e1 lista.");
                return;
            }
            cancel = false;
            synchronized (STATUS_LOCK) {
                index = 0;
                count = uris.size();
                imported = 0;
                copied = total = 0;
                setStatus("copying", "", "");
            }
            IMPORT_WORKER.execute(new Runnable() {
                @Override
                public void run() {
                    importAll(uris);
                }
            });
        }

        private void removeSelf() {
            try {
                getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            } catch (Throwable ignored) {
                // La actividad ya no existe; no queda nada que limpiar.
            }
        }
    }

    static final class ImportException extends Exception {
        ImportException(String text) {
            super(text);
        }
    }

    private static void importAll(ArrayList<Uri> uris) {
        String lastError = "";
        int duplicates = 0;
        for (int i = 0; i < uris.size(); i++) {
            if (cancel) {
                break;
            }
            synchronized (STATUS_LOCK) {
                index = i + 1;
                copied = total = 0;
                serial++;
            }
            try {
                if (importOne(uris.get(i))) {
                    synchronized (STATUS_LOCK) {
                        imported++;
                        serial++;
                    }
                } else {
                    duplicates++;
                }
            } catch (ImportException e) {
                lastError = e.getMessage();
                Log.w(TAG, "[FolioXR] Importar: " + lastError);
            } catch (Throwable t) {
                lastError = cancel ? "" : "No se pudo copiar el archivo y no conocemos la causa.";
                Log.w(TAG, "[FolioXR] Importar: fallo inesperado", t);
            }
        }
        int done;
        synchronized (STATUS_LOCK) {
            done = imported;
        }
        if (cancel) {
            finish("cancelled", "Importaci\u00f3n cancelada. Quedaron " + done + " libro(s).");
            return;
        }
        String summary = done == 1 ? "Se import\u00f3 1 libro." : "Se importaron " + done + " libros.";
        if (duplicates > 0) {
            summary += " " + duplicates + " ya estaba(n) en la biblioteca.";
        }
        if (lastError.length() > 0) {
            finish("error", (done > 0 || duplicates > 0 ? summary + "\n" : "") + lastError);
        } else {
            finish("done", summary);
        }
    }

    /** Devuelve false si el libro ya estaba en la biblioteca. */
    private static boolean importOne(Uri uri) throws Exception {
        ContentResolver resolver = app.getContentResolver();
        String name = null;
        long size = -1;
        Cursor c = resolver.query(uri, new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE},
                null, null, null);
        if (c != null) {
            try {
                if (c.moveToFirst()) {
                    name = c.getString(0);
                    if (!c.isNull(1)) {
                        size = c.getLong(1);
                    }
                }
            } finally {
                c.close();
            }
        }
        if (name == null || name.length() == 0) {
            name = uri.getLastPathSegment() != null ? uri.getLastPathSegment() : "libro";
        }
        synchronized (STATUS_LOCK) {
            file = name;
            total = Math.max(0, size);
            copied = 0;
            serial++;
        }
        long free = tempDir.getUsableSpace();
        if (size > 0 && free < size + SPACE_MARGIN) {
            throw new ImportException("No hay espacio suficiente para \u00ab" + name + "\u00bb: necesita "
                    + megabytes(size) + " y quedan " + megabytes(free) + " libres.");
        }

        File part = new File(tempDir, UUID.randomUUID().toString() + ".part");
        long written = 0;
        try {
            InputStream in = resolver.openInputStream(uri);
            if (in == null) {
                throw new ImportException("No se pudo leer \u00ab" + name + "\u00bb.");
            }
            try {
                FileOutputStream out = new FileOutputStream(part);
                try {
                    byte[] buffer = new byte[BUFFER];
                    long lastReport = 0;
                    int n;
                    while ((n = in.read(buffer)) >= 0) {
                        if (cancel) {
                            throw new ImportException("Importaci\u00f3n cancelada.");
                        }
                        out.write(buffer, 0, n);
                        written += n;
                        long now = System.currentTimeMillis();
                        if (now - lastReport >= 250) {
                            lastReport = now;
                            synchronized (STATUS_LOCK) {
                                copied = written;
                                serial++;
                            }
                        }
                    }
                    out.flush();
                    out.getFD().sync();
                } finally {
                    out.close();
                }
            } finally {
                in.close();
            }
            if (written == 0) {
                throw new ImportException("El archivo \u00ab" + name + "\u00bb est\u00e1 vac\u00edo. Prueba a volver a descargarlo.");
            }
            if (size >= 0 && written != size) {
                throw new ImportException("La copia de \u00ab" + name + "\u00bb qued\u00f3 incompleta. Vuelve a intentarlo.");
            }

            return ingest(part, name, written);
        } finally {
            if (part.exists()) {
                part.delete();
            }
        }
    }

    /**
     * Ultimo tramo de toda importacion (selector o Wi-Fi): formato, id estable, diario, renombrado,
     * ficha y portada. Devuelve false si el libro ya estaba. El llamador borra "part" si sigue existiendo.
     */
    static boolean ingest(File part, String name, long written) throws Exception {
        String format = detectFormat(part, name);
        if ("acsm".equals(format)) {
            throw new ImportException("\u00ab" + name + "\u00bb es un comprobante de compra (ACSM), no el libro."
                    + " Esta app solo abre libros sin DRM.");
        }
        if (!Arrays.asList(KNOWN).contains(format)) {
            throw new ImportException("Formato a\u00fan no compatible: \u00ab" + name + "\u00bb.");
        }
        String id = stableId(part, written);
        if (hasBook(id)) {
            return false;
        }
        String title = name;
        int dot = title.lastIndexOf('.');
        if (dot > 0) {
            title = title.substring(0, dot);
        }
        File dest = new File(booksDir, id + "." + format);
        File journal = new File(tempDir, id + ".json");
        writeSmallText(journal, new JSONObject().put("id", id).put("title", title).put("format", format)
                .put("size", written).put("path", dest.getAbsolutePath()).toString());
        if (dest.exists()) {
            dest.delete(); // Copia huerfana de un intento anterior sin ficha.
        }
        if (!part.renameTo(dest)) {
            journal.delete();
            throw new ImportException("No se pudo guardar \u00ab" + name + "\u00bb en la biblioteca.");
        }
        insertBook(id, title, format, dest, written);
        journal.delete();
        if ("pdf".equals(format)) {
            makeCover(id, dest);
        }
        Log.i(TAG, "[FolioXR] Importar: \u00ab" + title + "\u00bb (" + format + ", " + megabytes(written) + ") id=" + id);
        return true;
    }

    static File newPartFile() {
        return new File(tempDir, UUID.randomUUID().toString() + ".part");
    }

    static long freeSpace() {
        return tempDir.getUsableSpace();
    }

    static boolean ready() {
        return helper != null;
    }

    static String megabytesText(long bytes) {
        return megabytes(bytes);
    }

    /** Hash de 1 MB inicial + 1 MB final + tamano: estable aunque cambien el nombre o la fecha. */
    private static String stableId(File source, long size) throws Exception {
        MessageDigest sha = MessageDigest.getInstance("SHA-256");
        byte[] buffer = new byte[BUFFER];
        RandomAccessFile raf = new RandomAccessFile(source, "r");
        try {
            int n = readFully(raf, buffer);
            sha.update(buffer, 0, n);
            if (size > BUFFER) {
                raf.seek(Math.max(BUFFER, size - BUFFER));
                n = readFully(raf, buffer);
                sha.update(buffer, 0, n);
            }
        } finally {
            raf.close();
        }
        sha.update(Long.toString(size).getBytes("UTF-8"));
        byte[] digest = sha.digest();
        StringBuilder hex = new StringBuilder();
        for (int i = 0; i < 8; i++) {
            hex.append(String.format(Locale.US, "%02x", digest[i] & 0xff));
        }
        return hex.toString() + "-" + size;
    }

    private static int readFully(RandomAccessFile raf, byte[] buffer) throws Exception {
        int offset = 0;
        while (offset < buffer.length) {
            int n = raf.read(buffer, offset, buffer.length - offset);
            if (n < 0) {
                break;
            }
            offset += n;
        }
        return offset;
    }

    /** Formato por contenido (firma de bytes); la extension solo desempata dentro de una familia. */
    private static String detectFormat(File source, String name) throws Exception {
        byte[] head = new byte[96];
        int n;
        RandomAccessFile raf = new RandomAccessFile(source, "r");
        try {
            n = readFully(raf, head);
        } finally {
            raf.close();
        }
        String extension = "";
        int dot = name.lastIndexOf('.');
        if (dot >= 0 && dot < name.length() - 1) {
            extension = name.substring(dot + 1).toLowerCase(Locale.US);
        }
        String text = new String(head, 0, n, "ISO-8859-1");
        if (text.startsWith("%PDF")) {
            return "pdf";
        }
        if (text.startsWith("PK\u0003\u0004")) {
            if (text.contains("mimetypeapplication/epub+zip") || "epub".equals(extension)) {
                return "epub";
            }
            if ("cbz".equals(extension) || "docx".equals(extension) || "odt".equals(extension)) {
                return extension;
            }
            return "zip";
        }
        if (text.startsWith("Rar!")) {
            return "cbr";
        }
        if (text.startsWith("7z\u00bc\u00af")) {
            return "cb7";
        }
        if (n >= 68 && text.substring(60, 68).equals("BOOKMOBI")) {
            return "azw3".equals(extension) || "azw".equals(extension) ? extension : "mobi";
        }
        if (text.startsWith("{\\rtf")) {
            return "rtf";
        }
        if (text.startsWith("AT&TFORM")) {
            return "djvu";
        }
        if (text.contains("<fulfillmentToken") || "acsm".equals(extension)) {
            return "acsm";
        }
        if ("txt".equals(extension) || "fb2".equals(extension)) {
            return extension;
        }
        return extension.length() > 0 ? extension : "desconocido";
    }

    /** Miniatura de la primera pagina (256 px de ancho). Si falla, el libro queda sin portada. */
    private static void makeCover(String id, File pdf) {
        ParcelFileDescriptor descriptor = null;
        PdfRenderer renderer = null;
        Bitmap bitmap = null;
        try {
            descriptor = ParcelFileDescriptor.open(pdf, ParcelFileDescriptor.MODE_READ_ONLY);
            renderer = new PdfRenderer(descriptor);
            descriptor = null; // Desde aqui lo cierra el renderer.
            if (renderer.getPageCount() <= 0) {
                return;
            }
            PdfRenderer.Page page = renderer.openPage(0);
            try {
                int width = 256;
                int height = Math.max(64, Math.min(512, Math.round(width * page.getHeight() / (float) page.getWidth())));
                bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
                bitmap.eraseColor(Color.WHITE);
                page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
            } finally {
                page.close();
            }
            File target = new File(coversDir, id + ".png");
            FileOutputStream out = new FileOutputStream(target);
            try {
                bitmap.compress(Bitmap.CompressFormat.PNG, 100, out);
            } finally {
                out.close();
            }
            ContentValues values = new ContentValues();
            values.put("cover", target.getAbsolutePath());
            helper.getWritableDatabase().update("books", values, "id = ?", new String[]{id});
        } catch (Throwable t) {
            Log.w(TAG, "[FolioXR] Importar: sin portada para " + id + " (" + t + ")");
        } finally {
            if (bitmap != null) {
                bitmap.recycle();
            }
            try {
                if (renderer != null) {
                    renderer.close();
                }
                if (descriptor != null) {
                    descriptor.close();
                }
            } catch (Throwable ignored) {
                // Nada que recuperar: la portada es opcional.
            }
        }
    }

    // ------------------------------------------------------------------ Utilidades

    private static String megabytes(long bytes) {
        return bytes >= 1L << 30 ? String.format(Locale.US, "%.1f GB", bytes / (double) (1L << 30))
                : String.format(Locale.US, "%.1f MB", bytes / (double) (1L << 20));
    }

    private static void deleteQuietly(String path) {
        if (path == null || path.length() == 0) {
            return;
        }
        try {
            new File(path).delete();
        } catch (Throwable ignored) {
            // Queda como archivo huerfano; no afecta a la biblioteca.
        }
    }

    private static String readSmallText(File source) throws Exception {
        byte[] data = new byte[(int) Math.min(source.length(), 65536)];
        FileInputStream in = new FileInputStream(source);
        try {
            int offset = 0;
            while (offset < data.length) {
                int n = in.read(data, offset, data.length - offset);
                if (n < 0) {
                    break;
                }
                offset += n;
            }
            return new String(data, 0, offset, "UTF-8");
        } finally {
            in.close();
        }
    }

    private static void writeSmallText(File target, String text) throws Exception {
        FileOutputStream out = new FileOutputStream(target);
        try {
            out.write(text.getBytes("UTF-8"));
            out.flush();
            out.getFD().sync();
        } finally {
            out.close();
        }
    }
}
