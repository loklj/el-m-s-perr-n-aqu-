#!/usr/bin/env python3
"""Generador de archivos gigantes para las pruebas de rendimiento de Folio XR (Fase 4).

Escribe en streaming (memoria acotada) y deja un manifiesto con tamaños y SHA-256.
Las salidas NO van a Git: por defecto se escriben en tools/test-data/salida/.

Ejemplos:
    # Todo con los tamaños por defecto (~0,5 GB en total):
    python3 tools/test-data/gigantes.py

    # Solo el PDF de 100.001 páginas:
    python3 tools/test-data/gigantes.py --solo pdf

    # Prueba rápida con tamaños pequeños:
    python3 tools/test-data/gigantes.py --pdf-paginas 2000 --txt-mb 5 --cbz-imagenes 50 --epub-kb 1024

    # Archivos de 5 GB (PDF de páginas con imágenes y CBZ con ZIP64):
    python3 tools/test-data/gigantes.py --solo pdf-gb --solo cbz-gb --gb 5 --limite-gb 12

    # TXT de 5 GB:
    python3 tools/test-data/gigantes.py --solo txt --txt-mb 5120 --limite-gb 6

Copiar al visor (ejemplo):
    adb push tools/test-data/salida/pdf-100001-paginas.pdf /sdcard/Download/

Contenido: texto e imágenes generados aquí, sin material de terceros.
"""

import argparse
import hashlib
import json
import os
import random
import shutil
import sys
import time
import zipfile

VERSION = "1.0"
AQUI = os.path.dirname(os.path.abspath(__file__))

PALABRAS = (
    "la el un una de del y en con por para sobre bajo entre hacia desde casa libro "
    "página lámpara ventana camino río puente ciudad noche mañana tarde lluvia viento "
    "papel tinta lectora lector historia memoria silencio voz carta mapa puerto tren "
    "mesa silla jardín árbol hoja piedra mar cielo luz sombra pregunta respuesta "
    "abrió cerró miró escribió leyó guardó encontró perdió volvió siguió pensó dijo "
    "lento rápido nuevo viejo pequeño grande claro oscuro tranquilo extraño único "
    "año día hora minuto señal niño niña pequeña búsqueda canción ñandú pingüino"
).split()


# ---------------------------------------------------------------------------
# Utilidades

def sha256(ruta):
    h = hashlib.sha256()
    with open(ruta, "rb") as f:
        for bloque in iter(lambda: f.read(4 * 1024 * 1024), b""):
            h.update(bloque)
    return h.hexdigest()


def progreso(etiqueta, hecho, total, inicio):
    if total <= 0:
        return
    pct = 100.0 * hecho / total
    seg = time.time() - inicio
    sys.stdout.write(f"\r  {etiqueta}: {pct:5.1f}%  ({seg:5.0f} s)")
    sys.stdout.flush()


# ---------------------------------------------------------------------------
# PDF con muchas páginas, escrito en streaming

def _pdf_texto(s):
    """Cadena literal PDF en WinAnsi (latin-1) con escapes."""
    s = s.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
    return s.encode("cp1252")


def generar_pdf(ruta, paginas, semilla):
    """PDF A5 con `paginas` páginas. Árbol de páginas de dos niveles (nodos de 500)."""
    rnd = random.Random(semilla)
    por_nodo = 500
    nodos = (paginas + por_nodo - 1) // por_nodo
    base = 4 + nodos  # primer objeto de página
    total_objetos = base + 2 * paginas  # 0..total-1
    offsets = [0] * total_objetos
    ancho, alto = 420, 595

    inicio = time.time()
    with open(ruta, "wb") as f:
        def obj(numero, cuerpo):
            offsets[numero] = f.tell()
            f.write(f"{numero} 0 obj\n".encode("ascii"))
            f.write(cuerpo)
            f.write(b"\nendobj\n")

        f.write(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
        obj(1, b"<< /Type /Catalog /Pages 2 0 R >>")
        kids = " ".join(f"{4 + n} 0 R" for n in range(nodos))
        obj(2, f"<< /Type /Pages /Kids [{kids}] /Count {paginas} >>".encode("ascii"))
        obj(3, b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
        for n in range(nodos):
            desde = n * por_nodo
            hasta = min(paginas, desde + por_nodo)
            hijos = " ".join(f"{base + 2 * i} 0 R" for i in range(desde, hasta))
            obj(4 + n, f"<< /Type /Pages /Parent 2 0 R /Kids [{hijos}] /Count {hasta - desde} >>".encode("ascii"))

        for i in range(paginas):
            num = i + 1
            frac = num / paginas
            palabras = " ".join(rnd.choice(PALABRAS) for _ in range(9))
            barra = max(1, int((ancho - 80) * frac))
            contenido = b"".join([
                b"0.16 0.35 0.63 rg 40 520 %d 14 re f\n" % barra,  # barra de progreso
                b"0 g BT /F1 30 Tf 40 450 Td (" + _pdf_texto(f"Página {num:,}".replace(",", ".")) + b") Tj ET\n",
                b"BT /F1 12 Tf 40 420 Td (" + _pdf_texto(f"de {paginas:,} - {frac * 100:.3f} %".replace(",", ".")) + b") Tj ET\n",
                b"BT /F1 11 Tf 40 380 Td (" + _pdf_texto(palabras) + b") Tj ET\n",
                b"BT /F1 9 Tf 40 40 Td (" + _pdf_texto(f"Folio XR - prueba de estrés - semilla {semilla}") + b") Tj ET\n",
            ])
            nodo = 4 + i // por_nodo
            pagina_obj = base + 2 * i
            obj(pagina_obj, (f"<< /Type /Page /Parent {nodo} 0 R /MediaBox [0 0 {ancho} {alto}] "
                             f"/Resources << /Font << /F1 3 0 R >> >> /Contents {pagina_obj + 1} 0 R >>").encode("ascii"))
            obj(pagina_obj + 1, b"<< /Length %d >>\nstream\n" % len(contenido) + contenido + b"endstream")
            if num % 2000 == 0 or num == paginas:
                progreso("PDF", num, paginas, inicio)

        xref = f.tell()
        f.write(f"xref\n0 {total_objetos}\n".encode("ascii"))
        f.write(b"0000000000 65535 f \n")
        for n in range(1, total_objetos):
            f.write(b"%010d 00000 n \n" % offsets[n])
        f.write(f"trailer\n<< /Size {total_objetos} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode("ascii"))
    print()
    return {"paginas": paginas}


# ---------------------------------------------------------------------------
# TXT de cientos de MB

def generar_txt(ruta, megas, codificacion, semilla):
    rnd = random.Random(semilla)
    objetivo = int(megas * 1024 * 1024)
    escritos = 0
    capitulo = 0
    inicio = time.time()
    bom = {"utf-16": b"\xff\xfe"}.get(codificacion, b"")
    codec = {"utf-16": "utf-16-le"}.get(codificacion, codificacion)
    salto = "\r\n" if codificacion == "cp1252" else "\n"
    with open(ruta, "wb") as f:
        f.write(bom)
        escritos += len(bom)
        while escritos < objetivo:
            capitulo += 1
            partes = [f"Capítulo {capitulo}{salto}{salto}"]
            for _ in range(rnd.randint(25, 45)):
                n = rnd.randint(40, 110)
                frase = " ".join(rnd.choice(PALABRAS) for _ in range(n))
                partes.append(frase[0].upper() + frase[1:] + "." + salto + salto)
            datos = "".join(partes).encode(codec)
            f.write(datos)
            escritos += len(datos)
            if capitulo % 50 == 0:
                progreso(f"TXT {codificacion}", escritos, objetivo, inicio)
    progreso(f"TXT {codificacion}", objetivo, objetivo, inicio)
    print()
    return {"capitulos": capitulo, "codificacion": codificacion}


# ---------------------------------------------------------------------------
# CBZ con miles de imágenes

def generar_cbz(ruta, imagenes, semilla):
    from PIL import Image, ImageDraw  # import tardío: solo hace falta para CBZ
    import io
    rnd = random.Random(semilla)
    inicio = time.time()
    with zipfile.ZipFile(ruta, "w", allowZip64=True) as z:
        for n in range(1, imagenes + 1):
            color = (rnd.randint(30, 200), rnd.randint(30, 200), rnd.randint(30, 200))
            img = Image.new("RGB", (900, 1350), (250, 248, 242))
            d = ImageDraw.Draw(img)
            d.rectangle([40, 40, 860, 160], fill=color)
            for k in range(rnd.randint(3, 6)):
                x0, y0 = rnd.randint(60, 600), rnd.randint(220, 1050)
                d.rectangle([x0, y0, x0 + rnd.randint(120, 240), y0 + rnd.randint(120, 240)],
                            outline=(20, 20, 20), width=5)
            # Número grande dibujado con barras: no depende de fuentes del sistema.
            ancho_barra = int(820 * n / imagenes)
            d.rectangle([40, 1250, 40 + max(4, ancho_barra), 1290], fill=color)
            d.text((50, 1300), f"{n} / {imagenes}", fill=(40, 40, 40))
            buf = io.BytesIO()
            img.save(buf, "JPEG", quality=75)
            info = zipfile.ZipInfo(f"{n:05d}.jpg", date_time=(2026, 11, 5, 0, 0, 0))
            info.compress_type = zipfile.ZIP_STORED  # JPEG ya está comprimido
            z.writestr(info, buf.getvalue())
            if n % 50 == 0 or n == imagenes:
                progreso("CBZ", n, imagenes, inicio)
    print()
    return {"imagenes": imagenes}


# ---------------------------------------------------------------------------
# Archivos de varios GB (páginas con imágenes que no se pueden comprimir)

def _pool_jpeg(semilla, cantidad=8, ancho=1000, alto=1414, calidad=80):
    """Imágenes de ruido (~1 MB cada una) para alcanzar tamaños grandes rápido."""
    from PIL import Image
    import io
    rnd = random.Random(semilla)
    pool = []
    for _ in range(cantidad):
        img = Image.frombytes("RGB", (ancho, alto), rnd.randbytes(ancho * alto * 3))
        buf = io.BytesIO()
        img.save(buf, "JPEG", quality=calidad)
        pool.append(buf.getvalue())
    return pool, ancho, alto


def generar_pdf_gb(ruta, gb, semilla):
    """PDF de `gb` GiB: cada página es una imagen de ~1 MB con su número encima.

    Comprueba que el lector use desplazamientos de 64 bits (el archivo supera 4 GiB).
    """
    pool, iw, ih = _pool_jpeg(semilla)
    objetivo = int(gb * 1024 ** 3)
    media = sum(len(j) for j in pool) / len(pool)
    paginas = max(1, int(objetivo / (media + 700)) + 1)
    por_nodo = 500
    nodos = (paginas + por_nodo - 1) // por_nodo
    base = 4 + nodos
    total_objetos = base + 3 * paginas
    offsets = [0] * total_objetos
    ancho, alto = 420, 595

    inicio = time.time()
    with open(ruta, "wb") as f:
        def obj(numero, *partes):
            offsets[numero] = f.tell()
            f.write(f"{numero} 0 obj\n".encode("ascii"))
            for parte in partes:
                f.write(parte)
            f.write(b"\nendobj\n")

        f.write(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
        obj(1, b"<< /Type /Catalog /Pages 2 0 R >>")
        kids = " ".join(f"{4 + n} 0 R" for n in range(nodos))
        obj(2, f"<< /Type /Pages /Kids [{kids}] /Count {paginas} >>".encode("ascii"))
        obj(3, b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
        for n in range(nodos):
            desde, hasta = n * por_nodo, min(paginas, (n + 1) * por_nodo)
            hijos = " ".join(f"{base + 3 * i} 0 R" for i in range(desde, hasta))
            obj(4 + n, f"<< /Type /Pages /Parent 2 0 R /Kids [{hijos}] /Count {hasta - desde} >>".encode("ascii"))

        for i in range(paginas):
            num = i + 1
            pagina, contenido_n, imagen_n = base + 3 * i, base + 3 * i + 1, base + 3 * i + 2
            jpeg = pool[i % len(pool)]
            contenido = (b"q %d 0 0 %d 0 0 cm /Im Do Q\n" % (ancho, alto)
                         + b"1 g 30 500 360 60 re f 0 g BT /F1 28 Tf 40 520 Td ("
                         + _pdf_texto(f"Página {num:,} de {paginas:,}".replace(",", ".")) + b") Tj ET\n")
            obj(pagina, (f"<< /Type /Page /Parent {4 + i // por_nodo} 0 R /MediaBox [0 0 {ancho} {alto}] "
                         f"/Resources << /Font << /F1 3 0 R >> /XObject << /Im {imagen_n} 0 R >> >> "
                         f"/Contents {contenido_n} 0 R >>").encode("ascii"))
            obj(contenido_n, b"<< /Length %d >>\nstream\n" % len(contenido), contenido, b"endstream")
            obj(imagen_n, (f"<< /Type /XObject /Subtype /Image /Width {iw} /Height {ih} /ColorSpace /DeviceRGB "
                           f"/BitsPerComponent 8 /Filter /DCTDecode /Length {len(jpeg)} >>\nstream\n").encode("ascii"),
                jpeg, b"\nendstream")
            if num % 100 == 0 or num == paginas:
                progreso("PDF GB", num, paginas, inicio)

        xref = f.tell()
        f.write(f"xref\n0 {total_objetos}\n".encode("ascii"))
        f.write(b"0000000000 65535 f \n")
        for n in range(1, total_objetos):
            f.write(b"%010d 00000 n \n" % offsets[n])
        f.write(f"trailer\n<< /Size {total_objetos} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode("ascii"))
    print()
    return {"paginas": paginas, "supera_4_gib": os.path.getsize(ruta) > 4 * 1024 ** 3}


def generar_cbz_gb(ruta, gb, semilla):
    """CBZ de `gb` GiB con imágenes de ~1 MB. Usa ZIP64 automáticamente al pasar de 4 GiB."""
    pool, _, _ = _pool_jpeg(semilla + 1)
    objetivo = int(gb * 1024 ** 3)
    escritos, n = 0, 0
    inicio = time.time()
    with zipfile.ZipFile(ruta, "w", allowZip64=True) as z:
        while escritos < objetivo:
            n += 1
            datos = pool[n % len(pool)]
            info = zipfile.ZipInfo(f"{n:05d}.jpg", date_time=(2026, 11, 5, 0, 0, 0))
            info.compress_type = zipfile.ZIP_STORED
            z.writestr(info, datos)
            escritos += len(datos) + 100
            if n % 200 == 0:
                progreso("CBZ GB", escritos, objetivo, inicio)
    progreso("CBZ GB", objetivo, objetivo, inicio)
    print()
    return {"imagenes": n, "zip64": os.path.getsize(ruta) > 4 * 1024 ** 3}


# ---------------------------------------------------------------------------
# EPUB con un capítulo enorme

def generar_epub(ruta, kb):
    sys.path.insert(0, AQUI)
    import fixtures  # mismo directorio
    fixtures.generar_epub(ruta, "Capítulo enorme", "urn:uuid:5f0c1e2a-7d1b-4c3e-9a51-0f0110c1a0ff",
                          capitulo_gigante_kb=kb)
    return {"capitulo_enorme_kb": kb}


# ---------------------------------------------------------------------------

def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--salida", default=os.path.join(AQUI, "salida"))
    p.add_argument("--semilla", type=int, default=20261105)
    p.add_argument("--solo", choices=["pdf", "txt", "cbz", "epub", "pdf-gb", "cbz-gb"], action="append",
                   help="Generar solo estos tipos (se puede repetir). pdf-gb y cbz-gb solo se generan si se piden.")
    p.add_argument("--gb", type=float, default=5, help="Tamaño en GiB de pdf-gb y cbz-gb.")
    p.add_argument("--pdf-paginas", type=int, default=100_001)
    p.add_argument("--txt-mb", type=float, default=300)
    p.add_argument("--txt-codificaciones", default="utf-8",
                   help="Lista separada por comas: utf-8, utf-16, cp1252.")
    p.add_argument("--cbz-imagenes", type=int, default=3000)
    p.add_argument("--epub-kb", type=int, default=30_000, help="Tamaño del capítulo enorme en KiB.")
    p.add_argument("--limite-gb", type=float, default=12.0,
                   help="No generar si el total estimado supera este límite.")
    a = p.parse_args()

    tipos = a.solo or ["pdf", "txt", "cbz", "epub"]
    codificaciones = [c.strip() for c in a.txt_codificaciones.split(",") if c.strip()]
    for c in codificaciones:
        if c not in ("utf-8", "utf-16", "cp1252"):
            p.error(f"codificación no admitida: {c}")

    # Estimación conservadora para no llenar el disco.
    estimado = 0
    if "pdf" in tipos:
        estimado += a.pdf_paginas * 700
    if "txt" in tipos:
        estimado += int(a.txt_mb * 1024 * 1024) * len(codificaciones)
    if "cbz" in tipos:
        estimado += a.cbz_imagenes * 90_000
    if "epub" in tipos:
        estimado += a.epub_kb * 1024 // 3
    for grande in ("pdf-gb", "cbz-gb"):
        if grande in tipos:
            estimado += int(a.gb * 1024 ** 3 * 1.01)
    os.makedirs(a.salida, exist_ok=True)
    libre = shutil.disk_usage(a.salida).free
    print(f"Estimado: {estimado / 1e9:.2f} GB | libre: {libre / 1e9:.1f} GB | límite: {a.limite_gb} GB")
    if estimado > a.limite_gb * 1e9:
        sys.exit("Supera --limite-gb. Reduce los tamaños o sube el límite.")
    if estimado + 1e9 > libre:
        sys.exit("No hay espacio suficiente (se deja 1 GB de margen).")

    archivos = []

    def registrar(nombre, datos):
        ruta = os.path.join(a.salida, nombre)
        print(f"  calculando SHA-256 de {nombre}...")
        datos.update({"archivo": nombre, "bytes": os.path.getsize(ruta), "sha256": sha256(ruta)})
        archivos.append(datos)

    if "pdf" in tipos:
        nombre = f"pdf-{a.pdf_paginas}-paginas.pdf"
        registrar(nombre, generar_pdf(os.path.join(a.salida, nombre), a.pdf_paginas, a.semilla))
    if "txt" in tipos:
        for c in codificaciones:
            nombre = f"txt-{a.txt_mb:g}mb-{c}.txt"
            registrar(nombre, generar_txt(os.path.join(a.salida, nombre), a.txt_mb, c, a.semilla))
    if "cbz" in tipos:
        nombre = f"cbz-{a.cbz_imagenes}-imagenes.cbz"
        registrar(nombre, generar_cbz(os.path.join(a.salida, nombre), a.cbz_imagenes, a.semilla))
    if "epub" in tipos:
        nombre = f"epub-capitulo-{a.epub_kb}kb.epub"
        registrar(nombre, generar_epub(os.path.join(a.salida, nombre), a.epub_kb))
    if "pdf-gb" in tipos:
        nombre = f"pdf-{a.gb:g}gb-imagenes.pdf"
        registrar(nombre, generar_pdf_gb(os.path.join(a.salida, nombre), a.gb, a.semilla))
    if "cbz-gb" in tipos:
        nombre = f"cbz-{a.gb:g}gb.cbz"
        registrar(nombre, generar_cbz_gb(os.path.join(a.salida, nombre), a.gb, a.semilla))

    manifiesto = {
        "generador": "tools/test-data/gigantes.py",
        "version": VERSION,
        "semilla": a.semilla,
        "parametros": {k: v for k, v in vars(a).items() if k not in ("salida",)},
        "archivos": archivos,
        "nota": "RAR sólido no incluido: requiere la herramienta propietaria 'rar'.",
    }
    with open(os.path.join(a.salida, "manifiesto.json"), "w", encoding="utf-8") as f:
        json.dump(manifiesto, f, ensure_ascii=False, indent=2)
    print(f"Listo. Manifiesto: {os.path.join(a.salida, 'manifiesto.json')}")


if __name__ == "__main__":
    main()
