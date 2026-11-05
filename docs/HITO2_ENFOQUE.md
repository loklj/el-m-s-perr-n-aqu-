# Hito 2 — Enfoque propuesto: páginas EPUB en el libro 3D

Estado: **propuesta para aprobar antes de programar** (5 de noviembre de 2026, escrita por Claude). No hay código del Hito 2 todavía.

## En pocas líneas

- El problema: un PDF ya trae páginas, pero un EPUB es texto que se acomoda al tamaño de la página. Hay que **maquetarlo** (decidir qué texto cabe en cada página) y **dibujar cada página como imagen** para ponerla en el libro 3D, igual que en el Hito 1.
- Hay cuatro caminos (tabla abajo). Probé uno en el PC con nuestros EPUB de prueba: **MuPDF funciona muy bien** (texto nítido, tildes, sangrías, títulos; ~6–20 ms por página; cambiar el tamaño de letra vuelve a paginar al instante).
- **Recomendación:** prototipar el Hito 2 con **MuPDF** y tener **WebView** como plan B.
- **Decisión que necesito de ti:** MuPDF es **AGPL**. Si la app es solo para ti, o la publicas con su código abierto, no hay problema. Si algún día quieres venderla con código cerrado, habría que pagar una licencia comercial a Artifex o usar el plan B. **¿Cuál es tu caso?**

## Las cuatro opciones

| | A. WebView del sistema | B. Maquetado propio (StaticLayout de Android) | C. MuPDF | D. Vuplex 3D WebView |
| --- | --- | --- | --- | --- |
| Cómo funciona | Abrir cada capítulo en el WebView de Android fuera de pantalla, paginar con columnas CSS y capturar la página a imagen | Convertir XHTML a párrafos con estilos y maquetarlos con el motor de texto de Android | Librería nativa que abre el EPUB, lo maqueta y dibuja cada página a imagen | Plugin comercial de Unity que dibuja una web en una textura |
| Fidelidad del EPUB | La más alta (es un navegador) | Media: texto, títulos, cursivas, imágenes, listas; sin CSS complejo | Alta para libros normales (CSS común, imágenes, tablas simples) | Igual que A |
| Hilo | Solo en el hilo de interfaz de Android | Cualquier hilo | Cualquier hilo | Lo gestiona el plugin |
| Otros formatos que cubre | — | Base para DOCX/ODT/RTF/TXT | **También PDF, CBZ, FB2, XPS y MOBI** | — |
| Licencia / costo | Gratis (sistema) | Gratis (código propio) | **AGPL-3.0** o licencia comercial | De pago, código nativo cerrado |
| Riesgo principal | Capturar páginas fuera de pantalla es delicado: hace falta `WebView.enableSlowWholeDocumentDraw()` y tiene costo de rendimiento; afinidad con el hilo de UI | Mucho código propio; EPUB complejos se verán simplificados | Licencia; memoria con capítulos enormes; compilar/integrar la librería Android | Costo y dependencia de un tercero |

Notas:

- **A:** el Quest trae el Android System WebView preinstalado (lo usan plugins de terceros para Quest). Hay una librería MIT, *SimpleUnity3DWebView*, que ya dibuja un WebView en una textura de Unity en Meta Quest; sirve como referencia, aunque su transferencia es por CPU.
- **B:** encaja con el «modelo común» de la Fase 0 para DOCX, ODT, RTF y TXT, pero obliga a escribir y mantener un maquetador.
- **C:** MuPDF tiene enlaces Java para Android (*mupdf-android-fitz*, AGPL-3.0), publicados como artefacto Maven por Artifex. Desde la nube no pude descargarlo (dominio bloqueado); hay que comprobar la versión exacta en el PC.

## Medición de la opción C en PC (no en el Quest)

Script: `tools/prototipos/medir_epub_mupdf.py`. MuPDF 1.28.6, página A5 (420×595 pt), letra de 12 pt, dibujo a 200 dpi (1167×1653 px), CPU Intel Xeon de 2 núcleos.

| Libro | Páginas | Maquetado inicial | Dibujar una página | Cambiar a 16 pt | Memoria máx. |
| --- | --- | --- | --- | --- | --- |
| `tests/fixtures/libro-prueba.epub` (21 KB) | 11 | < 0,01 s | 6–16 ms | < 0,01 s (16 págs.) | 86 MB |
| EPUB con un capítulo de 30 MB (`gigantes.py --epub-kb 30000`) | 18.526 | 3,2 s | Primera página del capítulo enorme: ~3,1 s; luego 14–20 ms en cualquier posición | 3,2 s (35.615 págs.) | ~600 MB |

Lectura: con libros normales es instantáneo. Un capítulo de 30 MB (caso extremo) tarda unos segundos la primera vez y usa ~600 MB. Hay que repetir la medición en el Quest 3, donde la CPU es más lenta. La memoria con capítulos enormes es el punto a vigilar, junto al límite de ~5,75 GiB del visor y el presupuesto interno de 1,5 GiB de la arquitectura.

## Plan del prototipo (si apruebas C)

1. Añadir la librería Android de MuPDF (AAR) en `Assets/Plugins/Android` y una clase Java parecida a `FolioPdfRenderer`: abrir el EPUB, maquetar con el tamaño de página y de letra, y dibujar la página N a imagen en un hilo propio.
2. Reutilizar `FolioBook` del Hito 1: el libro no necesita saber si la página viene de un PDF o de un EPUB.
3. Botones para cambiar el tamaño de letra (por ejemplo, la cruceta del mando) que vuelvan a paginar **conservando la posición de lectura** (MuPDF tiene marcadores de posición que sobreviven al re-maquetado).
4. Probar en el visor con `tests/fixtures/libro-prueba.epub` y con el EPUB gigante.

Criterios para aprobar el Hito 2:

- [ ] El texto de 12 pt se lee bien a ~45 cm, sin parpadeo al mover la cabeza.
- [ ] Pasar página tarda menos de 100 ms con la página siguiente ya preparada.
- [ ] Cambiar el tamaño de letra mantiene el párrafo que se estaba leyendo.
- [ ] El EPUB con capítulo de 30 MB abre con indicador de progreso y sin cerrarse por memoria.
- [ ] Tildes, eñes, cursivas, títulos, la imagen y la lista del EPUB de prueba se ven bien.

## Si eliges el plan B (WebView)

Prototipo equivalente con un WebView fuera de pantalla: `enableSlowWholeDocumentDraw()` antes de crear el WebView, maquetado por columnas CSS del ancho de la página, `draw()` sobre un `Bitmap` y la misma entrega a textura del Hito 1. Se descarta MuPDF para EPUB y se mantiene PDFium/PdfRenderer para PDF, como en la Fase 0.

## Referencias

- MuPDF para Android (JNI, AGPL-3.0): <https://github.com/ArtifexSoftware/mupdf-android-fitz>
- Uso de MuPDF en Android (repositorio Maven de Artifex): <https://mupdf.readthedocs.io/en/latest/guide/using-with-android.html>
- `WebView.enableSlowWholeDocumentDraw()` (documentación de Android, vía referencia .NET para Android): <https://learn.microsoft.com/dotnet/api/android.webkit.webview.enableslowwholedocumentdraw>
- Simple Unity 3D WebView (MIT, Android/Meta Quest): <https://github.com/t-34400/SimpleUnity3DWebView>
- Vuplex 3D WebView: <https://developer.vuplex.com/webview/overview>
