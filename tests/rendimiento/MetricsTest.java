package com.loklj.folioxr;
import java.io.*;import java.nio.file.*;import java.lang.reflect.*;import java.util.*;
/** Exportacion real, reloj/Log Android sustituidos; sin datos de libros. */
public class MetricsTest {
 static int checks;static void check(boolean ok){if(!ok)throw new AssertionError("CSV "+checks);checks++;}
 static void field(String name,Object value)throws Exception{Field f=FolioPerformance.class.getDeclaredField(name);f.setAccessible(true);f.set(null,value);}
 public static void main(String[] args)throws Exception{
  Path dir=Files.createTempDirectory("folio-csv-");File csv=dir.resolve("rendimiento.csv").toFile();
  try{
   field("csv",csv);field("enabled",true);
   for(int i=1;i<=100;i++)FolioPerformance.record("page_ms",i);
   FolioPerformance.frames(new float[]{10,12,14});FolioPerformance.record("/libros/privado.pdf",1);
   Method export=FolioPerformance.class.getDeclaredMethod("export");export.setAccessible(true);export.invoke(null);
   String text=new String(Files.readAllBytes(csv.toPath()),"UTF-8");
   check(text.startsWith("utc_ms,metric,unit,total,window,median,p95,p99\n"));
   check(text.contains(",page_ms,ms,100,100,50.000,95.000,99.000"));
   check(text.contains(",frame_ms,ms,3,3,12.000,14.000,14.000"));check(!text.contains("privado"));
   field("enabled",false);FolioPerformance.record("page_ms",999);export.invoke(null);
   text=new String(Files.readAllBytes(csv.toPath()),"UTF-8");check(!text.contains(",101,"));
   try(RandomAccessFile out=new RandomAccessFile(csv,"rw")){out.setLength((2L<<20)+1);}
   export.invoke(null);check(new File(csv.getPath()+".previous").length()==(2L<<20)+1);check(csv.length()<10000);
   FolioPerformance.Samples s=new FolioPerformance.Samples();s.add(10);FolioPerformance.Samples snapshot=s.snapshot();s.add(20);check(snapshot.count==1 && snapshot.percentile(.99)==10);
   System.out.println("OK: "+checks+" comprobaciones de exportacion CSV y percentiles");
  }finally{try(java.util.stream.Stream<Path> paths=Files.walk(dir)){paths.sorted(Comparator.reverseOrder()).forEach(p->{try{Files.delete(p);}catch(Exception e){throw new RuntimeException(e);}});}}
 }
}
