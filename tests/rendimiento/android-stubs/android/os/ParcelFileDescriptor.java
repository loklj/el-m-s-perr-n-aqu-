package android.os;
import java.io.*;
public class ParcelFileDescriptor implements AutoCloseable {
 public static final int MODE_READ_ONLY=1; public static long lastSize;final File file;
 private ParcelFileDescriptor(File f){file=f;lastSize=f.length();}public static ParcelFileDescriptor open(File f,int mode){return new ParcelFileDescriptor(f);}
 public long getStatSize(){return file.length();}public void close(){}
}
