package com.loklj.folioxr;
import java.io.*;

/** Contrato independiente de WebView: capitulos UTF-8, recursos locales y proteccion. */
public interface FolioHtmlSource extends Closeable {
    int chapterCount();
    String chapterName(int index);
    byte[] chapter(int index) throws IOException;
    byte[] resource(String name, int limit) throws IOException;
    boolean isFont(String name);
    default java.util.List<FolioContents.Entry> contents() throws IOException { return java.util.Collections.emptyList(); }
    void checkDrm() throws IOException;
    static FolioHtmlSource open(String path, String encoding) throws Exception {
        return open(path, encoding, () -> false);
    }
    static FolioHtmlSource open(String path, String encoding, java.util.function.BooleanSupplier cancelled) throws Exception {
        String extension = path.toLowerCase(java.util.Locale.ROOT);
        if (extension.endsWith(".txt")) return new FolioTxtSource(path, encoding, cancelled);
        if (extension.endsWith(".mobi") || extension.endsWith(".azw") || extension.endsWith(".azw3")) return new FolioMobiSource(path, cancelled);
        if (extension.endsWith(".docx") || extension.endsWith(".odt") || extension.endsWith(".rtf")) return new FolioDocumentSource(path, cancelled);
        if (extension.endsWith(".epub")) return new FolioEpubContainer(path);
        throw new IOException(FolioDocumentDiagnostics.unsupported(extension));
    }
}
