package com.loklj.folioxr;

import android.content.Context;
import android.graphics.*;
import android.os.*;
import android.util.Log;
import android.webkit.*;
import android.view.View;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

/** EPUB: cola serial, UI para WebView y trabajador para ZIP/PNG. Sin Unity ni Gradle. */
public final class FolioEpubRenderer {
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static final ExecutorService WORK = Executors.newSingleThreadExecutor();
    private static final ConcurrentHashMap<String,String> STATUS = new ConcurrentHashMap<>();
    private static final AtomicInteger GENERATION = new AtomicInteger();
    private static android.app.Activity context;
    private static WebView web;
    private static FolioHtmlSource book;
    private static String txtEncoding = "auto";
    private static String path;
    private static FolioReadingSettings layout=new FolioReadingSettings(0,1.4f,48);
    private static float fontScale = 1f;
    private static int[] counts;
    private static int loaded = -1;
    private static final int WIDTH = 596, HEIGHT = 842;
    private static final LinkedHashMap<String,byte[]> CACHE = new LinkedHashMap<>(8, .75f, true);
    private static int cacheBytes;
    private static final int CACHE_LIMIT = 24 * 1024 * 1024;

    public static void init(Context activity, float scale) {
        context = (android.app.Activity)activity;
        WORK.execute(() -> { if (fontScale != scale) { fontScale = scale; reset(); } });
    }
    public static void setTxtEncoding(String encoding) {
        WORK.execute(() -> { if (!txtEncoding.equals(encoding)) { txtEncoding = encoding; reset(); } });
    }
    public static void setLayout(int font,float line,int margin) {
        FolioReadingSettings next=new FolioReadingSettings(font,line,margin);
        WORK.execute(() -> { if(!layout.equals(next)){layout=next;reset();} });
    }
    public static String getStatus(String key) { return STATUS.get(key); }
    public static void clearStatus(String key) {
        if ("PENDING".equals(STATUS.remove(key))) { GENERATION.incrementAndGet(); WORK.execute(FolioEpubRenderer::reset); }
    }
    public static void close() {
        GENERATION.incrementAndGet(); STATUS.clear();
        WORK.execute(FolioEpubRenderer::reset);
    }
    private static void reset() {
        try { ui(() -> { if (web != null) { web.stopLoading(); if (web.getParent() instanceof android.view.ViewGroup) ((android.view.ViewGroup)web.getParent()).removeView(web); web.destroy(); web = null; } return null; }); }
        catch (Exception ignored) { }
        try { if (book != null) book.close(); } catch (IOException ignored) { }
        book = null; path = null; counts = null; loaded = -1; CACHE.clear(); cacheBytes = 0;
    }
    private interface Job { String run(int generation) throws Exception; }
    private static void submit(String key, Job job) {
        int generation = GENERATION.get();
        STATUS.put(key, "PENDING");
        WORK.execute(() -> {
            try {
                check(generation, key);
                String result = job.run(generation);
                check(generation, key); STATUS.replace(key, result);
            } catch (Throwable e) {
                String kind = e instanceof FolioEpubContainer.DrmException ? "DRM" :
                    e instanceof OutOfMemoryError ? "MEMORY" : e instanceof TimeoutException ? "TIMEOUT" : "IO";
                STATUS.replace(key, "ERROR:" + kind + ":" + e.getMessage());
                Log.w("FolioXR", "[FolioXR] EPUB: " + kind);
            }
        });
    }
    private static void check(int generation, String key) throws IOException {
        if (generation != GENERATION.get() || (key != null && !STATUS.containsKey(key))) throw new IOException("Cancelado");
    }
    private static <T> T ui(Callable<T> action) throws Exception {
        FutureTask<T> task = new FutureTask<>(action); UI.post(task);
        try { return task.get(20, TimeUnit.SECONDS); }
        catch (Exception e) { task.cancel(false); throw e; }
    }
    private static void open(String file, int generation) throws Exception {
        if (file.equals(path) && counts != null) return;
        reset();
        if (context == null) throw new IOException("EPUB sin contexto Android");
        try(InputStream font=context.getAssets().open("folio-fonts/"+layout.family()+"-Regular.ttf")) { if(font.read()<0)throw new IOException("Fuente incluida ausente"); }
        book = FolioHtmlSource.open(file, txtEncoding, () -> generation != GENERATION.get());
        book.checkDrm();
        ui(() -> {
            WebView.enableSlowWholeDocumentDraw();
            web = new WebView(context);
            web.setLayerType(View.LAYER_TYPE_SOFTWARE, null);
            web.getSettings().setJavaScriptEnabled(true);
            web.getSettings().setAllowFileAccess(false);
            web.getSettings().setAllowContentAccess(false);
            web.getSettings().setBlockNetworkLoads(true);
            web.getSettings().setCacheMode(WebSettings.LOAD_NO_CACHE);
            web.getSettings().setDefaultTextEncodingName("UTF-8");
            web.setInitialScale(100);
            ((android.view.ViewGroup)context.getWindow().getDecorView()).addView(web,
                new android.view.ViewGroup.LayoutParams(WIDTH, HEIGHT));
            web.setTranslationX(-10000f);
            web.measure(View.MeasureSpec.makeMeasureSpec(WIDTH, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(HEIGHT, View.MeasureSpec.EXACTLY));
            web.layout(0, 0, WIDTH, HEIGHT);
            return null;
        });
        int[] result = new int[book.chapterCount()];
        for (int i = 0; i < result.length; i++) {
            check(generation, null); load(i);
            String number = js("Math.max(1,Math.ceil((document.body.scrollWidth+"+layout.margin+")/596))");
            result[i] = Integer.parseInt(number);
            if (result[i] > 100000) throw new IOException("Capitulo demasiado extenso");
        }
        counts = result; path = file;
    }
    private static String js(String script) throws Exception {
        CompletableFuture<String> result = new CompletableFuture<>();
        ui(() -> { web.evaluateJavascript(script, result::complete); return null; });
        return result.get(20, TimeUnit.SECONDS);
    }
    private static void load(int chapter) throws Exception {
        if (chapter == loaded) return;
        final FolioHtmlSource source = book;
        String resource = source.chapterName(chapter);
        String content = new String(source.chapter(chapter), StandardCharsets.UTF_8);
        String policy = "default-src 'none'; img-src https://folio.invalid data:; style-src 'unsafe-inline' https://folio.invalid; font-src https://folio.invalid/folio-fonts/; script-src 'none';";
        String prefix = "<meta http-equiv='Content-Security-Policy' content=\"" + policy + "\"><meta name='viewport' content='width=596'>";
        String css = layout.css(fontScale);
        // CSP precede al contenido del editor; evaluateJavascript solo ejecuta codigo propio.
        String html = "<!doctype html><html><head>" + prefix + "</head><body>" + content + css + "</body></html>";
        CompletableFuture<Void> ready = new CompletableFuture<>();
        ui(() -> {
            web.setWebViewClient(new WebViewClient() {
                @Override public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest r) { return true; }
                @Override public WebResourceResponse shouldInterceptRequest(WebView v, WebResourceRequest request) {
                    try {
                        String name = FolioEpubContainer.resolve("", request.getUrl().toString());
                        if(FolioReadingSettings.fontAsset(name))return new WebResourceResponse("font/ttf",null,context.getAssets().open(name));
                        if (source.isFont(name)) throw new IOException("Fuente del libro no habilitada");
                        String mime = name.endsWith(".css") ? "text/css" : name.endsWith(".svg") ? "image/svg+xml" :
                            name.endsWith(".png") ? "image/png" : "image/jpeg";
                        byte[] data = source.resource(name, FolioEpubContainer.MAX_RESOURCE);
                        if (!mime.equals("text/css") && !mime.equals("image/svg+xml")) {
                            BitmapFactory.Options bounds = new BitmapFactory.Options(); bounds.inJustDecodeBounds = true;
                            BitmapFactory.decodeByteArray(data, 0, data.length, bounds);
                            if (bounds.outWidth <= 0 || bounds.outHeight <= 0 ||
                                (long)bounds.outWidth * bounds.outHeight > 4000000L) throw new IOException("Imagen demasiado grande");
                        }
                        return new WebResourceResponse(mime, "UTF-8", new ByteArrayInputStream(data));
                    } catch (Exception e) { return new WebResourceResponse("text/plain", "UTF-8", new ByteArrayInputStream(new byte[0])); }
                }
                @Override public void onPageFinished(WebView v, String url) {
                    v.postVisualStateCallback(1, new WebView.VisualStateCallback() {
                        @Override public void onComplete(long id) { ready.complete(null); }
                    });
                }
            });
            String url = new java.net.URI("https", "folio.invalid", "/" + resource, null).toASCIIString();
            web.loadDataWithBaseURL(url, html, "text/html", "UTF-8", null);
            return null;
        });
        ready.get(20, TimeUnit.SECONDS);
        js("window.folioFontsReady=false;document.fonts.ready.then(function(){window.folioFontsReady=true;});true");
        long until=SystemClock.elapsedRealtime()+20000;
        while(!"true".equals(js("window.folioFontsReady===true"))){
            if(SystemClock.elapsedRealtime()>until)throw new TimeoutException("Carga de fuentes");Thread.sleep(25);
        }
        loaded = chapter;
    }
    public static void requestPageCount(String file, String key) {
        submit(key, generation -> { open(file, generation); int total = 0; for (int n : counts) total += n; return "OK:" + total; });
    }
    public static void requestContents(String file,String key){
        submit(key,generation->{
            open(file,generation);List<FolioContents.Entry> entries=book.contents();int first=0;
            for(int chapter=0;chapter<counts.length;chapter++){
                check(generation,key);boolean needed=false;
                for(FolioContents.Entry e:entries)if(e.chapter==chapter&&!e.fragment.isEmpty())needed=true;
                if(needed){load(chapter);js("document.body.style.transform='none';true");}
                for(FolioContents.Entry e:entries)if(e.chapter==chapter){
                    check(generation,key);int local=0;
                    if(!e.fragment.isEmpty()){
                        String name=FolioContents.quote(e.fragment);
                        String result=js("(function(){var e=document.getElementById("+name+")||document.getElementsByName("+name+")[0];if(!e)return -1;var r=e.getClientRects();return r.length?Math.max(0,Math.floor(r[0].left/596)):-1;})()");
                        local=Integer.parseInt(result);
                    }
                    e.page=local<0?-1:first+Math.min(local,counts[chapter]-1);
                }
                first+=counts[chapter];
            }
            return "OK:"+FolioContents.json(entries);
        });
    }
    /** Busqueda en el libro abierto: hasta limit coincidencias desde el cursor "capitulo:saltar". */
    public static void requestSearch(String file, String query, String from, int limit, String key) {
        submit(key, generation -> {
            String q = FolioBookSearch.clip(query);
            if (q.isEmpty()) return "OK:" + FolioBookSearch.json(new ArrayList<>(), "", "Escribe qué buscar.");
            open(file, generation);
            int[] c = FolioBookSearch.cursor(from);
            int want = FolioBookSearch.clamp(limit), first = 0, chapter = Math.min(c[0], counts.length), skip = c[1];
            for (int i = 0; i < chapter; i++) first += counts[i];
            List<FolioBookSearch.Hit> hits = new ArrayList<>();
            String next = "";
            for (; chapter < counts.length; chapter++) {
                check(generation, key); load(chapter);
                org.json.JSONObject result = new org.json.JSONObject(js(FolioBookSearch.script(q, skip, want - hits.size())));
                org.json.JSONArray found = result.getJSONArray("hits");
                int last = 0;
                for (int i = 0; i < found.length(); i++) {
                    org.json.JSONObject hit = found.getJSONObject(i);
                    int local = hit.optInt("page", -1); local = local < 0 ? last : Math.min(local, counts[chapter] - 1); last = local;
                    hits.add(new FolioBookSearch.Hit(first + local, hit.optString("text", "")));
                }
                if (result.optBoolean("more")) { next = chapter + ":" + (skip + found.length()); break; }
                skip = 0; first += counts[chapter];
                if (hits.size() >= want) { if (chapter + 1 < counts.length) next = (chapter + 1) + ":0"; break; }
            }
            String message = hits.isEmpty() ? (from == null || from.isEmpty() ? "Sin coincidencias." : "No hay más coincidencias.") : "";
            return "OK:" + FolioBookSearch.json(hits, next, message);
        });
    }
    private static byte[] page(int index, int width, int generation) throws Exception {
        check(generation, null);
        String cacheKey = index + ":" + width;
        byte[] cached = CACHE.get(cacheKey); if (cached != null) return cached;
        int chapter = 0, local = index;
        while (chapter < counts.length && local >= counts[chapter]) local -= counts[chapter++];
        if (index < 0 || chapter >= counts.length) throw new IOException("Pagina fuera de rango");
        load(chapter);
        js("document.body.style.transform='translateX(-" + (local * 596L) + "px)';true");
        CompletableFuture<Void> visual = new CompletableFuture<>();
        ui(() -> { web.postVisualStateCallback(2, new WebView.VisualStateCallback() {
            @Override public void onComplete(long id) { visual.complete(null); }
        }); return null; });
        visual.get(20, TimeUnit.SECONDS);
        long start = SystemClock.elapsedRealtime();
        Bitmap bitmap = ui(() -> {
            Bitmap b = Bitmap.createBitmap(width, Math.round(width * HEIGHT / (float)WIDTH), Bitmap.Config.ARGB_8888);
            b.eraseColor(Color.WHITE);
            Canvas canvas = new Canvas(b); canvas.scale(width / (float)WIDTH, width / (float)WIDTH);
            try { web.draw(canvas); return b; } catch (Throwable e) { b.recycle(); throw e; }
        });
        byte[] bytes;
        try (ByteArrayOutputStream out = new ByteArrayOutputStream()) {
            if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, out)) throw new IOException("PNG fallido");
            bytes = out.toByteArray();
        } finally { bitmap.recycle(); }
        Log.i("FolioXR", "[FolioXR] EPUB captura pagina=" + index + " ms=" + (SystemClock.elapsedRealtime()-start) + " ancho=" + width);
        while (cacheBytes + bytes.length > CACHE_LIMIT && !CACHE.isEmpty()) {
            String first = CACHE.keySet().iterator().next(); cacheBytes -= CACHE.remove(first).length;
        }
        if (bytes.length <= CACHE_LIMIT) { CACHE.put(cacheKey, bytes); cacheBytes += bytes.length; }
        return bytes;
    }
    public static void requestRenderPage(String file, int index, int targetWidth, String output, String key) {
        submit(key, generation -> {
            open(file, generation);
            int width = Math.max(64, Math.min(3072, targetWidth));
            byte[] bytes = page(index, width, generation); check(generation, key);
            File out = new File(output), tmp = new File(output + ".tmp");
            try {
                File parent = out.getParentFile();
                if (parent != null && !parent.isDirectory() && !parent.mkdirs()) throw new IOException("No se pudo crear cache");
                try (FileOutputStream stream = new FileOutputStream(tmp)) { stream.write(bytes); }
                check(generation, key);
                if (!tmp.renameTo(out)) throw new IOException("No se pudo publicar pagina");
            } finally { tmp.delete(); }
            // Precarga en la misma cola; nunca mantiene bitmaps de varias paginas.
            if ((index & 1) == 1) WORK.execute(() -> {
                try {
                    int total = 0; for (int n : counts) total += n;
                    for (int i = index + 1; i <= index + 2 && i < total; i++) page(i, width, generation);
                } catch (Exception ignored) { }
            });
            return "OK:" + width + "x" + Math.round(width * HEIGHT / (float)WIDTH);
        });
    }
}
