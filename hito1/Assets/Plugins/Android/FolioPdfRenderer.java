package com.loklj.folioxr;

import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import android.util.Log;
import java.io.*;
import java.nio.*;
import java.nio.channels.FileChannel;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

/** PDF por descriptor de 64 bits; cola cancelable, FXR1 acotado y reduccion de resolucion. */
public final class FolioPdfRenderer {
    private static final ExecutorService WORK=Executors.newSingleThreadExecutor();
    private static final ConcurrentHashMap<String,String> STATUS=new ConcurrentHashMap<>();
    private static final ConcurrentHashMap<String,Boolean> HITS=new ConcurrentHashMap<>();
    private static final AtomicInteger GENERATION=new AtomicInteger(), DEMAND=new AtomicInteger();
    private static String openPath;
    private static ParcelFileDescriptor descriptor;
    private static PdfRenderer renderer;
    private static int last=-1;
    private static FolioRawCache cache;
    private static final LinkedHashMap<String,Boolean> SEEN=new LinkedHashMap<String,Boolean>(16,.75f,true){
        protected boolean removeEldestEntry(Map.Entry<String,Boolean> e){return size()>64;}
    };
    private interface Job {String run() throws Exception;}
    private static void submit(String key,Job job){
        int generation=GENERATION.get();STATUS.put(key,"PENDING");
        WORK.execute(()->{
            if(generation!=GENERATION.get() || !STATUS.containsKey(key))return;
            try{String result=job.run();if(generation==GENERATION.get())STATUS.replace(key,result);}
            catch(Throwable t){STATUS.replace(key,error(t));}
        });
    }
    public static void requestPageCount(String path,String key){submit(key,()->"OK:"+ensureOpen(path).getPageCount());}
    public static void requestContents(String path,String key){submit(key,()->{
        try(FolioPdfOutline reader=new FolioPdfOutline(path,()->!STATUS.containsKey(key))){return "OK:"+FolioContents.json(reader.read());}
    });}
    public static void requestRenderPage(String path,int index,int width,String output,String key){
        int demand=DEMAND.incrementAndGet(),generation=GENERATION.get();
        submit(key,()->{
            PdfRenderer pdf=ensureOpen(path);if(index<0 || index>=pdf.getPageCount())throw new IOException("Pagina fuera de rango");
            File out=new File(output),parent=out.getParentFile();
            if(parent==null || (!parent.isDirectory()&&!parent.mkdirs()))throw new IOException("Cache no disponible");
            if(cache==null || !cache.folder.getParentFile().equals(parent))cache=new FolioRawCache(parent);
            int effective=effectiveWidth(width);long start=System.nanoTime();
            boolean hit=cache.take(cache.file(path,index,effective),out);
            HITS.put(key,hit);
            String size=hit?FolioRawCache.size(out):render(pdf,index,effective,out);
            if(!STATUS.containsKey(key) || generation!=GENERATION.get()){out.delete();HITS.remove(key);return "ERROR:CANCEL:Cancelado";}
            FolioPerformance.record(hit?"pdf_cached_ms":"pdf_render_ms",elapsed(start));
            if(last>=0 && Math.abs((long)index-last)>2)FolioPerformance.record("pdf_random_ms",elapsed(start));
            boolean forward=index>=last;last=index;
            if((index%2==1 || index==pdf.getPageCount()-1) && FolioPerformance.pressure()==0){
                int first=forward?index+1:index-3;
                WORK.execute(()->prefetch(path,first,effective,demand,generation));
            }
            return "OK:"+size;
        });
    }
    private static int effectiveWidth(int width){int pressure=FolioPerformance.pressure();return Math.max(64,Math.min(width,pressure==2?832:pressure==1?1400:4096));}
    private static void prefetch(String path,int first,int width,int demand,int generation){
        try{
            for(int page=Math.max(0,first);page<=first+1;page++){
                if(demand!=DEMAND.get() || generation!=GENERATION.get() || FolioPerformance.pressure()!=0 ||
                   renderer==null || !path.equals(openPath) || page>=renderer.getPageCount())return;
                File file=cache.file(path,page,width);if(file.exists())continue;
                cache.trim(32L<<20);render(renderer,page,width,file);file.setLastModified(System.currentTimeMillis());cache.trim(0);
            }
        }catch(Throwable e){Log.w("FolioXR","[FolioXR] Precarga PDF omitida: "+e.getClass().getSimpleName());}
    }
    private static String render(PdfRenderer pdf,int index,int requested,File out)throws Exception{
        int width=effectiveWidth(requested);
        for(int attempt=0;;attempt++){
            Bitmap bitmap=null;PdfRenderer.Page page=null;long reservation=0;
            try{
                page=pdf.openPage(index);
                int[] size=FolioRenderPolicy.dimensions(width,page.getWidth(),page.getHeight(),FolioRenderPolicy.pixels(FolioPerformance.pressure()));
                long bytes=4L*size[0]*size[1];
                if(!FolioRenderPolicy.acquire(bytes))throw new OutOfMemoryError("Presupuesto global de capturas");
                reservation=bytes;
                if(!FolioLargeFiles.fits(FolioLargeFiles.unreserved(out.getParentFile().getUsableSpace()),bytes+16))throw new IOException("Sin espacio para la pagina");
                bitmap=Bitmap.createBitmap(size[0],size[1],Bitmap.Config.ARGB_8888);bitmap.eraseColor(Color.WHITE);
                Matrix transform=new Matrix();transform.setScale(size[0]/(float)Math.max(1,page.getWidth()),size[1]/(float)Math.max(1,page.getHeight()));
                page.render(bitmap,null,transform,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                writeRaw(bitmap,out);return size[0]+"x"+size[1];
            }catch(OutOfMemoryError | IllegalArgumentException e){
                FolioPerformance.memoryFailure();
                if(attempt>=3 || width<=128)throw e;
                width=Math.max(64,width/2);
                Log.w("FolioXR","[FolioXR] Pagina PDF demasiado grande; reintento a "+width+" px");
            }finally{if(bitmap!=null)bitmap.recycle();FolioRenderPolicy.release(reservation);if(page!=null)page.close();}
        }
    }
    /** Una fila auxiliar: evita duplicar el bitmap completo en un ByteBuffer directo. */
    private static void writeRaw(Bitmap bitmap,File out)throws IOException{
        File tmp=new File(out.getPath()+".tmp");int w=bitmap.getWidth(),h=bitmap.getHeight();
        ByteBuffer header=ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN);
        header.put(new byte[]{'F','X','R','1'}).putInt(w).putInt(h).putInt(0).flip();
        int[] row=new int[w];ByteBuffer rgba=ByteBuffer.allocate(w*4);
        try{
            try(FileOutputStream stream=new FileOutputStream(tmp)){
                FileChannel channel=stream.getChannel();while(header.hasRemaining())channel.write(header);
                for(int y=h-1;y>=0;y--){
                    bitmap.getPixels(row,0,w,0,y,w,1);rgba.clear();
                    for(int color:row)rgba.put((byte)(color>>16)).put((byte)(color>>8)).put((byte)color).put((byte)(color>>>24));
                    rgba.flip();while(rgba.hasRemaining())channel.write(rgba);
                }
            }
            if((out.exists()&&!out.delete())||!tmp.renameTo(out))throw new IOException("No se pudo publicar FXR1");
        }finally{tmp.delete();}
    }
    private static PdfRenderer ensureOpen(String path)throws Exception{
        if(renderer!=null && path.equals(openPath))return renderer;
        closeCurrent();long start=System.nanoTime();File file=new File(path);
        long bytes=file.length();if(bytes<=0)throw new IOException("PDF vacio o ausente");
        ParcelFileDescriptor fd=ParcelFileDescriptor.open(file,ParcelFileDescriptor.MODE_READ_ONLY);
        try{
            long descriptorBytes=fd.getStatSize();
            if(descriptorBytes>=0 && descriptorBytes!=bytes)throw new IOException("El PDF cambio mientras se abria");
            renderer=new PdfRenderer(fd);descriptor=fd;openPath=path;last=-1;
            boolean warm=SEEN.put(path,Boolean.TRUE)!=null;
            FolioPerformance.record(warm?"pdf_open_warm_ms":"pdf_open_cold_ms",elapsed(start));
            Log.i("FolioXR","[FolioXR] PDF abierto: bytes="+bytes+" paginas="+renderer.getPageCount());return renderer;
        }catch(Throwable e){fd.close();throw e;}
    }
    private static double elapsed(long start){return (System.nanoTime()-start)/1000000.0;}
    public static String getStatus(String key){return STATUS.get(key);}
    public static boolean wasCached(String key){return Boolean.TRUE.equals(HITS.get(key));}
    public static void clearStatus(String key){HITS.remove(key);if("PENDING".equals(STATUS.remove(key)))DEMAND.incrementAndGet();}
    public static void close(){GENERATION.incrementAndGet();DEMAND.incrementAndGet();STATUS.clear();HITS.clear();WORK.execute(FolioPdfRenderer::closeCurrent);}
    private static void closeCurrent(){
        if(renderer!=null)try{renderer.close();}catch(Exception ignored){}
        if(descriptor!=null)try{descriptor.close();}catch(Exception ignored){}
        renderer=null;descriptor=null;openPath=null;last=-1;
    }
    private static String error(Throwable t){
        Log.w("FolioXR","[FolioXR] PDF: "+t.getClass().getSimpleName());
        String kind=t instanceof SecurityException?"PASSWORD":t instanceof OutOfMemoryError?"MEMORY":t instanceof IOException?"IO":"OTHER";
        return "ERROR:"+kind+":"+t.getClass().getSimpleName()+": "+t.getMessage();
    }
}
