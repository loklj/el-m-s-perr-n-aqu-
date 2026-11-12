package com.loklj.folioxr;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Pruebas JVM de la busqueda: cursor, JSON, formatos y script seguro. Escribe el nucleo JS para node. */
public final class BookSearchTest {
    static int fails;
    static void ok(boolean c, String what) { if (!c) { fails++; System.out.println("FALLA: " + what); } }
    public static void main(String[] args) throws Exception {
        int[] c = FolioBookSearch.cursor("3:7"); ok(c[0] == 3 && c[1] == 7, "cursor 3:7");
        c = FolioBookSearch.cursor(""); ok(c[0] == 0 && c[1] == 0, "cursor vacio");
        c = FolioBookSearch.cursor("x:y"); ok(c[0] == 0 && c[1] == 0, "cursor invalido");
        c = FolioBookSearch.cursor("-2:-5"); ok(c[0] == 0 && c[1] == 0, "cursor negativo");
        ok(FolioBookSearch.clamp(0) == 1 && FolioBookSearch.clamp(999) == FolioBookSearch.MAX_LIMIT, "limite");
        for (String p : new String[]{"a.epub","B.TXT","c.mobi","d.azw3","e.docx","f.odt","g.rtf","h.azw"}) ok(FolioBookSearch.searchable(p), "buscable " + p);
        for (String p : new String[]{"a.pdf","b.cbz","c.cbr","d.zip",null}) ok(!FolioBookSearch.searchable(p), "no buscable " + p);
        String q = "\"});alert(1);//\\";
        String s = FolioBookSearch.script(q, 0, 10);
        ok(s.contains(FolioContents.quote(q)), "consulta citada");
        ok(!s.contains("\"});alert(1);//\\\","), "sin inyeccion");
        char[] big = new char[500]; Arrays.fill(big, 'a');
        ok(FolioBookSearch.clip(new String(big)).length() == FolioBookSearch.MAX_QUERY, "consulta recortada");
        List<FolioBookSearch.Hit> hits = new ArrayList<>();
        hits.add(new FolioBookSearch.Hit(4, "…el \"niño\" dijo…"));
        String json = FolioBookSearch.json(hits, "2:5", "");
        ok(json.equals("{\"hits\":[{\"page\":4,\"text\":\"…el \\\"niño\\\" dijo…\"}],\"next\":\"2:5\",\"message\":\"\"}"), "json " + json);
        if (args.length > 0) try (Writer w = new OutputStreamWriter(new FileOutputStream(args[0]), StandardCharsets.UTF_8)) {
            w.write(FolioBookSearch.CORE); w.write("\nmodule.exports={folioFind:folioFind,folioFold:folioFold};\n");
        }
        System.out.println(fails == 0 ? "busqueda JVM: OK" : "busqueda JVM: " + fails + " fallas");
        if (fails > 0) System.exit(1);
    }
}
