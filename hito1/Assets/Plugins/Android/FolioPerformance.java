package com.loklj.folioxr;

import android.os.Debug;
import android.util.Log;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;

/** Muestreo fuera de Unity; CSV acotado, sin rutas, titulos ni contenido de libros. */
public final class FolioPerformance {
    private static final ScheduledExecutorService WORK=Executors.newSingleThreadScheduledExecutor();
    private static final Map<String,Samples> METRICS=new LinkedHashMap<>();
    private static volatile boolean enabled;
    private static volatile long graphics;
    private static volatile int pressure;
    private static File csv;
    private static boolean started;
    private static long lastSummary;
    static final class Samples {
        final double[] values=new double[1024]; int count, cursor;long total;
        void add(double n){if(!Double.isFinite(n)||n<0)return;values[cursor++%values.length]=n;cursor%=values.length;count=Math.min(count+1,values.length);total++;}
        Samples snapshot(){Samples s=new Samples();System.arraycopy(values,0,s.values,0,count);s.count=count;s.cursor=cursor;s.total=total;return s;}
        double percentile(double q){if(count==0)return 0;double[] copy=Arrays.copyOf(values,count);Arrays.sort(copy);return copy[Math.max(0,(int)Math.ceil(count*q)-1)];}
    }
    public static synchronized void configure(String directory,boolean on){
        enabled=on;csv=new File(directory,"rendimiento.csv");
        if(!started){started=true;WORK.scheduleWithFixedDelay(FolioPerformance::sample,0,2,TimeUnit.SECONDS);}
        if(on)Log.i("FolioXR","[FolioXR] Modo diagnostico: CSV en la carpeta de la app; ventana de 1024 muestras");
    }
    public static void setGraphicsBytes(long bytes){graphics=Math.max(0,bytes);}
    public static int pressure(){return pressure;}
    static void memoryFailure(){pressure=2;}
    public static void record(String metric,double value){
        if(!enabled)return;
        synchronized(METRICS){
            if(!metric.matches("[a-z0-9_]{1,48}"))return;
            Samples s=METRICS.get(metric);if(s==null){if(METRICS.size()>=48)return;METRICS.put(metric,s=new Samples());}s.add(value);
        }
    }
    public static void frames(float[] milliseconds){
        if(!enabled)return;
        synchronized(METRICS){
            Samples s=METRICS.get("frame_ms");
            if(s==null){if(METRICS.size()>=48)return;METRICS.put("frame_ms",s=new Samples());}
            for(float v:milliseconds)s.add(v);
        }
    }
    private static void sample(){
        try{
            Debug.MemoryInfo info=new Debug.MemoryInfo();Debug.getMemoryInfo(info);
            long pss=info.getTotalPss()*1024L;Runtime r=Runtime.getRuntime();
            int next=FolioRenderPolicy.pressure(pss,graphics,r.maxMemory()-r.totalMemory()+r.freeMemory());
            if(next!=pressure)Log.i("FolioXR","[FolioXR] Presupuesto de lectura: presion="+next);
            pressure=next; record("memory_pressure",pressure);
            record("pss_bytes",pss);record("graphics_estimated_bytes",graphics);
            if(enabled && System.nanoTime()-lastSummary>10000000000L){lastSummary=System.nanoTime();export();}
        }catch(Throwable e){Log.w("FolioXR","[FolioXR] No se pudo muestrear rendimiento: "+e.getClass().getSimpleName());}
    }
    public static void flush(){WORK.execute(FolioPerformance::export);}
    private static void export(){
        File target=csv;if(target==null)return;
        try{
            Map<String,Samples> snapshot=new LinkedHashMap<>();
            synchronized(METRICS){for(Map.Entry<String,Samples> e:METRICS.entrySet())snapshot.put(e.getKey(),e.getValue().snapshot());}
            if(target.length()>2L*1024*1024){File old=new File(target.getPath()+".previous");old.delete();if(!target.renameTo(old))return;}
            boolean header=!target.exists()||target.length()==0;
            try(PrintWriter out=new PrintWriter(new OutputStreamWriter(new FileOutputStream(target,true),"UTF-8"))){
                if(header)out.println("utc_ms,metric,unit,total,window,median,p95,p99");
                for(Map.Entry<String,Samples> e:snapshot.entrySet()){
                    Samples s=e.getValue();String metric=e.getKey(),unit=metric.endsWith("bytes")?"bytes":metric.equals("fps")?"fps":metric.equals("memory_pressure")?"level":"ms";
                    String row=String.format(Locale.ROOT,"%d,%s,%s,%d,%d,%.3f,%.3f,%.3f",System.currentTimeMillis(),metric,unit,s.total,s.count,s.percentile(.5),s.percentile(.95),s.percentile(.99));
                    out.println(row);Log.i("FolioXR","[FolioXR] rendimiento "+row);
                }
                if(out.checkError())throw new IOException("CSV incompleto");
            }
        }catch(IOException e){Log.w("FolioXR","[FolioXR] No se pudo exportar rendimiento");}
    }
}
