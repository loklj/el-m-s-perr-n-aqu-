# Pruebas de archivos gigantes y rendimiento

Corpus generado temporalmente, propio. No se incluyen PDF grandes ni compresores en Git.

```bash
export ANDROID_JAR=/ruta/android-34/android.jar
export JAVA=/ruta/jdk/bin/java JAVAC=/ruta/jdk/bin/javac
sh tests/rendimiento/validar.sh
# RAR es opcional, externo, con licencia/evaluación vigente del usuario:
RAR_COMMAND=/ruta/a/rar python3 tests/rendimiento/generador_test.py
```

- `LargeFilesTest`: 87 comprobaciones; JVM con heap de 64 MiB. Copia realmente archivos dispersos en los límites 2 GiB ±1, 4 GiB ±1 y 8 GiB. Comprueba el marcador del final, longitudes, progreso, truncado, cancelación, SAF sin longitud, agotamiento y reservas concurrentes, dimensiones extremas, presupuesto compartido, LRU/corrupción FXR1 y percentiles. Son unos 26 GiB de lectura lógica; puede tardar uno o varios minutos, sin requerir ese espacio físico.
- `RendererTest`: 15 comprobaciones. Compila todos los plugins contra `android.jar` 34 primero; después sustituye solo las clases Android de dibujo para ejecutar el plugin PDF real en JVM. Descriptor de 8 GiB, bytes RGBA/orden de filas, precarga, falta de memoria, página extrema y cancelación. **No valida PdfRenderer nativo, ni su velocidad o memoria.**
- `MetricsTest`: 8 comprobaciones de CSV real, percentiles, apagado del diagnóstico, ausencia de rutas, rotación y copia de muestras sin bloquear la escritura.
- `generador_test.py`: 8 pruebas con RAR disponible; sin él una se omite explícitamente. Semillas, EPUB por tramos, PDF de 100.001 páginas, PDF escaneado pequeño, TXT UTF-16, CBZ, límite efectivo, CLI inválida y RAR5 sólido real con CRC. RAR 7.23 se ejecutó en evaluación, fuera del repo/APK; salida y binario no se distribuyen.
- `medicion-pc.json`: evidencia independiente de un PDF de imágenes de 4.423.496.009 bytes y 208 páginas. Archivo eliminado tras validar inicio/medio/final. Los tiempos corresponden a PyMuPDF en Linux, no al visor.

Regresión completa: documentos (102 + 8), formatos `--large` (1240 + 4 + 10 + 13), lectura (77), puente C# (28), biblioteca/menú C# (15), fuentes/CSS Chromium (26). Sumando rendimiento (87 + 15 + 8), generador (8) y búsqueda (22 JVM + 11 JavaScript), **1674 comprobaciones/pruebas aprobadas**. C# usa contratos simulados: no equivale a compilar Unity ni ejecutar IL2CPP.

Los sustitutos Android están solo bajo `tests/`, nunca bajo `Assets`. La prueba de copia dispersa escribe huecos únicamente en su destino de prueba; la copia de producción usa `FileOutputStream` normal.

Rebase del 13 de noviembre sobre `2329aff`: se conservan búsqueda, cursor y cancelación. La batería `tests/busqueda/validar.sh` se ejecutó con Java y Node; el puente C# comprueba respuesta de búsqueda y que cancelar impida la devolución tardía.
