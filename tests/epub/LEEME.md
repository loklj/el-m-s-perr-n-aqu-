# Pruebas del contenedor EPUB

Sin Unity ni Android en ejecución. Desde la raíz, con JDK y `ANDROID_JAR` apuntando al `android.jar` de API 34:

```bash
mkdir -p work/epub-test
javac --release 8 -cp "$ANDROID_JAR" -d work/epub-test hito1/Assets/Plugins/Android/FolioEpub*.java tests/epub/ContainerTest.java
java -cp work/epub-test com.loklj.folioxr.ContainerTest tests/fixtures/libro-prueba.epub
```

Comprueba spine, nav, recursos acotados, rutas, rechazo DRM y aceptación de la ofuscación de fuentes sin descifrar. No acredita WebView, captura, Unity ni nitidez: esas comprobaciones están en el Paso 10 de la guía.
