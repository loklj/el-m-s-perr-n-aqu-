package com.loklj.folioxr;
import java.io.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

/** CBR comparte el lector de imagenes; RAR4 se prepara en disco con cuota. */
public final class FolioCbrRenderer {
    private static final ExecutorService WORK=Executors.newSingleThreadExecutor();
    private static final ConcurrentHashMap<String,String> STATUS=new ConcurrentHashMap<>();
    private static final AtomicInteger GENERATION=new AtomicInteger();
    private static String openPath;
    private static File prepared;
    private static void release(){if(prepared!=null && !prepared.getPath().equals(openPath))prepared.delete();prepared=null;openPath=null;}
    private static void request(String path,String key,int page,int width,String output){
        int generation=GENERATION.get(); STATUS.put(key,"PENDING");
        WORK.execute(()->{
            try{
                if(generation!=GENERATION.get() || !STATUS.containsKey(key))return;
                if(!path.equals(openPath)) {
                    release(); File result=FolioCbrArchive.prepare(new File(path),()->generation!=GENERATION.get() || !STATUS.containsKey(key));
                    prepared=result;openPath=path;
                }
                if(generation!=GENERATION.get() || !STATUS.containsKey(key))return;
                if(page<0)FolioComicRenderer.requestPageCount(prepared.getPath(),key);
                else FolioComicRenderer.requestRenderPage(prepared.getPath(),page,width,output,key);
                STATUS.replace(key,"DELEGATED");
            }catch(Exception e){STATUS.replace(key,"ERROR:IO:"+e.getMessage());}
        });
    }
    public static void requestPageCount(String path,String key){request(path,key,-1,0,null);}
    public static void requestRenderPage(String path,int page,int width,String output,String key){request(path,key,page,width,output);}
    public static String getStatus(String key){String status=STATUS.get(key);return "DELEGATED".equals(status)?FolioComicRenderer.getStatus(key):status;}
    public static void clearStatus(String key){STATUS.remove(key);FolioComicRenderer.clearStatus(key);}
    public static void close(){GENERATION.incrementAndGet();STATUS.clear();FolioComicRenderer.close();WORK.execute(FolioCbrRenderer::release);}
}
