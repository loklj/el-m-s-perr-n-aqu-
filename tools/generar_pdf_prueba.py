#!/usr/bin/env python3
"""Genera el PDF de prueba del Hito 1 de Folio XR.

Texto y dibujos originales de este proyecto (sin derechos de terceros).
Uso:
    pip install reportlab
    python3 tools/generar_pdf_prueba.py hito1/Assets/StreamingAssets/folio-prueba.pdf
"""

import sys

from reportlab.lib.pagesizes import A5
from reportlab.lib.units import mm
from reportlab.pdfgen import canvas

ANCHO, ALTO = A5
MARGEN = 14 * mm
TOTAL = 8

PARRAFO = (
    "Este párrafo sirve para comprobar si el texto se lee bien dentro del visor. "
    "Si las letras se ven borrosas o tiemblan al mover la cabeza, anota a qué "
    "distancia estabas y con qué tamaño de letra ocurrió. Las tildes y la eñe "
    "(á, é, í, ó, ú, ñ, ü) también deben verse correctamente."
)


def ajustar(c, texto, fuente, tam, ancho):
    """Corta el texto en líneas que caben en el ancho dado."""
    palabras = texto.split()
    lineas, actual = [], ""
    for p in palabras:
        prueba = (actual + " " + p).strip()
        if c.stringWidth(prueba, fuente, tam) <= ancho:
            actual = prueba
        else:
            lineas.append(actual)
            actual = p
    if actual:
        lineas.append(actual)
    return lineas


def pie(c, n):
    c.setFont("Helvetica", 9)
    c.setFillGray(0.35)
    c.drawCentredString(ANCHO / 2, 9 * mm, f"Página {n} de {TOTAL}")
    c.setFillGray(0)


def titulo(c, texto, y):
    c.setFont("Helvetica-Bold", 16)
    c.drawString(MARGEN, y, texto)
    return y - 12 * mm


def bloque(c, texto, tam, y, fuente="Helvetica"):
    c.setFont(fuente, tam)
    for linea in ajustar(c, texto, fuente, tam, ANCHO - 2 * MARGEN):
        c.drawString(MARGEN, y, linea)
        y -= tam * 1.35
    return y - 4 * mm


def portada(c):
    c.setFillColorRGB(0.16, 0.35, 0.63)
    c.rect(0, ALTO * 0.55, ANCHO, ALTO * 0.45, stroke=0, fill=1)
    c.setFillColorRGB(1, 1, 1)
    c.setFont("Helvetica-Bold", 30)
    c.drawCentredString(ANCHO / 2, ALTO * 0.78, "Folio XR")
    c.setFont("Helvetica", 14)
    c.drawCentredString(ANCHO / 2, ALTO * 0.71, "PDF de prueba del Hito 1")
    c.setFillGray(0)
    y = ALTO * 0.45
    y = bloque(c, "Si ves esta portada dentro del Quest 3, el lector ya puede abrir un PDF "
                  "y dibujar sus páginas sobre el libro 3D.", 12, y)
    bloque(c, "Pulsa A o X para avanzar y B o Y para retroceder. Clic en el stick "
              "derecho vuelve a poner el libro frente a ti.", 11, y)
    pie(c, 1)


def pagina_tamanos(c, n, nombre, tamanos):
    y = titulo(c, nombre, ALTO - MARGEN - 6 * mm)
    for tam in tamanos:
        c.setFont("Helvetica-Bold", 10)
        c.drawString(MARGEN, y, f"{tam} puntos")
        y -= 3 * mm + tam  # deja espacio para la primera línea del bloque
        y = bloque(c, PARRAFO, tam, y)
    assert y > 16 * mm, f"La página {n} no cabe: y={y / mm:.1f} mm"
    pie(c, n)


def pagina_vinetas(c, n):
    y = titulo(c, "Viñetas tipo cómic", ALTO - MARGEN - 6 * mm)
    cols, filas = 2, 3
    gap = 4 * mm
    ancho = (ANCHO - 2 * MARGEN - gap) / cols
    alto = (y - 20 * mm - gap * (filas - 1)) / filas
    colores = [(0.95, 0.85, 0.55), (0.6, 0.8, 0.95), (0.8, 0.95, 0.7),
               (0.95, 0.7, 0.7), (0.85, 0.75, 0.95), (0.7, 0.9, 0.9)]
    i = 0
    for f in range(filas):
        for col in range(cols):
            x = MARGEN + col * (ancho + gap)
            yy = y - (f + 1) * alto - f * gap
            c.setFillColorRGB(*colores[i])
            c.setStrokeGray(0)
            c.setLineWidth(1.5)
            c.rect(x, yy, ancho, alto, stroke=1, fill=1)
            c.setFillGray(0)
            c.circle(x + ancho * 0.3, yy + alto * 0.45, alto * 0.18, stroke=1, fill=0)
            c.setFont("Helvetica-Bold", 11)
            c.drawString(x + 3 * mm, yy + alto - 6 * mm, f"Viñeta {i + 1}")
            i += 1
    pie(c, n)


def pagina_texto(c, n):
    y = titulo(c, f"Texto corrido — página {n}", ALTO - MARGEN - 6 * mm)
    y -= 4 * mm
    texto = (
        f"Esta es la página {n}. Sirve para probar el paso de páginas: al avanzar, "
        "el número de arriba y el del pie deben cambiar juntos. "
    ) + PARRAFO
    for _ in range(3):
        y = bloque(c, texto, 11, y)
    assert y > 16 * mm, f"La página {n} no cabe: y={y / mm:.1f} mm"
    pie(c, n)


def contraportada(c, n):
    y = titulo(c, "Fin del PDF de prueba", ALTO * 0.6)
    bloque(c, "Para probar con tu propio PDF, cópialo al visor con adb como "
              "libro.pdf, en la carpeta de datos de la app (ver docs/HITO1_GUIA.md).", 11, y)
    pie(c, n)


def main():
    salida = sys.argv[1] if len(sys.argv) > 1 else "folio-prueba.pdf"
    c = canvas.Canvas(salida, pagesize=A5)
    c.setTitle("Folio XR - PDF de prueba del Hito 1")
    c.setAuthor("Proyecto Folio XR")
    c.setSubject("Contenido original de prueba, sin derechos de terceros")

    portada(c)
    c.showPage()
    pagina_tamanos(c, 2, "Tamaños de letra", (8, 10, 12))
    c.showPage()
    pagina_vinetas(c, 3)
    c.showPage()
    pagina_tamanos(c, 4, "Letra grande", (14, 18))
    c.showPage()
    for n in range(5, TOTAL):
        pagina_texto(c, n)
        c.showPage()
    contraportada(c, TOTAL)
    c.showPage()
    c.save()
    print(f"PDF generado: {salida} ({TOTAL} páginas, A5)")


if __name__ == "__main__":
    main()
