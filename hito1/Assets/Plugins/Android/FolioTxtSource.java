package com.loklj.folioxr;
import java.io.*;
import java.nio.*;
import java.nio.charset.*;
import java.util.*;

/** Indice de tramos normalizados en disco; memoria independiente del largo total. */
public final class FolioTxtSource implements FolioHtmlSource {
    private final File cache;
    private RandomAccessFile data;
    private final List<Long> offsets = new ArrayList<>();
    public final String encoding;
    FolioTxtSource(String path, String preference) throws IOException { this(path, preference, () -> false); }
    FolioTxtSource(String path, String preference, java.util.function.BooleanSupplier cancelled) throws IOException {
        byte[] sample = new byte[65536]; int size;
        try (InputStream input = new FileInputStream(path)) { size = input.read(sample); }
        if (size < 0) size = 0;
        encoding = detect(sample, size, preference);
        cache = File.createTempFile("folio-txt-", ".cache");
        try {
            data = new RandomAccessFile(cache, "rw"); offsets.add(0L);
            CharsetDecoder decoder = Charset.forName(encoding).newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT);
            try (Reader reader = new BufferedReader(new InputStreamReader(new FileInputStream(path), decoder))) {
                StringBuilder chunk = new StringBuilder(210000); int c, bytes = 0; boolean first = true;
                while ((c = reader.read()) != -1) {
                    if ((chunk.length() & 8191) == 0 && (cancelled.getAsBoolean() || Thread.currentThread().isInterrupted())) throw new InterruptedIOException();
                    if (first && c == 0xfeff) { first = false; continue; } first = false;
                    chunk.append((char)c); bytes += c < 128 ? 1 : c < 2048 ? 2 : 3;
                    if ((bytes >= 200000 && c == '\n') || (bytes >= 262144 && !Character.isHighSurrogate((char)c))) {
                        write(chunk); bytes = 0;
                    }
                }
                if (chunk.length() > 0 || offsets.size() == 1) write(chunk);
            }
        } catch (IOException | RuntimeException e) { close(); throw e; }
    }
    private void write(StringBuilder text) throws IOException {
        if (offsets.size() >= 100000) throw new IOException("Demasiados capitulos TXT");
        data.write(text.toString().getBytes(StandardCharsets.UTF_8)); offsets.add(data.getFilePointer()); text.setLength(0);
    }
    static String detect(byte[] bytes, int n, String preference) {
        if ("UTF-8".equals(preference) || "Windows-1252".equals(preference)) return preference;
        if (n >= 2 && (bytes[0]&255)==255 && (bytes[1]&255)==254) return "UTF-16LE";
        if (n >= 2 && (bytes[0]&255)==254 && (bytes[1]&255)==255) return "UTF-16BE";
        int even = 0, odd = 0;
        for (int i=0;i<n;i++) if(bytes[i]==0) { if((i&1)==0) even++; else odd++; }
        if (n>=8 && odd > n/6 && even < n/20) return "UTF-16LE";
        if (n>=8 && even > n/6 && odd < n/20) return "UTF-16BE";
        CharsetDecoder utf8 = StandardCharsets.UTF_8.newDecoder();
        CoderResult result = utf8.decode(ByteBuffer.wrap(bytes,0,n), CharBuffer.allocate(n+1), false);
        if (!result.isError()) return "UTF-8";
        for (int i=0;i<n;i++) if((bytes[i]&255)>=128 && (bytes[i]&255)<160) return "Windows-1252";
        return "ISO-8859-1";
    }
    public int chapterCount() { return offsets.size()-1; }
    public String chapterName(int index) { return "texto/"+index+".html"; }
    public synchronized byte[] chapter(int index) throws IOException {
        if(index<0 || index>=chapterCount()) throw new IOException("Capitulo TXT fuera de rango");
        byte[] bytes = new byte[(int)(offsets.get(index+1)-offsets.get(index))]; data.seek(offsets.get(index)); data.readFully(bytes);
        String text = new String(bytes,StandardCharsets.UTF_8).replace("&","&amp;").replace("<","&lt;").replace(">","&gt;");
        return ("<div style='white-space:pre-wrap'>"+text+"</div>").getBytes(StandardCharsets.UTF_8);
    }
    public byte[] resource(String name,int limit) throws IOException { throw new FileNotFoundException(); }
    public boolean isFont(String name) { return false; }
    public void checkDrm() { }
    public void close() throws IOException { try { if(data!=null)data.close(); } finally { cache.delete(); } }
}
