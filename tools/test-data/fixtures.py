#!/usr/bin/env python3
"""Genera las muestras pequeñas de tests/fixtures/ para Folio XR.

Todo el contenido (texto e imágenes) es original de este proyecto y se genera
aquí mismo; no hay libros ni cómics de terceros.

Uso (desde la raíz del repositorio):
    pip install pillow
    python3 tools/test-data/fixtures.py            # escribe en tests/fixtures/
    python3 tools/test-data/fixtures.py --salida /tmp/fixtures

La salida es determinista: misma versión del script -> mismos archivos
(las fechas internas de ZIP se fijan para que los hashes no cambien).
"""

import argparse
import io
import os
import zipfile

from PIL import Image, ImageDraw, ImageFont

FECHA_ZIP = (2026, 11, 5, 0, 0, 0)


def fuente(tam):
    """Fuente TrueType incluida en Pillow (>= 10.1): dibuja tildes y eñe."""
    return ImageFont.load_default(size=tam)

PARRAFOS = [
    "La lectora encendió la lámpara y abrió el libro por la mitad, donde había "
    "dejado una hoja seca como señal. Afuera llovía sin prisa.",
    "Cada página tenía un olor distinto: algunas a papel nuevo, otras a cajón "
    "cerrado durante años. Ella las pasaba despacio, como quien cruza un puente.",
    "Al final del capítulo encontró una nota escrita a lápiz: «Si llegaste hasta "
    "aquí, sigue un poco más». Sonrió y obedeció.",
    "El texto de prueba incluye tildes (á, é, í, ó, ú), la eñe (ñ), la diéresis "
    "(ü) y signos de apertura (¿¡) para comprobar la codificación.",
]

CAPITULOS = [
    ("Capítulo 1 — La lámpara", 0),
    ("Capítulo 2 — El puente de papel", 1),
    ("Capítulo 3 — La nota a lápiz", 2),
]


# ---------------------------------------------------------------------------
# Utilidades

def zip_info(nombre, comprimir=True):
    info = zipfile.ZipInfo(nombre, date_time=FECHA_ZIP)
    info.compress_type = zipfile.ZIP_DEFLATED if comprimir else zipfile.ZIP_STORED
    info.external_attr = 0o644 << 16
    return info


def imagen_pagina(ancho, alto, numero, total, color, titulo, manga=False):
    """Página de cómic simple: viñetas, círculos y número de página."""
    img = Image.new("RGB", (ancho, alto), (250, 248, 242))
    d = ImageDraw.Draw(img)
    margen = ancho // 20
    d.rectangle([margen, margen, ancho - margen, alto // 8], fill=color)
    d.text((margen * 2, margen + 18), titulo, fill=(255, 255, 255), font=fuente(44))
    filas, cols = 3, 2
    gap = margen // 2
    top = alto // 8 + gap
    alto_v = (alto - top - margen * 2 - gap * (filas - 1)) // filas
    ancho_v = (ancho - margen * 2 - gap) // cols
    orden = 1
    for f in range(filas):
        rango = range(cols - 1, -1, -1) if manga else range(cols)
        for c in rango:
            x0 = margen + c * (ancho_v + gap)
            y0 = top + f * (alto_v + gap)
            d.rectangle([x0, y0, x0 + ancho_v, y0 + alto_v], outline=(20, 20, 20), width=4)
            r = alto_v // 4
            cx, cy = x0 + ancho_v // 2, y0 + alto_v // 2
            d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=color, width=6)
            d.text((x0 + 14, y0 + 10), f"cuadro {orden}", fill=(20, 20, 20), font=fuente(30))
            orden += 1
    d.text((ancho // 2, alto - margen // 2), f"{numero} / {total}", fill=(60, 60, 60),
           font=fuente(30), anchor="ms")
    return img


def jpeg(img, calidad=82):
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=calidad, optimize=True)
    return buf.getvalue()


def png(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


# ---------------------------------------------------------------------------
# CBZ

COMIC_INFO = """<?xml version="1.0" encoding="utf-8"?>
<ComicInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Title>{titulo}</Title>
  <Series>Folio XR - muestras</Series>
  <Number>1</Number>
  <Summary>Cómic de prueba generado por Folio XR. Contenido original del proyecto.</Summary>
  <Writer>Proyecto Folio XR</Writer>
  <PageCount>{paginas}</PageCount>
  <LanguageISO>es</LanguageISO>
  <Manga>{manga}</Manga>
</ComicInfo>
"""


def generar_cbz(ruta, titulo, paginas, manga):
    color = (40, 90, 160) if not manga else (170, 50, 60)
    with zipfile.ZipFile(ruta, "w") as z:
        z.writestr(zip_info("ComicInfo.xml"), COMIC_INFO.format(
            titulo=titulo, paginas=paginas, manga="YesAndRightToLeft" if manga else "No"))
        for n in range(1, paginas + 1):
            img = imagen_pagina(800, 1200, n, paginas, color, titulo, manga=manga)
            # Nombres con relleno de ceros y sin carpeta, como la mayoría de los CBZ.
            z.writestr(zip_info(f"{n:03d}.jpg", comprimir=False), jpeg(img))


# ---------------------------------------------------------------------------
# EPUB 3 (con nav.xhtml y toc.ncx para lectores EPUB 2)

CONTAINER = """<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>
"""

CSS = """body { font-family: serif; line-height: 1.5; margin: 0 5%; }
h1 { font-size: 1.6em; margin: 1.2em 0 0.8em; }
p { text-indent: 1.5em; margin: 0 0 0.6em; }
p.primero { text-indent: 0; }
.nota { font-style: italic; color: #444; }
figure { text-align: center; margin: 1em 0; }
figure img { max-width: 80%; }
figcaption { font-size: 0.85em; color: #555; }
"""


def xhtml(titulo, cuerpo):
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="es" lang="es">
<head>
  <meta charset="UTF-8"/>
  <title>{titulo}</title>
  <link rel="stylesheet" type="text/css" href="estilo.css"/>
</head>
<body>
{cuerpo}
</body>
</html>
"""


def capitulo_html(titulo, indice, repeticiones):
    partes = [f'  <section epub:type="chapter">', f"    <h1>{titulo}</h1>"]
    for r in range(repeticiones):
        for i, p in enumerate(PARRAFOS):
            clase = ' class="primero"' if r == 0 and i == 0 else ""
            partes.append(f"    <p{clase}>{p}</p>")
        if r == 0 and indice == 1:
            partes.append('    <figure><img src="imagenes/puente.png" alt="Dibujo de un puente de papel"/>'
                          "<figcaption>Figura 1. Un puente dibujado para la prueba.</figcaption></figure>")
        if r == 0 and indice == 2:
            partes.append('    <p class="nota">Esta línea va en cursiva para probar estilos.</p>')
            partes.append("    <ul><li>Primer elemento de una lista</li><li>Segundo elemento</li></ul>")
    partes.append("  </section>")
    return "\n".join(partes)


def capitulo_enorme_html(titulo, bytes_objetivo, semilla=20261105):
    """Capítulo con texto variado (no repetido) hasta alcanzar el tamaño pedido."""
    import random
    rnd = random.Random(semilla)
    palabras = (" ".join(PARRAFOS)).replace(",", "").replace(".", "").replace("«", "").replace("»", "").split()
    partes = ['  <section epub:type="chapter">', f"    <h1>{titulo}</h1>"]
    tam = sum(len(x.encode("utf-8")) for x in partes)
    n = 0
    while tam < bytes_objetivo:
        n += 1
        frase = " ".join(rnd.choice(palabras) for _ in range(rnd.randint(40, 120)))
        linea = f"    <p>{n}. {frase[0].upper()}{frase[1:]}.</p>"
        partes.append(linea)
        tam += len(linea.encode("utf-8")) + 1
        if n % 200 == 0:
            partes.append(f'    <h2 id="seccion{n // 200}">Sección {n // 200}</h2>')
    partes.append("  </section>")
    return "\n".join(partes)


def generar_epub(ruta, titulo, identificador, repeticiones=6, capitulo_gigante_kb=0):
    portada = Image.new("RGB", (600, 900), (40, 90, 160))
    d = ImageDraw.Draw(portada)
    d.rectangle([40, 300, 560, 600], outline=(255, 255, 255), width=6)
    d.text((300, 450), titulo, fill=(255, 255, 255), font=fuente(42), anchor="mm")
    puente = Image.new("RGB", (500, 250), (250, 248, 242))
    dp = ImageDraw.Draw(puente)
    dp.arc([50, 60, 450, 420], 180, 360, fill=(120, 80, 40), width=10)
    dp.line([30, 240, 470, 240], fill=(60, 60, 60), width=6)

    capitulos = list(CAPITULOS)
    if capitulo_gigante_kb:
        capitulos.append(("Capítulo enorme (prueba de estrés)", 3))

    manifest, spine, nav_items, ncx_points = [], [], [], []
    archivos = {}
    for n, (cap_titulo, indice) in enumerate(capitulos, start=1):
        nombre = f"capitulo{n}.xhtml"
        if indice == 3:
            cuerpo = capitulo_enorme_html(cap_titulo, capitulo_gigante_kb * 1024)
        else:
            cuerpo = capitulo_html(cap_titulo, indice, repeticiones)
        archivos[f"OEBPS/{nombre}"] = xhtml(cap_titulo, cuerpo)
        manifest.append(f'    <item id="cap{n}" href="{nombre}" media-type="application/xhtml+xml"/>')
        spine.append(f'    <itemref idref="cap{n}"/>')
        nav_items.append(f'        <li><a href="{nombre}">{cap_titulo}</a></li>')
        ncx_points.append(
            f'    <navPoint id="np{n}" playOrder="{n + 1}"><navLabel><text>{cap_titulo}</text></navLabel>'
            f'<content src="{nombre}"/></navPoint>')

    portada_html = xhtml(titulo, f'  <section epub:type="cover"><img src="imagenes/portada.jpg" alt="Portada: {titulo}"/></section>')
    nav = xhtml("Índice", "  <nav epub:type=\"toc\" id=\"toc\">\n    <h1>Índice</h1>\n    <ol>\n"
                + "\n".join(nav_items) + "\n    </ol>\n  </nav>")
    opf = f"""<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid" xml:lang="es">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="uid">{identificador}</dc:identifier>
    <dc:title>{titulo}</dc:title>
    <dc:creator>Proyecto Folio XR</dc:creator>
    <dc:language>es</dc:language>
    <dc:rights>Contenido original de prueba del proyecto Folio XR.</dc:rights>
    <meta property="dcterms:modified">2026-11-05T00:00:00Z</meta>
    <meta name="cover" content="portada-img"/>
  </metadata>
  <manifest>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
    <item id="css" href="estilo.css" media-type="text/css"/>
    <item id="portada" href="portada.xhtml" media-type="application/xhtml+xml"/>
    <item id="portada-img" href="imagenes/portada.jpg" media-type="image/jpeg" properties="cover-image"/>
    <item id="puente" href="imagenes/puente.png" media-type="image/png"/>
{chr(10).join(manifest)}
  </manifest>
  <spine toc="ncx">
    <itemref idref="portada"/>
{chr(10).join(spine)}
  </spine>
</package>
"""
    ncx = f"""<?xml version="1.0" encoding="UTF-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1" xml:lang="es">
  <head><meta name="dtb:uid" content="{identificador}"/></head>
  <docTitle><text>{titulo}</text></docTitle>
  <navMap>
    <navPoint id="np0" playOrder="1"><navLabel><text>Portada</text></navLabel><content src="portada.xhtml"/></navPoint>
{chr(10).join(ncx_points)}
  </navMap>
</ncx>
"""
    with zipfile.ZipFile(ruta, "w") as z:
        # "mimetype" debe ser la primera entrada y sin comprimir.
        z.writestr(zip_info("mimetype", comprimir=False), "application/epub+zip")
        z.writestr(zip_info("META-INF/container.xml"), CONTAINER)
        z.writestr(zip_info("OEBPS/content.opf"), opf)
        z.writestr(zip_info("OEBPS/nav.xhtml"), nav)
        z.writestr(zip_info("OEBPS/toc.ncx"), ncx)
        z.writestr(zip_info("OEBPS/estilo.css"), CSS)
        z.writestr(zip_info("OEBPS/portada.xhtml"), portada_html)
        z.writestr(zip_info("OEBPS/imagenes/portada.jpg", comprimir=False), jpeg(portada))
        z.writestr(zip_info("OEBPS/imagenes/puente.png", comprimir=False), png(puente))
        for nombre, contenido in archivos.items():
            z.writestr(zip_info(nombre), contenido)


# ---------------------------------------------------------------------------
# TXT en varias codificaciones

def texto_txt():
    lineas = ["Folio XR — texto de prueba", ""]
    for titulo, _ in CAPITULOS:
        lineas.append(titulo)
        lineas.append("")
        lineas.extend(PARRAFOS)
        lineas.append("")
    return "\n".join(lineas) + "\n"


def generar_txt(carpeta):
    texto = texto_txt()
    salidas = {
        "texto-utf8.txt": texto.encode("utf-8"),
        "texto-utf8-bom.txt": b"\xef\xbb\xbf" + texto.encode("utf-8"),
        "texto-utf16le-bom.txt": b"\xff\xfe" + texto.encode("utf-16-le"),
        # Windows-1252 no tiene la raya «—»: se reemplaza por guion para poder codificarlo.
        "texto-windows1252.txt": texto.replace("—", "-").replace("\n", "\r\n").encode("cp1252"),
    }
    for nombre, datos in salidas.items():
        with open(os.path.join(carpeta, nombre), "wb") as f:
            f.write(datos)
    return list(salidas)


# ---------------------------------------------------------------------------

def main():
    raiz = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--salida", default=os.path.join(raiz, "tests", "fixtures"))
    args = parser.parse_args()
    os.makedirs(args.salida, exist_ok=True)

    generados = []
    generar_epub(os.path.join(args.salida, "libro-prueba.epub"), "El puente de papel",
                 "urn:uuid:5f0c1e2a-7d1b-4c3e-9a51-0f0110c1a001")
    generados.append("libro-prueba.epub")
    generar_cbz(os.path.join(args.salida, "comic-prueba.cbz"), "Comic de prueba", 8, manga=False)
    generados.append("comic-prueba.cbz")
    generar_cbz(os.path.join(args.salida, "manga-prueba-rtl.cbz"), "Manga de prueba", 8, manga=True)
    generados.append("manga-prueba-rtl.cbz")
    generados.extend(generar_txt(args.salida))

    for nombre in generados:
        tam = os.path.getsize(os.path.join(args.salida, nombre))
        print(f"{nombre:28s} {tam / 1024:8.1f} KiB")


if __name__ == "__main__":
    main()
