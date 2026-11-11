package com.loklj.folioxr;
import java.nio.file.*;
import org.json.*;
public final class OpdsTest {
    static int checks;
    static void check(boolean ok, String what) { if (!ok) throw new AssertionError(what); checks++; }
    public static void main(String[] args) throws Exception {
        JSONObject atom = FolioOpds.parse(Files.readAllBytes(Paths.get(args[0], "calibre-web-raiz.xml")), "http://192.168.1.10:8083/opds");
        check("Calibre-Web".equals(atom.getString("title")), "titulo atom");
        check("http://192.168.1.10:8083/opds/new?offset=30".equals(atom.getString("next")), "next atom");
        JSONArray e = atom.getJSONArray("entries");
        check(e.length() == 3, "entradas atom " + e.length());
        check(e.getJSONObject(0).getString("nav").endsWith("/opds/new"), "nav atom");
        check(e.getJSONObject(1).getJSONArray("books").length() == 2, "dos adquisiciones");
        check("Miguel de Cervantes".equals(e.getJSONObject(1).getString("author")), "autor");
        check(e.getJSONObject(2).getJSONArray("books").getJSONObject(0).getBoolean("drm"), "drm indirecto");
        JSONObject json = FolioOpds.parse(Files.readAllBytes(Paths.get(args[0], "komga-opds2.json")), "http://nas:25600/opds/v2/catalog");
        check("Komga".equals(json.getString("title")), "titulo json");
        check(json.getString("next").equals("http://nas:25600/opds/v2/series?page=1"), "next json");
        JSONArray j = json.getJSONArray("entries");
        check(j.length() == 3, "entradas json " + j.length());
        check(j.getJSONObject(0).getString("nav").equals("http://nas:25600/opds/v2/series"), "nav json");
        check("Autora".equals(j.getJSONObject(1).getString("author")), "autor json");
        check(!j.getJSONObject(1).getJSONArray("books").getJSONObject(0).getBoolean("drm"), "cbz legible");
        check(j.getJSONObject(2).getJSONArray("books").getJSONObject(0).getBoolean("drm"), "lcp es drm");
        System.out.println("OK: " + checks + " comprobaciones OPDS");
    }
}
