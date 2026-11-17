package com.loklj.folioxr;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.Locale;

/**
 * «Nuevo entorno» (pedido del usuario, 17 nov 2026): elegir una foto 360° del visor con el selector del
 * sistema y copiarla a la carpeta privada de la app, donde Unity la usa de entorno (CustomEnvironments).
 * El estado se consulta con status(): "idle", "picking", "copying", "done|ruta", "cancelled" o "error|mensaje".
 */
public final class FolioImagePicker {
    private static final String TAG = "FolioXR";
    private static final int REQUEST_IMAGE = 0x4F59;
    private static final long MAX_BYTES = 80L * 1024 * 1024;
    private static final Object LOCK = new Object();
    private static String state = "idle";
    private static String folder;

    private FolioImagePicker() {
    }

    public static String status() {
        synchronized (LOCK) {
            return state;
        }
    }

    private static void set(String value) {
        synchronized (LOCK) {
            state = value;
        }
        Log.i(TAG, "[FolioXR] Entorno propio: " + value);
    }

    /** Abre el selector de imágenes; la copia queda en destination (se crea si falta). */
    public static void pick(final Activity activity, String destination) {
        synchronized (LOCK) {
            if ("picking".equals(state) || "copying".equals(state)) {
                return;
            }
            state = "picking";
            folder = destination;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    activity.getFragmentManager().beginTransaction()
                            .add(new PickerFragment(), "folio-entorno").commitAllowingStateLoss();
                } catch (Throwable t) {
                    Log.e(TAG, "[FolioXR] Entorno propio: no se pudo abrir el selector", t);
                    set("error|El selector de archivos no está disponible en este visor.");
                }
            }
        });
    }

    /** Fragment sin interfaz que recibe la imagen elegida. */
    public static final class PickerFragment extends Fragment {
        public PickerFragment() {
        }

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            if (savedInstanceState != null) {
                return;
            }
            try {
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("image/*");
                intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"image/jpeg", "image/png"});
                startActivityForResult(intent, REQUEST_IMAGE);
            } catch (ActivityNotFoundException e) {
                set("error|El selector de archivos no está disponible en este visor.");
                removeSelf();
            } catch (Throwable t) {
                Log.e(TAG, "[FolioXR] Entorno propio: fallo al abrir el selector", t);
                set("error|El selector de archivos no está disponible en este visor.");
                removeSelf();
            }
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            if (requestCode != REQUEST_IMAGE) {
                return;
            }
            final Uri uri = resultCode == Activity.RESULT_OK && data != null ? data.getData() : null;
            final Activity activity = getActivity();
            removeSelf();
            if (uri == null || activity == null) {
                set("cancelled");
                return;
            }
            set("copying");
            new Thread(new Runnable() {
                @Override
                public void run() {
                    copy(activity, uri);
                }
            }, "FolioXR entorno").start();
        }

        private void removeSelf() {
            try {
                getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            } catch (Throwable ignored) {
                // La actividad ya no existe.
            }
        }
    }

    private static void copy(Activity activity, Uri uri) {
        String name = "";
        try (Cursor c = activity.getContentResolver().query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null)) {
            if (c != null && c.moveToFirst() && !c.isNull(0)) {
                name = c.getString(0);
            }
        } catch (Throwable ignored) {
            // Sin nombre: se usa .jpg.
        }
        String extension = name.toLowerCase(Locale.ROOT).endsWith(".png") ? ".png" : ".jpg";
        File dir = new File(folder);
        if (!dir.isDirectory() && !dir.mkdirs()) {
            set("error|No se pudo crear la carpeta de entornos.");
            return;
        }
        File target = new File(dir, "entorno-" + System.currentTimeMillis() + extension);
        long copied = 0;
        try (InputStream in = activity.getContentResolver().openInputStream(uri);
             FileOutputStream out = new FileOutputStream(target)) {
            if (in == null) {
                throw new java.io.IOException("sin datos");
            }
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = in.read(buffer)) > 0) {
                copied += n;
                if (copied > MAX_BYTES) {
                    throw new java.io.IOException("la imagen pesa más de 80 MB");
                }
                out.write(buffer, 0, n);
            }
        } catch (Throwable t) {
            //noinspection ResultOfMethodCallIgnored
            target.delete();
            Log.e(TAG, "[FolioXR] Entorno propio: no se pudo copiar la imagen", t);
            set("error|No se pudo copiar la imagen: " + t.getMessage());
            return;
        }
        set("done|" + target.getAbsolutePath());
    }
}
