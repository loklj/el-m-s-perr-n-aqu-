package android.graphics;
public class Bitmap { public enum Config{ARGB_8888} final int w,h; public static int maxWidth=8192;
 public Bitmap(int w,int h){this.w=w;this.h=h;}public static Bitmap createBitmap(int w,int h,Config c){if(w>maxWidth)throw new OutOfMemoryError("simulada");return new Bitmap(w,h);}
 public int getWidth(){return w;}public int getHeight(){return h;}public void eraseColor(int c){}public void recycle(){}
 public void getPixels(int[] row,int off,int stride,int x,int y,int w,int h){java.util.Arrays.fill(row,0xff112233+y);}
}
