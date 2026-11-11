# Encargo 10: pruebas reproducibles

Desde la raíz, indicar API 34 y un JDK (se usó OpenJDK 21.0.2 con `--release 8`):

```bash
export ANDROID_JAR=/ruta/android-34/android.jar
sh tests/formatos/validar.sh
# Incluye un TXT temporal de 240 MB, heap JVM máximo de 64 MiB:
sh tests/formatos/validar.sh --large
# Opcional: contratos JNI y lectura FXR1 del puente C#, con stubs:
export DOTNET_ROOT=/ruta/dotnet-con-sdk-8
python3 tests/formatos/validar-puente.py
```

`JAVAC` y `JAVA` permiten seleccionar ejecutables fuera del PATH. El script compila **todos** los plugins Folio Java contra android.jar, sin dependencias externas de compilación. Junrar/SLF4J solo se agregan al classpath al ejecutar la prueba RAR4; se entregan como JAR locales en la app. SLF4J avisa que usa NOP porque no se incluye implementación de logging.

Resultados del 11 de noviembre de 2026, después del rebase sobre `7fcf14a`:

| Grupo | Comprobaciones aprobadas |
| --- | ---: |
| Formatos, con `--large` | 1240 |
| PalmDOC/HUFF/CDIC y colas de registros | 4 |
| Detector real `FolioLibrary.detectFormat` | 10 |
| Regresión del contenedor EPUB | 13 |
| Puente C# con stubs, incluida lectura FXR1 | 15 |
| Total con prueba grande y puente | **1282** |

Los 1240 incluyen comprobaciones de cada tramo del TXT grande; no son 1240 archivos diferentes. El archivo grande se crea fuera del repositorio y se borra al terminar. La prueba habitual pequeña tiene menos tramos y, por tanto, menos comprobaciones.

Los fixtures de `tests/fixtures/formatos/` son originales y regenerables con `python3 tests/formatos/generar.py`. RAR4 usa almacenamiento sin compresión; RAR5 es solo una firma para comprobar rechazo. El fixture DRM solo activa el indicador de cifrado: no contiene texto realmente cifrado ni descifrador. La prueba KF8 contiene un esqueleto y un fragmento; no acredita todos los índices reales.

Las pruebas JVM no usan WebView ni Android real. Las pruebas C# usan el archivo real PdfPageRenderer, pero sustituyen tipos Unity/JNI: no acreditan compilación de Unity, texturas GPU, rendimiento ni fidelidad visual. La revisión del rebase también comprobó que `LoadRaw` permaneciera idéntico al de la base y que no hubiera cambios propios en PDF Java, Tools, SphereMenuView o WifiServer. Las pruebas en Quest están en el Paso 10 de la guía.
