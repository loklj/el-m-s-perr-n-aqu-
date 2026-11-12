# Preferencias de lectura, índice y marcadores

## Estado

12 de noviembre de 2026. Encargo 17, partes **2, 4 y 5**, rama `fase5/lectura`, rebasada sobre `c29434d` de `fase-1/preparacion`. Implementado para revisión de Claude; pendiente APK y prueba en Quest. La parte 1 sigue en `Reading/PageTheme.cs`. Búsqueda y panel plano, partes 3 y 6, no forman parte de esta entrega.

No hay cambios propios en `Scripts/Environments`, `Scripts/Tools`, ningún directorio `Resources/` ni `herramientas/`. En MenuModel se conservan las opciones existentes en su orden; se agregan las opciones nuevas al final de Biblioteca/Ajustes y se usa un submenú paginado para las listas. Las etiquetas de Ajustes se reconstruyen al cambiar las preferencias.

## Tipografía y nueva paginación

En EPUB, TXT, MOBI/AZW/AZW3, DOCX, ODT y RTF:

| Ajuste | Valores | PlayerPrefs |
| --- | --- | --- |
| Fuente | Gentium Book Plus / Atkinson Hyperlegible | `folioxr.readingFont`: 0 / 1 |
| Interlineado | 1,2 / 1,4 / 1,8 | `folioxr.lineSpacing` |
| Márgenes | Estrechos / normales / amplios: 24 / 48 / 72 px CSS | `folioxr.pageMargin` |
| Tamaño, existente | Pequeña / normal / grande | `folioxr.epubFontScale`, sin cambiar su contrato |

Las preferencias son globales. Cambiarlas recrea el paginador y su caché para el libro HTML abierto y conserva el **progreso proporcional** existente. No afectan la tipografía del PDF ni de los cómics. Día/sepia/noche siguen funcionando por PageTheme sin modificaciones.

`FolioReadingSettings` mantiene un paso de columna de 596 px: ancho disponible `596 − 2 × margen`, separación `2 × margen`; la altura es `842 − 2 × margen`. CSS fija la familia y el interlineado y el conteo espera `document.fonts.ready`. Los archivos incluidos se sirven mediante una lista cerrada de rutas desde los assets Android, sin habilitar acceso a archivos, red o fuentes ofuscadas del libro. Las familias incluyen regular, cursiva, negrita y negrita cursiva originales.

### Fuentes y licencias

Las dos familias usan **SIL Open Font License 1.1**, autorizada expresamente para este encargo. No se modifica ningún TTF ni su nombre interno. Se conservan copyrights y nombres reservados de Gentium/SIL. Los nombres de ruta `serif-*.ttf` y `sans-*.ttf` y el alias CSS `FolioText` no cambian los archivos tipográficos.

- Gentium Book Plus: SIL International; [fuente y licencia](https://github.com/google/fonts/tree/c30b221beb0c1da59d563feb91ec8656a1fa972a/ofl/gentiumbookplus).
- Atkinson Hyperlegible: Braille Institute of America; [fuente y licencia](https://github.com/google/fonts/tree/c30b221beb0c1da59d563feb91ec8656a1fa972a/ofl/atkinsonhyperlegible).
- Revisión fijada: `c30b221beb0c1da59d563feb91ec8656a1fa972a`. Procedencia, tamaño y SHA-256 de cada TTF: `tests/lectura/fuentes.json`.
- AAR solo de assets: `Assets/Plugins/Android/FolioFonts.aar`, 1.898.658 bytes; SHA-256 `0ece135a79576b0c438295efcaebd04c9eeb4cce83274f7697ac20f71601ebc5`. No añade dependencias Gradle, código nativo ni clases Java de terceros. Incluye también ambas licencias.
- Avisos legibles en `docs/licencias/` y `Assets/StreamingAssets/Licencias/`. El empaquetado reproducible opcional está en `tests/lectura/empaquetar-fuentes.py`.

## Índice navegable

Biblioteca → **Índice del libro**. Lista de seis entradas por página con título, profundidad y número de página; botones para avanzar, volver, actualizar y cancelar. Saltar muestra la doble página que contiene el destino. Un destino inexistente se muestra desactivado; no se inventa una página.

La extensión del protocolo es `requestContents(path, key)` → los mismos `getStatus`/`clearStatus`: `OK:{"entries":[{"title":"…","page":0,"depth":0}],"message":"…"}`. Las páginas son índices desde cero. El libro usa su renderizador actual; consultar el índice no abre una instancia adicional que cierre el libro. La cancelación usa la cola/generación del paginador y descarta respuestas de libros anteriores.

| Formato | Fuente del índice | Alcance |
| --- | --- | --- |
| EPUB | `nav` con `epub:type=toc`/`role=doc-toc`, o NCX como reserva | Conserva entradas múltiples al mismo capítulo, fragmentos y jerarquía; excluye landmarks/page-list y enlaces externos. La página de cada ancla se calcula con la composición actual, tras cargar la fuente |
| MOBI/AZW | INDX/NCX + etiquetas CNCX, o guía HTML `type=toc` con enlaces `filepos` | Mapea offsets de texto a capítulos e inserta anclas locales antes del render. No fabrica un índice si el archivo no lo contiene |
| KF8/AZW3 | INDX/NCX/CNCX y posiciones en esqueletos/fragmentos | Traduce el destino a la posición reconstruida; fixture específico de destino dentro de un fragmento. Las variantes reales complejas siguen siendo experimentales |
| PDF | Árbol Outlines y destinos directos/nombrados | Parser propio de metadatos, separado de PdfRenderer: xref clásico, revisiones incrementales, xref stream y ObjStm con Flate/PNG predictor; UTF-16/UTF-8 BOM y PDFDocEncoding para títulos. Solo acciones locales GoTo; nunca ejecuta acciones ni descifra |

No hay una biblioteca PDF externa. API 34 no expone estos marcadores en PdfRenderer. Si la variante del índice no es compatible, se explica en el menú y el PDF continúa siendo legible con el motor existente. Se mantienen FXR1, PageTheme y la precarga.

Límites del índice PDF: 300.000 referencias xref, 100.000 páginas, 32 revisiones, profundidad estructural 64, streams comprimidos/descomprimidos de hasta 4 MiB y 2.048 entradas de índice. Caché de 64 objetos pequeños (cada objeto cacheable limitado por estimación a 64 KiB); offsets de archivo de 64 bits. Filtros distintos de Flate, cifrado, estructuras dañadas/cíclicas o cuotas excedidas producen un diagnóstico, sin recorrer ni cargar el PDF completo. Se prueba un archivo disperso temporal con metadatos por encima de 5 GiB.

EPUB/MOBI limitan el índice a 2.048 entradas y lo indican al alcanzar el límite. EPUB/HTML siguen calculando el conteo completo antes de entregar páginas; los timeouts y la latencia en libros extensos requieren medición en Quest. Un índice ausente no se confunde con un error de lectura del libro.

## Marcadores con nombre

El botón de marcar/quitar existente permanece. Biblioteca → **Lista de marcadores** muestra todos por página, con acciones **ir**, **nombrar** y **quitar**, tres marcadores por pantalla. Nombrar abre TouchScreenKeyboard del sistema, igual que el renombrado del libro; cancelar no guarda. Una etiqueta vacía se muestra como «Página N».

SQLite ya tenía `bookmarks.label`: se reutiliza, **sin borrar datos ni cambiar la versión del esquema**. `bookmarksJson` conserva `positions` y añade `entries` con posición/etiqueta. El cliente también admite JSON antiguo sin `entries`. Los nombres se normalizan, se limitan a 120 caracteres y no cortan pares sustitutos. La escritura usa `UPDATE` por libro y posición: si se eliminó el marcador mientras estaba abierto el teclado, no lo recrea. Se conserva la copia de seguridad de SQLite existente.

**Límite heredado:** los marcadores guardan número de página, no localizadores semánticos. Sus nombres se conservan al repaginar, pero un cambio tipográfico puede mover el texto marcado a otra página; la reubicación proporcional se aplica al progreso del libro, no a los marcadores. Tampoco se migra la tinta a nuevos anclajes. La persistencia real y el teclado en Horizon OS deben probarse en el visor.

## Validación

Comandos y recuento en [tests/lectura/LEEME.md](../tests/lectura/LEEME.md). Incluye compilación de todos los plugins Java contra API 34, fixtures propios EPUB/NCX/MOBI/KF8/PDF, datos inválidos, cancelación, CSS real en Chromium, hashes de fuentes y C# real de MenuModel/BookLibrary/puente con stubs. PyMuPDF leyó de forma independiente los PDF válidos con sus tres marcadores. No se distribuye esa herramienta ni se copia código suyo.

Estas pruebas no son una compilación de Unity ni acreditan WebView de Horizon OS, SQLite Android, nitidez a 45 cm, rendimiento ni 72 FPS. Las pruebas del dispositivo están en el Paso 10 de la guía.
