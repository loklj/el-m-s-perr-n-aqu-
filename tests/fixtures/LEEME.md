# Muestras pequeñas (fixtures)

Archivos de prueba **con contenido original del proyecto Folio XR** (texto e imágenes generados por script). No contienen material de terceros y se pueden redistribuir dentro del repositorio.

Se regeneran con:

```bash
pip install pillow reportlab
python3 tools/test-data/fixtures.py
python3 tools/generar_pdf_prueba.py tests/fixtures/pdf-prueba.pdf
```

`fixtures.py` es determinista: con la misma versión del script, los archivos y sus hashes no cambian.

| Archivo | Qué prueba |
| --- | --- |
| `pdf-prueba.pdf` | PDF A5 de 8 páginas: portada, tamaños de letra de 8 a 18 pt, viñetas, texto corrido. Es el mismo PDF que trae el Hito 1. |
| `libro-prueba.epub` | EPUB 3 válido (epubcheck 5.3.0: 0 errores, 0 avisos) con portada, `nav.xhtml` y `toc.ncx` (compatible con lectores EPUB 2), 3 capítulos, CSS, imagen con pie de figura, cursiva y lista. Texto con tildes, eñe, diéresis y ¿¡. |
| `comic-prueba.cbz` | Cómic de 8 páginas JPEG (`001.jpg`…`008.jpg`) con `ComicInfo.xml` (`Manga=No`). |
| `manga-prueba-rtl.cbz` | Igual, pero con `ComicInfo.xml` `Manga=YesAndRightToLeft` y cuadros numerados de derecha a izquierda, para probar el modo manga. |
| `texto-utf8.txt` | UTF-8 sin BOM. |
| `texto-utf8-bom.txt` | UTF-8 con BOM. |
| `texto-utf16le-bom.txt` | UTF-16 little-endian con BOM. |
| `texto-windows1252.txt` | Windows-1252 con saltos de línea CRLF (sin la raya «—», que no existe en esa codificación). |

Los cuatro TXT tienen el mismo texto: la app debe mostrarlos igual, con tildes y eñes correctas.

Para archivos gigantes (PDF de 100.001 páginas, TXT de cientos de MB, CBZ con miles de imágenes, EPUB con un capítulo enorme), usa `tools/test-data/gigantes.py`. Esos archivos no se guardan en Git.
