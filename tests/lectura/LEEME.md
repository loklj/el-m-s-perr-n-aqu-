# Encargo 17: pruebas de lectura

Resultados del 12 de noviembre de 2026, después del rebase sobre `c29434d`:

| Grupo | Comprobaciones aprobadas |
| --- | ---: |
| Lectura JVM: geometría, nombres, TOC EPUB/NCX/MOBI/KF8/PDF, cuotas/cancelación y offsets de 64 bits | 77 |
| Biblioteca/MenuModel C# reales, con contratos Unity/JNI simulados | 11 |
| Fuentes/CSS real en Chromium: hashes, carga, composición y salto a anclas | 26 |
| Documentos y detector documental del encargo 11 | 110 |
| Formatos con TXT grande (`--large`) | 1240 |
| PalmDOC/HUFF/CDIC | 4 |
| Detector previo de biblioteca | 10 |
| Contenedor EPUB | 13 |
| Puente C# real y PageTheme/FXR1, con stubs | 23 |
| Total | **1514** |

Las comprobaciones por tramo del TXT grande forman parte de las 1240; no son 1240 libros distintos. JVM de lectura: heap máximo 96 MiB; TXT grande: heap 64 MiB y 240 MB de archivo temporal. El PDF disperso tiene offsets por encima de 5 GiB, ocupa pocos bloques físicos y se elimina al terminar. No se guarda un archivo gigante en el repositorio.

## Reproducir

Desde la raíz, con OpenJDK y SDK Android API 34:

```bash
export ANDROID_JAR=/ruta/android-34/android.jar
# JAVA y JAVAC permiten seleccionar ejecutables fuera del PATH.
python3 tests/lectura/generar.py
sh tests/lectura/validar.sh
sh tests/documentos/validar.sh
sh tests/formatos/validar.sh --large
export DOTNET_ROOT=/ruta/dotnet-con-sdk-8
python3 tests/formatos/validar-puente.py
python3 tests/lectura/validar-ui.py
# Requiere Playwright Python y /usr/bin/chromium; usa HTML emitido por validar.sh.
python3 tests/lectura/validar-web.py
```

Se usó OpenJDK 21.0.2 con `--release 8`, `android.jar` API 34, SDK .NET 8.0.428 y Chromium instalado. Se compilan todos los `Folio*.java` sin bibliotecas de terceros de compilación. La prueba C# compila el MenuModel y BookLibrary reales y verifica preferencias, nueva paginación solicitada, etiquetas actualizadas, lista de nombres, compatibilidad de marcadores antiguos, teclado y destino del índice. **Los stubs no prueban SQLite Android, TouchScreenKeyboard real ni Unity.**

La prueba Chromium usa el CSS emitido por Java y la misma CSP que el paginador. Extrae fuentes del AAR y responde solo a sus rutas locales. Las cuatro composiciones dieron 9, 12, 16 y 8 páginas; el destino `p60` quedó en índices 6, 8, 12 y 5 y se comprobó visible al desplazarse. Esto prueba geometría y carga de fuentes en Chromium, no WebView en Horizon OS ni la captura Bitmap.

PyMuPDF leyó independientemente los PDF clásicos y con ObjStm/xref stream: dos páginas y tres marcadores, con destinos 1, 2 y 2 (numeración visible). Solo se usa como herramienta de contraste en desarrollo; no forma parte del APK. Los fixtures son originales, generados por el script del repositorio.

## Fuentes

`FolioFonts.aar` contiene ocho TTF originales (dos familias, cuatro variantes), manifiesto Android y avisos OFL. El inventario de hashes y URLs está en `fuentes.json`. Para regenerarlo desde la revisión fijada de Google Fonts, opcionalmente ejecutar:

```bash
python3 tests/lectura/empaquetar-fuentes.py
```

Este paso requiere red y no es necesario para las pruebas normales. Los nombres internos y bytes de las fuentes permanecen intactos. Las licencias copiadas como texto se normalizan a LF sin alterar su contenido.

## Límites de la evidencia

No se ha compilado una APK Unity ni ejecutado en Quest. Falta validar empaquetado AAR en Unity, carga de fuentes en WebView, teclado/persistencia SQLite, memoria, latencia, nitidez y FPS. Los índices PDF/MOBI complejos requieren ampliar corpus. El menú conserva el orden anterior y no hay cambios propios en Environments, Tools, Resources ni herramientas. Los marcadores siguen guardando páginas; no se convierten en localizadores semánticos al cambiar tipografía.

## Portada sola (13 de noviembre de 2026)

```bash
DOTNET_ROOT=/ruta/dotnet-con-sdk-8 python3 tests/lectura/validar-portada.py
DOTNET_ROOT=/ruta/dotnet-con-sdk-8 python3 tests/lectura/validar-ui.py
```

863 comprobaciones de `BookSpread` y miembros reales de `FolioBook`: el script extrae las propiedades y métodos originales, sin duplicar el algoritmo en el arnés. Se sustituyen solamente el renderizador, los contratos Unity y los miembros de escena que no intervienen en esta prueba; no equivale a compilar `FolioBook` completo ni Unity. Se comprueban páginas 0..100, todos los saltos, recorridos ida/vuelta, lados normal/manga, última página, reapertura de preferencias, cambio diferido, animación invocada, papel vacío y fallo de carga.

La batería biblioteca/menú pasa 22 comprobaciones (incluye opciones por libro y repaginación sin redondear páginas impares). Regresión: 28 del puente C#, 22 JVM y 11 JavaScript de `tests/busqueda/validar.sh`. Total de esta entrega: **946 comprobaciones**. Sigue pendiente la prueba de animación/materiales y gestos en Quest.
