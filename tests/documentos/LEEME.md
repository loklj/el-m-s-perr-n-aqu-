# Encargo 11: documentos

Desde la raíz del repositorio:

```bash
python3 tests/documentos/generar.py
export ANDROID_JAR=/ruta/android-34/android.jar
sh tests/documentos/validar.sh
# Regresión completa, incluido TXT temporal de 240 MB con heap de 64 MiB:
sh tests/formatos/validar.sh --large
# Puente C# real con tipos Unity/JNI sustituidos:
export DOTNET_ROOT=/ruta/dotnet-con-sdk-8
python3 tests/formatos/validar-puente.py
```

`JAVA` y `JAVAC` permiten seleccionar ejecutables fuera del PATH. Se usó OpenJDK 21.0.2, `javac --release 8 -cp android.jar` API 34 y .NET SDK 8.0.428. Se compilan todos los plugins Folio Java sin dependencias externas de compilación. No es una compilación Gradle/D8 ni una APK Unity: las pruebas JVM no ejecutan Android/WebView, y las del puente C# usan stubs.

Resultados del 12 de noviembre de 2026, tras rebase sobre `50ddb17`:

| Grupo | Comprobaciones aprobadas |
| --- | ---: |
| Conversores documentales: formatos, recursos, daños, Unicode, cuotas y cancelación | 102 |
| Detector real de la biblioteca y diagnóstico documental | 8 |
| Regresión de formatos con `--large` | 1240 |
| PalmDOC/HUFF/CDIC | 4 |
| Detector previo de biblioteca | 10 |
| Contenedor EPUB | 13 |
| Puente C#: selección JNI de 12 formatos, FXR1 y PageTheme real | 23 |
| Total | **1400** |

Las comprobaciones por tramo del TXT grande forman parte de las 1240; no representan 1240 libros distintos. Los temporales grandes se borran. El test documental corre con heap máximo de 64 MiB. Prueba recursos inexistentes, límites de imágenes, rechazo DTD con UTF-8/UTF-16, profundidad excesiva, XML demasiado grande, cancelación en curso, capítulos completos, ODT con indicador de cifrado y RTF truncado/hexadecimal inválido/grupos excesivos. La imagen RTF se prueba tanto en hexadecimal como mediante `bin` y `shppict`.

## Contraste independiente del corpus

Los tres `propio.*` se abrieron y exportaron a PDF (una página cada uno) con LibreOfficeDev 26.8.0.0.alpha0. DOCX y ODT también se leyeron con Pandoc; python-docx comprobó texto, tabla e imagen de DOCX y Pillow decodificó los PNG originales. No se distribuyen esas herramientas ni los PDF exportados. Ejemplos:

```bash
pandoc tests/fixtures/documentos/propio.docx -t plain
pandoc tests/fixtures/documentos/propio.odt -t plain
soffice --headless --convert-to pdf --outdir /tmp/folio-docx tests/fixtures/documentos/propio.docx
soffice --headless --convert-to pdf --outdir /tmp/folio-odt tests/fixtures/documentos/propio.odt
soffice --headless --convert-to pdf --outdir /tmp/folio-rtf tests/fixtures/documentos/propio.rtf
```

Esto acredita apertura independiente de los fixtures, no conformidad completa con todas las normas, fidelidad de Word ni renderizado Quest. Los documentos, dibujos y textos del corpus son originales; no contienen macros, libros ajenos ni contenido cifrado real. La revisión de alcance comprueba que no haya cambios en Environments, Tools, Scripts/Menu, FolioWifiServer, FolioPdfRenderer ni FolioBook, y que LoadRaw siga intacto.
