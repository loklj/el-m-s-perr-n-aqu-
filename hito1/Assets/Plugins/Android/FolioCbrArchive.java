package com.loklj.folioxr;
import java.io.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.function.BooleanSupplier;
import java.util.zip.*;

/** Adaptador de Junrar 7.5.5. Solo extraccion; no crear un compresor compatible con RAR.
 * Dependencias JAR locales con licencia UnRAR y MIT, sin resolucion Gradle.
 */
public final class FolioCbrArchive {
    static String kind(File file) throws IOException {
        byte[] b=new byte[8]; int n; try(InputStream in=new FileInputStream(file)){n=in.read(b);}
        if(n>=4 && b[0]=='P' && b[1]=='K' && b[2]==3 && b[3]==4)return "zip";
        if(n>=7 && b[0]=='R' && b[1]=='a' && b[2]=='r' && b[3]=='!' && b[4]==26 && b[5]==7) {
            if(b[6]==0)return "rar4";
            if(n>=8 && b[6]==1 && b[7]==0)return "rar5";
        }
        throw new IOException("No es un comic ZIP/RAR");
    }
    static File prepare(File source,BooleanSupplier cancelled) throws Exception {
        String kind=kind(source);
        if(kind.equals("zip"))return source;
        if(kind.equals("rar5"))throw new IOException("CBR (RAR5) a\u00fan no compatible: convi\u00e9rtelo a CBZ");
        File target=File.createTempFile("folio-cbr-", ".cbz"); boolean success=false;
        Object archive=null;
        try {
            Class<?> archiveType=Class.forName("com.github.junrar.Archive");
            Class<?> headerType=Class.forName("com.github.junrar.rarfile.FileHeader");
            archive=archiveType.getConstructor(File.class).newInstance(source);
            if((Boolean)archiveType.getMethod("isPasswordProtected").invoke(archive))throw new IOException("CBR protegido con contrase\u00f1a: no compatible");
            List<?> headers=(List<?>)archiveType.getMethod("getFileHeaders").invoke(archive);
            if(headers.size()>10000)throw new IOException("Demasiadas entradas RAR");
            Method extract=archiveType.getMethod("extractFile",headerType,OutputStream.class);
            long total=0; int images=0; Set<String> names=new HashSet<>();
            try(ZipOutputStream zip=new ZipOutputStream(new BufferedOutputStream(new FileOutputStream(target)))) {
                for(Object header:headers) {
                    if(cancelled.getAsBoolean())throw new InterruptedIOException("Cancelado");
                    if((Boolean)headerType.getMethod("isEncrypted").invoke(header))throw new IOException("CBR cifrado: no compatible");
                    if((Boolean)headerType.getMethod("isSplitBefore").invoke(header) || (Boolean)headerType.getMethod("isSplitAfter").invoke(header))throw new IOException("RAR multipartes: conviertelo a CBZ");
                    if((Boolean)headerType.getMethod("isDirectory").invoke(header))continue;
                    String name=((String)headerType.getMethod("getFileName").invoke(header)).replace('\\','/');
                    long size=(Long)headerType.getMethod("getFullUnpackSize").invoke(header);
                    if(size<0 || size>32*1024*1024 || (total+=size)>512L*1024*1024)throw new IOException("CBR excede cuota de extraccion (32 MiB/entrada, 512 MiB/archivo)");
                    boolean image=name.toLowerCase(Locale.ROOT).matches(".*\\.(png|jpe?g|webp|gif|bmp)$");
                    // Nombres sinteticos: ninguna ruta del archivo llega al sistema de archivos.
                    if(image){
                        String outputName=name.substring(name.lastIndexOf('/')+1);
                        if(!names.add(outputName)) { outputName=images+"-"+outputName; names.add(outputName); }
                        images++; zip.putNextEntry(new ZipEntry(outputName));
                    }
                    final boolean keep=image; final long expected=size;
                    OutputStream bounded=new OutputStream(){
                        long count;
                        public void write(int b)throws IOException{byte[] one={(byte)b};write(one,0,1);}
                        public void write(byte[] b,int off,int len)throws IOException{
                            if(cancelled.getAsBoolean())throw new InterruptedIOException("Cancelado");
                            count+=len; if(count>expected)throw new IOException("RAR excede tamano declarado");
                            if(keep)zip.write(b,off,len);
                        }
                    };
                    // Incluso entradas no visuales se consumen para conservar el diccionario solid.
                    extract.invoke(archive,header,bounded);
                    if(image)zip.closeEntry();
                }
            }
            if(images==0)throw new IOException("CBR sin imagenes"); success=true; return target;
        } catch(InvocationTargetException e) { throw new IOException("No se pudo extraer RAR4",e.getCause()); }
        finally {
            if(archive instanceof Closeable)((Closeable)archive).close();
            if(!success)target.delete();
        }
    }
}
