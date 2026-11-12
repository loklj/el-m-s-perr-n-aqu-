# Los once estilos, copiados de EnvironmentTheme.cs (si cambian allá, cambiar aquí).
# Colores en sRGB. acimut: 270 = frente de la casa (−Z de Unity). cielo: dia, atardecer, noche, nublado.
def h(x):
    return ((x >> 16 & 255) / 255, (x >> 8 & 255) / 255, (x & 255) / 255)


def tema(slug, nombre, semilla, pared, madera, tela, metal, follaje, exterior, cielo, acimut, elev,
         sol=(1.0, 0.96, 0.88), fuerza=1.1, nubes=0.35, ancho=12.0, fondo=10.0, alto=3.15, muebles=1.0, **mas):
    d = dict(slug=slug, nombre=nombre, semilla=semilla, pared=pared, madera=madera, tela=tela, metal=metal,
             follaje=follaje, exterior=exterior, cielo=cielo, acimut=acimut, elev=elev, sol=sol, fuerza=fuerza,
             nubes=nubes, ancho=ancho, fondo=fondo, alto=alto, muebles=muebles)
    d.update(mas)
    return d


TEMAS = [
    tema('moderna-calida', 'Moderna cálida', 58241, (0.94, 0.89, 0.80), (0.80, 0.64, 0.43), (0.68, 0.42, 0.29),
         (0.20, 0.23, 0.22), (0.38, 0.56, 0.32), 'jardin', 'dia', 235, 42, montes=9,
         suelo=h(0x55782F), suelo2=h(0x7A8A45), roca=h(0x787A80), hojas=[h(0x3F6B2E), h(0x4F7D36), h(0x35592A), h(0x5E8A3C)]),
    tema('refugio-alpino', 'Refugio alpino', 7713, h(0xE8DCC8), h(0x9A6B43), h(0x8E3B2E), h(0x2B2B2D), h(0x2F5A35),
         'montanas', 'dia', 300, 35, sol=h(0xFFF6E6), fuerza=1.2, nubes=0.25, ancho=12.5, alto=3.3, libros='oscuros',
         montes=42, suelo=h(0x55703A), suelo2=h(0x7A8048), roca=h(0x6E7178), nieve=h(0xF4F7FB),
         hojas=[h(0x1F4429), h(0x264D2E), h(0x2D5733), h(0x1C3B26)]),
    tema('casa-del-lago', 'Casa del lago', 4402, h(0xEFE6D6), h(0xB9895A), h(0x3F5E6E), h(0x272A2C), h(0x4C6B34),
         'lago', 'atardecer', 262, 5, sol=h(0xFFB070), fuerza=0.9, nubes=0.4, montes=20, lamparas=1.0,
         suelo=h(0x4D6630), suelo2=h(0x6B7238), roca=h(0x5A5866), agua=h(0x23455A),
         hojas=[h(0x2E4A24), h(0x3A5A2A), h(0x23402A), h(0x445E2C)]),
    tema('casa-de-playa', 'Casa de playa', 9120, h(0xF6F2EA), h(0xD8C3A0), h(0x5C8FA8), h(0x7E8488), h(0x4F8A3E),
         'playa', 'dia', 210, 55, fuerza=1.25, nubes=0.2, ancho=13.0, montes=12,
         suelo=h(0xE2D2AC), suelo2=h(0xD3C095), roca=h(0x8F8577), agua=h(0x1D7F98),
         hojas=[h(0x3F7A32), h(0x4E8A3A), h(0x5E9640), h(0x36692C)]),
    tema('cabana-nevada', 'Cabaña nevada', 3317, h(0xD9C7A8), h(0x7A4E2E), h(0x7E2A26), h(0x1F1F21), h(0x2E4F37),
         'nieve', 'nublado', 250, 25, sol=h(0xE6ECF5), fuerza=0.65, nubes=0.85, alto=3.0, libros='oscuros', montes=30,
         lamparas=0.6, suelo=h(0xECF0F5), suelo2=h(0xDCE2EA), roca=h(0x5C6068), nieve=h(0xF6F8FB),
         hojas=[h(0x1E3A2A), h(0x24422E), h(0x1A3326)]),
    tema('casa-del-desierto', 'Casa del desierto', 6608, h(0xE9D5B8), h(0xA9774A), h(0xB45A35), h(0x3A302A), h(0x6E8A4A),
         'desierto', 'atardecer', 285, 12, sol=h(0xFFC488), fuerza=1.05, nubes=0.12, ancho=14.0, fondo=11.0, montes=26,
         suelo=h(0xC99A68), suelo2=h(0xB98856), roca=h(0xA0583A), nieve=h(0xC8805A),
         hojas=[h(0x5E7A40), h(0x6E8A4A), h(0x56703C), h(0x7A9454)]),
    tema('casa-del-bosque', 'Casa del bosque', 1189, h(0xE3D7C2), h(0x8C6239), h(0x5B6B3A), h(0x232521), h(0x3C6B30),
         'bosque', 'dia', 200, 48, fuerza=1.05, nubes=0.45, muebles=1.2, libros='oscuros', montes=16,
         suelo=h(0x3F5A2A), suelo2=h(0x5A5230), roca=h(0x5A5E58),
         hojas=[h(0x2C5A26), h(0x3A6A2E), h(0x24482A), h(0x4A7434), h(0x1E3F25), h(0x355F2A)]),
    tema('otono-en-el-valle', 'Otoño en el valle', 2471, h(0xF0E4D0), h(0xA06A3C), h(0x9C4A28), h(0x2E2A26), h(0xB0602A),
         'otono', 'dia', 240, 28, sol=h(0xFFE6C0), fuerza=1.05, nubes=0.4, montes=18,
         suelo=h(0x6E6A34), suelo2=h(0x9A6A30), roca=h(0x6A6460),
         hojas=[h(0xC4501E), h(0xD9822A), h(0xE0B03A), h(0x9C3A1C), h(0xB8662A), h(0x6E7A30)]),
    tema('loft-nocturno', 'Loft nocturno', 8034, h(0xCFC8BE), h(0x5C4030), h(0x34405A), h(0x18191C), h(0x3A5A34),
         'ciudad', 'noche', 300, 35, sol=h(0x9AAAD0), fuerza=0.35, nubes=0.15, ancho=13.0, libros='oscuros', montes=0,
         lamparas=1.0, suelo=h(0x2E3A2A), suelo2=h(0x3A3A36), roca=h(0x303236),
         hojas=[h(0x1E3020), h(0x253A26), h(0x1A2A1C)]),
    tema('isla-tropical', 'Isla tropical', 5150, h(0xF4EEE2), h(0xC49A66), h(0x2E8C80), h(0x585C5E), h(0x3E8A3A),
         'isla', 'dia', 250, 60, fuerza=1.3, nubes=0.3, montes=14,
         suelo=h(0x5A8A3A), suelo2=h(0xE6D6AE), roca=h(0x6A6A5E), agua=h(0x1690A8),
         hojas=[h(0x3A8034), h(0x4C9238), h(0x2E6E2C), h(0x5AA040)]),
    tema('noche-estrellada', 'Noche estrellada', 9931, h(0xE6DCCB), h(0x8E6844), h(0x4A3A5C), h(0x202024), h(0x2E4A2A),
         'jardin', 'noche', 255, 30, sol=h(0xA8B8E0), fuerza=0.4, nubes=0.08, libros='oscuros', montes=12, lamparas=1.0,
         suelo=h(0x24361E), suelo2=h(0x2E3A22), roca=h(0x2A2C34),
         hojas=[h(0x182A18), h(0x1E321C), h(0x142416)]),
]
POR_SLUG = {t['slug']: t for t in TEMAS}
