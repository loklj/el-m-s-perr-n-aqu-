package com.loklj.folioxr;
import android.content.Context;
/** Entrada TXT/MOBI/DOCX/ODT/RTF al mismo paginador; mantiene el protocolo JNI existente. */
public final class FolioTextRenderer {
    public static void init(Context context,float scale) { FolioEpubRenderer.init(context,scale); }
    public static void setTxtEncoding(String encoding) { FolioEpubRenderer.setTxtEncoding(encoding); }
    public static void requestPageCount(String path,String key) { FolioEpubRenderer.requestPageCount(path,key); }
    public static void requestRenderPage(String path,int index,int width,String output,String key) { FolioEpubRenderer.requestRenderPage(path,index,width,output,key); }
    public static void setLayout(int font,float line,int margin) { FolioEpubRenderer.setLayout(font,line,margin); }
    public static void requestContents(String path,String key) { FolioEpubRenderer.requestContents(path,key); }
    public static void requestSearch(String path,String query,String from,int limit,String key) { FolioEpubRenderer.requestSearch(path,query,from,limit,key); }
    public static String getStatus(String key) { return FolioEpubRenderer.getStatus(key); }
    public static void clearStatus(String key) { FolioEpubRenderer.clearStatus(key); }
    public static void close() { FolioEpubRenderer.close(); }
}
