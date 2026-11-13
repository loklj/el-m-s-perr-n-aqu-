package com.loklj.folioxr;

import java.io.*;
import java.nio.*;
import java.security.MessageDigest;
import java.util.*;

/** LRU en disco exclusiva para FXR1, persistente, con cuota de bytes y validacion completa. */
final class FolioRawCache {
    static final long LIMIT=192L<<20;
    final File folder;
    FolioRawCache(File parent)throws IOException {folder=new File(parent,"folio-fxr1");if(!folder.isDirectory()&&!folder.mkdirs())throw new IOException("Cache no disponible");trim(0);}
    File file(String path,int page,int width)throws Exception {
        File source=new File(path);String key=source.getCanonicalPath()+"|"+source.length()+"|"+source.lastModified()+"|"+page+"|"+width;
        byte[] digest=MessageDigest.getInstance("SHA-256").digest(key.getBytes("UTF-8"));StringBuilder name=new StringBuilder();
        for(byte b:digest)name.append(String.format(Locale.ROOT,"%02x",b&255));return new File(folder,name+".raw");
    }
    static String size(File file)throws IOException {
        byte[] head=new byte[16];try(DataInputStream in=new DataInputStream(new FileInputStream(file))){in.readFully(head);}
        ByteBuffer b=ByteBuffer.wrap(head).order(ByteOrder.LITTLE_ENDIAN);int w=b.getInt(4),h=b.getInt(8);
        if(head[0]!='F'||head[1]!='X'||head[2]!='R'||head[3]!='1'||w<1||h<1||w>8192||h>8192||
           (long)w*h>8000000L||file.length()!=16L+4L*w*h)throw new IOException("FXR1 invalido");
        return w+"x"+h;
    }
    boolean take(File file,File out)throws IOException {
        if(!file.exists())return false;
        try{size(file);}catch(IOException e){file.delete();return false;}
        // Mover evita una segunda copia de decenas de MB en la ruta critica.
        if(out.exists()&&!out.delete())return false;
        return file.renameTo(out);
    }
    void trim(long incoming)throws IOException {
        File[] files=folder.listFiles();if(files==null)throw new IOException("No se pudo enumerar cache");
        Arrays.sort(files,Comparator.comparingLong(File::lastModified));long total=0;
        for(File f:files){if(f.getName().endsWith(".tmp"))f.delete();else total+=f.length();}
        for(File f:files){if(total+incoming<=LIMIT && FolioLargeFiles.fits(FolioLargeFiles.unreserved(folder.getUsableSpace()),incoming))break;
            long n=f.length();if(f.delete())total-=n;
        }
        if(total+incoming>LIMIT || !FolioLargeFiles.fits(FolioLargeFiles.unreserved(folder.getUsableSpace()),incoming))throw new IOException("Cuota de cache o espacio insuficiente");
    }
}
