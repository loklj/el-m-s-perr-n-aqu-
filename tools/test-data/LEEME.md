# Generadores de datos de prueba

| Script | Para qué |
| --- | --- |
| `fixtures.py` | Muestras pequeñas que sí van a Git, en `tests/fixtures/` (EPUB, CBZ, manga RTL, TXT en 4 codificaciones). |
| `gigantes.py` | Archivos de estrés de la Fase 4. Se escriben en `tools/test-data/salida/`, que Git ignora. |

Requisitos: Python 3 y `pip install pillow` (Pillow solo hace falta para CBZ y EPUB).

## gigantes.py

Escribe en streaming, con memoria acotada, y deja `manifiesto.json` con parámetros, tamaños y SHA-256 de cada archivo.

```bash
# Todo con los tamaños por defecto (~0,5 GB):
python3 tools/test-data/gigantes.py

# Solo el PDF de 100.001 páginas (~54 MB):
python3 tools/test-data/gigantes.py --solo pdf

# Prueba rápida:
python3 tools/test-data/gigantes.py --pdf-paginas 2000 --txt-mb 5 --cbz-imagenes 50 --epub-kb 1024
```

| Opción | Por defecto | Notas |
| --- | --- | --- |
| `--pdf-paginas` | 100.001 | PDF 1.4 A5, árbol de páginas de dos niveles (nodos de 500). Cada página muestra su número y una barra de progreso. |
| `--txt-mb` | 300 | Tamaño de cada TXT. |
| `--txt-codificaciones` | `utf-8` | Cualquier combinación de `utf-8`, `utf-16` (con BOM) y `cp1252` (CRLF). |
| `--cbz-imagenes` | 3000 | JPEG de 900×1350, sin comprimir dentro del ZIP (ZIP64 si hace falta). |
| `--epub-kb` | 30.000 | EPUB válido con un capítulo de ese tamaño, con texto variado (se comprime ~4:1, como un libro real). |
| `--limite-gb` | 5 | Si el total estimado lo supera, no genera nada. También exige 1 GB libre de margen. |
| `--semilla` | 20261105 | Mismo número, mismo contenido. |

Verificado el 5 de noviembre de 2026 en Linux: el PDF de 100.001 páginas pesa 54 MB, se genera en segundos con 26 MB de memoria máxima, `pdfinfo` cuenta 100.001 páginas, `qpdf --check` no encuentra errores y las páginas 1, 50.000 y 100.001 se dibujan bien. El EPUB con capítulo de 2 MB pasa epubcheck 5.3.0 sin errores ni avisos.

**No incluido:** RAR sólido (necesita la herramienta propietaria `rar`). Para CBR, comprimir a mano una carpeta de imágenes con `rar a -s`.

Para copiar un archivo al visor:

```bash
adb push tools/test-data/salida/pdf-100001-paginas.pdf /sdcard/Download/
```
