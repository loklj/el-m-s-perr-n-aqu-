package com.loklj.folioxr;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.zip.*;
public final class ContainerTest {
    private static int checks;
    private static void check(boolean ok) { if (!ok) throw new AssertionError(); checks++; }
    private static File fixture(String protection) throws Exception {
        File file = File.createTempFile("folio-test", ".epub"); file.deleteOnExit();
        try (ZipOutputStream zip = new ZipOutputStream(new FileOutputStream(file))) {
            add(zip,"mimetype","application/epub+zip");
            add(zip,"META-INF/container.xml","<container><rootfiles><rootfile full-path='OPS/book.opf'/></rootfiles></container>");
            add(zip,"OPS/book.opf","<package><manifest><item id='a' href='a.xhtml'/><item id='n' href='nav.xhtml' properties='nav'/></manifest><spine><itemref idref='a'/></spine></package>");
            add(zip,"OPS/a.xhtml","<html><body>Hola</body></html>");
            add(zip,"OPS/nav.xhtml","<html xmlns:epub='http://www.idpf.org/2007/ops'><nav epub:type='toc'><ol><li><a href='a.xhtml#inicio'>Inicio</a></li></ol></nav></html>");
            if ("RIGHTS".equals(protection)) add(zip,"META-INF/rights.xml","<rights/>");
            else if (protection != null) add(zip,"META-INF/encryption.xml",protection);
        } return file;
    }
    private static void add(ZipOutputStream zip,String name,String data) throws Exception {
        zip.putNextEntry(new ZipEntry(name)); zip.write(data.getBytes(StandardCharsets.UTF_8)); zip.closeEntry();
    }
    public static void main(String[] args) throws Exception {
        try (FolioEpubContainer c = new FolioEpubContainer(fixture(null).getPath())) {
            check(c.spine.size()==1); check(c.spine.get(0).equals("OPS/a.xhtml"));
            check(c.navigation.containsKey("OPS/a.xhtml"));
            try { c.read("OPS/a.xhtml",2); throw new AssertionError(); } catch(IOException expected) { checks++; }
        }
        try (FolioEpubContainer c = new FolioEpubContainer(fixture("<encryption><EncryptionMethod Algorithm='http://www.idpf.org/2008/embedding'/></encryption>").getPath())) { check(c.spine.size()==1); }
        try { new FolioEpubContainer(fixture("<encryption><EncryptionMethod Algorithm='http://www.w3.org/2001/04/xmlenc#aes128-cbc'/></encryption>").getPath()); throw new AssertionError(); }
        catch(FolioEpubContainer.DrmException expected) { check(expected.getMessage().equals("Este libro tiene DRM y no se puede abrir")); }
        try { new FolioEpubContainer(fixture("RIGHTS").getPath()); throw new AssertionError(); }
        catch(FolioEpubContainer.DrmException expected) { checks++; }
        try { new FolioEpubContainer(fixture("<!DOCTYPE encryption [<!ENTITY x 'x'>]><encryption>&x;</encryption>").getPath()); throw new AssertionError(); }
        catch(IOException expected) { checks++; }
        check(FolioEpubContainer.resolve("OPS/book.opf","../a.xhtml#x").equals("a.xhtml"));
        for (String target : new String[]{"file:///etc/passwd","https://example.com/a","%2e%2e/secret"}) {
            try { FolioEpubContainer.resolve("",target); throw new AssertionError(target); } catch(IOException expected) { checks++; }
        }
        if(args.length>0) try(FolioEpubContainer c=new FolioEpubContainer(args[0])) { check(!c.spine.isEmpty()); }
        System.out.println("OK: " + checks + " comprobaciones del contenedor");
    }
}
