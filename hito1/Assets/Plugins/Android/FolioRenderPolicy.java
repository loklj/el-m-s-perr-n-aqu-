package com.loklj.folioxr;

/** Presupuesto global de capturas, independiente del numero de renderizadores. */
final class FolioRenderPolicy {
    static final long PSS_BUDGET=1536L<<20, GPU_BUDGET=256L<<20;
    private static long held;
    static int pressure(long pss,long gpu,long javaFree){
        if(pss>=PSS_BUDGET || gpu>=GPU_BUDGET || javaFree<(48L<<20))return 2;
        if(pss>=PSS_BUDGET*3/4 || gpu>=GPU_BUDGET*3/4 || javaFree<(96L<<20))return 1;
        return 0;
    }
    static long pixels(int pressure){return pressure>=2?1000000L:pressure==1?3000000L:8000000L;}
    static int[] dimensions(int requested,int pageWidth,int pageHeight,long limit){
        double ratio=Math.max(1,pageHeight)/(double)Math.max(1,pageWidth);
        double width=Math.max(64,Math.min(4096,requested)),height=width*ratio;
        double scale=Math.min(1,Math.min(Math.sqrt(limit/(width*height)),8192.0/Math.max(width,height)));
        int w=Math.max(1,(int)(width*scale)),h=Math.max(1,(int)(height*scale));
        return new int[]{w,h};
    }
    // Reserva no bloqueante: nunca esperar en el hilo de UI del WebView.
    static synchronized boolean acquire(long bytes){if(bytes<0 || bytes>(128L<<20)-held)return false;held+=bytes;return true;}
    static synchronized void release(long bytes){held=Math.max(0,held-bytes);}
}
