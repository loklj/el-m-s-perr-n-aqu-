#!/usr/bin/env python3
"""Mide cómo maqueta y dibuja MuPDF un EPUB (opción C del Hito 2).

Es una prueba en PC, no en el Quest: sirve para comparar enfoques antes de
programar el Hito 2. En el visor se usaría la librería Android de MuPDF.

Uso:
    pip install pymupdf
    python3 tools/prototipos/medir_epub_mupdf.py tests/fixtures/libro-prueba.epub --guardar /tmp/paginas

Licencia: MuPDF/PyMuPDF son AGPL-3.0 (o licencia comercial de Artifex).
Este script solo se usa para medir; no se incluye en la app.
"""

import argparse
import os
import resource
import time

import pymupdf


def main():
    p = argparse.ArgumentParser()
    p.add_argument("epub")
    p.add_argument("--ancho", type=float, default=420, help="Ancho de página en puntos (A5 = 420).")
    p.add_argument("--alto", type=float, default=595, help="Alto de página en puntos (A5 = 595).")
    p.add_argument("--letra", type=float, default=12, help="Tamaño de letra base en puntos.")
    p.add_argument("--dpi", type=int, default=200, help="Resolución de las páginas dibujadas.")
    p.add_argument("--guardar", help="Carpeta donde guardar algunas páginas en PNG.")
    a = p.parse_args()

    t = time.time()
    doc = pymupdf.open(a.epub)
    doc.layout(width=a.ancho, height=a.alto, fontsize=a.letra)
    paginas = doc.page_count
    t_maquetado = time.time() - t

    muestras = sorted({0, min(3, paginas - 1), paginas // 2, paginas - 1})
    tiempos = []
    for i in muestras:
        t = time.time()
        pix = doc[i].get_pixmap(dpi=a.dpi)
        tiempos.append(time.time() - t)
        if a.guardar:
            os.makedirs(a.guardar, exist_ok=True)
            pix.save(os.path.join(a.guardar, f"pagina-{i + 1}.png"))

    t = time.time()
    doc.layout(width=a.ancho, height=a.alto, fontsize=a.letra * 4 / 3)
    paginas_grande = doc.page_count
    t_remaquetado = time.time() - t

    print(f"MuPDF {pymupdf.VersionFitz} | {os.path.basename(a.epub)}")
    print(f"  páginas a {a.letra:g} pt: {paginas} (maquetado {t_maquetado:.2f} s)")
    print(f"  dibujo por página a {a.dpi} dpi ({pix.width}x{pix.height}): "
          f"media {sum(tiempos) / len(tiempos) * 1000:.0f} ms, máx {max(tiempos) * 1000:.0f} ms")
    print(f"  re-maquetado a {a.letra * 4 / 3:g} pt: {paginas_grande} páginas en {t_remaquetado:.2f} s")
    print(f"  memoria máxima del proceso: {resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024:.0f} MB")


if __name__ == "__main__":
    main()
