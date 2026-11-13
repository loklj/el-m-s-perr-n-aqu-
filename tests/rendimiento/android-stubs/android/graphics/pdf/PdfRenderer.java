package android.graphics.pdf;
import android.os.ParcelFileDescriptor;import android.graphics.*;
public class PdfRenderer implements AutoCloseable { public PdfRenderer(ParcelFileDescriptor fd){}public int getPageCount(){return 8;}
 public Page openPage(int n){return new Page(n);}public void close(){}
 public static class Page implements AutoCloseable {public static final int RENDER_MODE_FOR_DISPLAY=1;final int n;Page(int n){this.n=n;}public int getWidth(){return n==4?1:420;}public int getHeight(){return n==4?Integer.MAX_VALUE:595;}public void render(Bitmap b,Rect clip,Matrix m,int mode){}public void close(){} }
}
