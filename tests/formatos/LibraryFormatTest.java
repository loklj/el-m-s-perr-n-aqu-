package com.loklj.folioxr;
import java.io.*;
import java.lang.reflect.*;
import java.util.zip.*;
/** Solo detectFormat: no instancia Android, no toca importacion ni SQLite. */
public final class LibraryFormatTest {
    static int checks;
    static Method detect;
    static void check(File file,String name,String expected)throws Exception{
        if(!expected.equals(detect.invoke(null,file,name)))throw new AssertionError(name);checks++;
    }
    public static void main(String[] args)throws Exception{
        detect=FolioLibrary.class.getDeclaredMethod("detectFormat",File.class,String.class);detect.setAccessible(true);
        File root=new File("tests/fixtures/formatos");
        check(new File(root,"propio.mobi"),"renombrado.bin","mobi");
        check(new File(root,"palmdoc.azw"),"libro.azw","azw");
        check(new File(root,"kf8.azw3"),"libro.azw3","azw3");
        check(new File(root,"zip-disfrazado.cbr"),"comic.cbr","cbr");
        check(new File(root,"rar4-almacenado.cbr"),"comic.bin","cbr");
        check(new File(root,"rar5-firma.cbr"),"comic.bin","cbr");
        check(new File("tests/fixtures/pdf-prueba.pdf"),"documento.bin","pdf");
        check(new File("tests/fixtures/texto-utf8.txt"),"texto.txt","txt");
        check(new File("tests/fixtures/libro-prueba.epub"),"publicacion.zip","epub");
        File temp=File.createTempFile("folio-container-", ".zip");
        try{
            try(ZipOutputStream out=new ZipOutputStream(new FileOutputStream(temp))){out.putNextEntry(new ZipEntry("META-INF/container.xml"));out.write("<container/>".getBytes("UTF-8"));out.closeEntry();}
            check(temp,"sin-mimetype.bin","epub");
        }finally{temp.delete();}
        System.out.println("OK: "+checks+" comprobaciones del detector de biblioteca");
    }
}
