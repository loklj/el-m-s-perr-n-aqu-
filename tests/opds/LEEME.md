# Pruebas del cliente OPDS

Sin Unity ni Android. `org.json` del `android.jar` es un esqueleto que falla al ejecutarse, así que se compila la implementación real desde su código fuente (`https://github.com/stleary/JSON-java`, dominio público), solo para la prueba:

```bash
git clone --depth 1 https://github.com/stleary/JSON-java /tmp/json-java
mkdir -p work/json work/opds
javac -nowarn -d work/json $(find /tmp/json-java/src/main/java -name '*.java')
javac -nowarn --release 8 -cp work/json:"$ANDROID_JAR" -d work/opds \
  hito1/Assets/Plugins/Android/FolioOpds.java hito1/Assets/Plugins/Android/FolioLibrary.java tests/opds/OpdsTest.java
java -cp work/opds:work/json:"$ANDROID_JAR" com.loklj.folioxr.OpdsTest tests/fixtures/opds
```

Comprueba OPDS 1.2 (Atom, como Calibre-Web) y OPDS 2.0 (JSON, como Komga): navegación, adquisiciones, autores, enlaces relativos, página siguiente y DRM (Adobe ADEPT indirecto y Readium LCP). No prueba la red, el Keystore ni la descarga: esas comprobaciones están en el Paso 10 de la guía.
