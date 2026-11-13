package com.loklj.folioxr;
import java.io.*;import java.nio.*;import java.nio.file.*;import java.lang.reflect.*;import java.util.concurrent.*;
/** Prueba el plugin real y FXR1 con sustitutos del dibujo nativo, nunca simula rendimiento Quest. */
public class RendererTest {
 static int checks;static void check(boolean ok){if(!ok)throw new AssertionError("Contrato "+checks);checks++;}
 static String waitFor(String key)throws Exception{long end=System.nanoTime()+10000000000L;for(;;){String s=FolioPdfRenderer.getStatus(key);if(s!=null&&!s.equals("PENDING"))return s;if(System.nanoTime()>end)throw new AssertionError("Timeout");Thread.sleep(5);}}
 static ExecutorService worker()throws Exception{Field f=FolioPdfRenderer.class.getDeclaredField("WORK");f.setAccessible(true);return (ExecutorService)f.get(null);}
 public static void main(String[] args)throws Exception{
  Path dir=Files.createTempDirectory("folio-render-");
  try{
   File pdf=dir.resolve("sparse.pdf").toFile();try(RandomAccessFile f=new RandomAccessFile(pdf,"rw")){f.setLength(8L<<30);}
   FolioPdfRenderer.requestPageCount(pdf.getPath(),"count");check(waitFor("count").equals("OK:8"));check(android.os.ParcelFileDescriptor.lastSize==(8L<<30));FolioPdfRenderer.clearStatus("count");
   File page=dir.resolve("page.raw").toFile();FolioPdfRenderer.requestRenderPage(pdf.getPath(),1,64,page.getPath(),"first");
   check(waitFor("first").startsWith("OK:"));check(!FolioPdfRenderer.wasCached("first"));check(FolioRawCache.size(page).equals("64x90"));
   byte[] bytes=Files.readAllBytes(page.toPath());check(bytes[16]==0x11 && bytes[17]==0x22 && (bytes[18]&255)==0x33+89 && (bytes[19]&255)==255);FolioPdfRenderer.clearStatus("first");
   worker().submit(()->{}).get();
   FolioPdfRenderer.requestRenderPage(pdf.getPath(),3,64,page.getPath(),"cached");check(waitFor("cached").startsWith("OK:"));check(FolioPdfRenderer.wasCached("cached"));FolioPdfRenderer.clearStatus("cached");check(!FolioPdfRenderer.wasCached("cached"));
   worker().submit(()->{}).get(); android.graphics.Bitmap.maxWidth=256;
   FolioPdfRenderer.requestRenderPage(pdf.getPath(),0,2048,page.getPath(),"oom");check(waitFor("oom").startsWith("OK:256x"));FolioPdfRenderer.clearStatus("oom");
   FolioPdfRenderer.requestRenderPage(pdf.getPath(),4,64,page.getPath(),"tall");check(waitFor("tall").startsWith("OK:"));check(FolioRawCache.size(page).equals("1x8192"));FolioPdfRenderer.clearStatus("tall");
   CountDownLatch release=new CountDownLatch(1);worker().submit(()->{try{release.await();}catch(Exception e){}});
   File cancelled=dir.resolve("cancelled.raw").toFile();FolioPdfRenderer.requestRenderPage(pdf.getPath(),0,64,cancelled.getPath(),"cancel");FolioPdfRenderer.clearStatus("cancel");release.countDown();worker().submit(()->{}).get();check(!cancelled.exists() && FolioPdfRenderer.getStatus("cancel")==null);
   FolioPdfRenderer.requestRenderPage(pdf.getPath(),999,64,page.getPath(),"bad");check(waitFor("bad").startsWith("ERROR:"));FolioPdfRenderer.clearStatus("bad");
   FolioPdfRenderer.close();worker().submit(()->{}).get();check(FolioPdfRenderer.getStatus("first")==null);
   System.out.println("OK: "+checks+" comprobaciones del plugin PDF real con contratos Android simulados");
  }finally{worker().shutdownNow();try(java.util.stream.Stream<Path> paths=Files.walk(dir)){paths.sorted(java.util.Comparator.reverseOrder()).forEach(p->{try{Files.delete(p);}catch(Exception e){throw new RuntimeException(e);}});}}
 }
}
