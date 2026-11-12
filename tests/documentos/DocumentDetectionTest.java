package com.loklj.folioxr;
import java.io.*;
import java.lang.reflect.*;
import java.util.zip.*;
/** Ejecuta el detector real sin instanciar Android ni importar a SQLite. */
public final class DocumentDetectionTest {
    public static void main(String[] args)throws Exception{
        Method method=FolioLibrary.class.getDeclaredMethod("detectFormat",File.class,String.class);method.setAccessible(true);int checks=0;
        String[][] cases={{"propio.docx","renombrado.bin","docx"},{"propio.odt","renombrado.zip","odt"},{"propio.rtf","renombrado.txt","rtf"},{"ole-firma.doc","cifrado.docx","ole"},{"kfx-firma.kfx","libro.kfx","kfx"},{"desconocido.bin","archivo.bin","bin"}};
        for(String[] c:cases){Object result=method.invoke(null,new File("tests/fixtures/documentos",c[0]),c[1]);if(!c[2].equals(result))throw new AssertionError(c[0]+": "+result);checks++;}
        File temp=File.createTempFile("folio-ods-", ".zip");
        try{
            try(ZipOutputStream zip=new ZipOutputStream(new FileOutputStream(temp))){
                zip.putNextEntry(new ZipEntry("mimetype"));zip.write("application/vnd.oasis.opendocument.spreadsheet".getBytes("UTF-8"));zip.closeEntry();
                zip.putNextEntry(new ZipEntry("content.xml"));zip.write("<x/>".getBytes("UTF-8"));zip.closeEntry();
            }
            if(!"desconocido".equals(method.invoke(null,temp,"falso.odt")))throw new AssertionError("ODS confundido con ODT");checks++;
        }finally{temp.delete();}
        if(!FolioDocumentDiagnostics.unsupported("ole").contains("no se ha confirmado DRM"))throw new AssertionError("DRM supuesto");checks++;
        System.out.println("OK: "+checks+" comprobaciones de deteccion documental");
    }
}
