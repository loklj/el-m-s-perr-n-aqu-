# Generadores de datos de prueba

| Script | Para qué |
| --- | --- |
| `fixtures.py` | Muestras pequeñas que sí van a Git, en `tests/fixtures/` (EPUB, CBZ, manga RTL, TXT en 4 codificaciones). |
| `gigantes.py` | Archivos de estrés de la Fase 4. Se escriben en `tools/test-data/salida/`, que Git ignora. |

Requisitos: Python 3 y Pillow (para CBZ, EPUB y los archivos de varios GB). En Pop!_OS/Ubuntu: `sudo apt install python3-pil python3-reportlab`. Con `pip`, usa un entorno virtual: las versiones recientes de Ubuntu bloquean `pip install` en el Python del sistema. `fixtures.py` necesita Pillow 10.1 o superior.

## gigantes.py

Escribe en streaming, con memoria acotada, y deja `manifiesto.json` con parámetros, tamaños y SHA-256 de cada archivo.

```bash
# Todo con los tamaños por defecto (~0,5 GB):
python3 tools/test-data/gigantes.py

# Solo el PDF de 100.001 páginas (~54 MB):
python3 tools/test-data/gigantes.py --solo pdf

# Prueba rápida:
python3 tools/test-data/gigantes.py --pdf-paginas 2000 --txt-mb 5 --cbz-imagenes 50 --epub-kb 1024

# Archivos de 5 GB (PDF de páginas con imágenes y CBZ con ZIP64), ~11 GB en total:
python3 tools/test-data/gigantes.py --solo pdf-gb --solo cbz-gb --gb 5

# TXT de 5 GB:
python3 tools/test-data/gigantes.py --solo txt --txt-mb 5120
```

| Opción | Por defecto | Notas |
| --- | --- | --- |
| `--pdf-paginas` | 100.001 | PDF 1.4 A5, árbol de páginas de dos niveles (nodos de 500). Cada página muestra su número y una barra de progreso. |
| `--txt-mb` | 300 | Tamaño de cada TXT. |
| `--txt-codificaciones` | `utf-8` | Cualquier combinación de `utf-8`, `utf-16` (con BOM) y `cp1252` (CRLF). |
| `--cbz-imagenes` | 3000 | JPEG de 900×1350, sin comprimir dentro del ZIP (ZIP64 si hace falta). |
| `--epub-kb` | 30.000 | EPUB válido con un capítulo de ese tamaño, con texto variado (se comprime ~4:1, como un libro real). |
| `--gb` | 5 | Tamaño en GiB de `pdf-gb` (PDF cuyas páginas son imágenes de ~1 MB) y `cbz-gb` (CBZ con ZIP64). Solo se generan si se piden con `--solo`. |
| `--limite-gb` | 12 | Si el total estimado lo supera, no genera nada. También exige 1 GB libre de margen. |
| `--semilla` | 20261105 | Mismo número, mismo contenido. |

Verificado el 5 de noviembre de 2026 en Linux: el PDF de 100.001 páginas pesa 54 MB, se genera en segundos con 26 MB de memoria máxima, `pdfinfo` cuenta 100.001 páginas, `qpdf --check` no encuentra errores y las páginas 1, 50.000 y 100.001 se dibujan bien. El EPUB con capítulo de 2 MB pasa epubcheck 5.3.0 sin errores ni avisos.

Archivos de 5 GB, verificados el mismo día: el PDF de 5.659 páginas (5,37 GB) pasa `pdfinfo` y `qpdf --check`; poppler dibuja las páginas 1, 2.829 y 5.659, y MuPDF dibuja la última en 0,2 s con 62 MB de memoria. El CBZ ZIP64 de 5.662 imágenes (5,37 GB) pasa `unzip -t`, y su última imagen, que empieza más allá de 4 GiB, se lee bien. Generar ambos tomó 13 s con 53 MB de memoria.

Las páginas de `pdf-gb` y `cbz-gb` son ruido de colores con el número de página encima. Es a propósito: el ruido no se puede comprimir, así que el archivo realmente pesa lo que se pide.

**No incluido:** RAR sólido (necesita la herramienta propietaria `rar`). Para CBR, comprimir a mano una carpeta de imágenes con `rar a -s`.

Para copiar un archivo al visor:

```bash
adb push tools/test-data/salida/pdf-100001-paginas.pdf /sdcard/Download/
```
