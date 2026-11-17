"""Suelo caminable y límite de cada entorno horneado (Claude, 17 nov 2026).

Antes el suelo físico era una tabla plana de 120 m a la altura del piso de la casa: donde el terreno
dibujado sube (dunas, lomas) la persona quedaba enterrada, donde baja quedaba flotando, y podía caminar
más allá del terreno cercano (34 m), donde solo queda el panorama: en el desierto se veía una franja
oscura cruzando el suelo. Este paso lee el terreno cercano ya exportado y escribe, para cada tema:

- suelo_malla.bytes: una grilla de 0,5 m (formato FXM1, sin UV) con la altura del terreno; Unity la usa
  como colisionador (se camina sobre las dunas y se puede teletransportar a ellas). Sobre el agua queda a la
  altura del agua, pero no se llega: el límite corta antes de la orilla.
- datos.json «limite»: 72 radios (uno cada 5°, empezando en +Z y girando hacia +X) hasta donde se puede
  caminar. Unity levanta ahí una pared invisible. Se detiene a 8 m del borde del terreno cercano y 1 m antes
  del agua.

Uso: python3 suelo.py [tema ...]   (sin temas: todos). No necesita Blender.
"""
import json
import os
import struct
import sys

import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.normpath(os.path.join(AQUI, '..', '..', 'hito1', 'Assets', 'FolioXR', 'Resources', 'Entornos'))
GY = -0.28           # Piso de la casa (Unity).
AGUA_Y = -0.83       # Superficie del agua (GY + paisaje.AGUA).
PASO = 0.5           # Lado de la grilla, en metros.
ANGULOS = 72
MARGEN_BORDE = 8.0   # Distancia al borde del terreno cercano que no se puede pasar.
TOPE = 26.0          # Radio máximo caminable.


def leer(path):
    d = open(path, 'rb').read()
    nv, ni = struct.unpack('<II', d[4:12])
    pos = np.frombuffer(d, '<f4', nv * 3, 12).reshape(-1, 3)
    idx = np.frombuffer(d, '<u4', ni, 12 + nv * 20).reshape(-1, 3)
    return pos, idx


def escribir(path, pos, idx):
    with open(path, 'wb') as f:
        f.write(b'FXM1')
        f.write(np.array([len(pos), idx.size], np.uint32).tobytes())
        f.write(pos.astype(np.float32).tobytes())
        f.write(np.zeros((len(pos), 2), np.float32).tobytes())
        f.write(idx.astype(np.uint32).tobytes())


def alturas(pos, idx, borde):
    """Altura del terreno en cada punto de la grilla: la superficie más baja que mira hacia arriba."""
    n = int(np.ceil((borde + 1.0) / PASO))
    eje = np.arange(-n, n + 1) * PASO
    H = np.full((len(eje), len(eje)), np.inf, np.float32)     # H[iz, ix]
    T = pos[idx]
    nrm = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
    area = np.linalg.norm(nrm, axis=1)
    arriba = (np.abs(nrm[:, 1]) > 0.5 * area) & (area > 1e-6)
    for t in T[arriba]:
        x0, z0 = t[:, 0].min(), t[:, 2].min()
        x1, z1 = t[:, 0].max(), t[:, 2].max()
        ix0, ix1 = int(np.ceil(x0 / PASO)) + n, int(np.floor(x1 / PASO)) + n
        iz0, iz1 = int(np.ceil(z0 / PASO)) + n, int(np.floor(z1 / PASO)) + n
        ix0, iz0 = max(ix0, 0), max(iz0, 0)
        ix1, iz1 = min(ix1, len(eje) - 1), min(iz1, len(eje) - 1)
        if ix0 > ix1 or iz0 > iz1:
            continue
        gx, gz = np.meshgrid(eje[ix0:ix1 + 1], eje[iz0:iz1 + 1])
        a, b, c = t[0], t[1], t[2]
        den = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(den) < 1e-12:
            continue
        l1 = ((b[2] - c[2]) * (gx - c[0]) + (c[0] - b[0]) * (gz - c[2])) / den
        l2 = ((c[2] - a[2]) * (gx - c[0]) + (a[0] - c[0]) * (gz - c[2])) / den
        l3 = 1 - l1 - l2
        dentro = (l1 >= -1e-4) & (l2 >= -1e-4) & (l3 >= -1e-4)
        if not dentro.any():
            continue
        y = l1 * a[1] + l2 * b[1] + l3 * c[1]
        blk = H[iz0:iz1 + 1, ix0:ix1 + 1]
        np.minimum(blk, np.where(dentro, y, np.inf), out=blk)
    return eje, H


def mediana(H, r=2):
    p = np.pad(H, r, mode='edge')
    vs = [p[r + dz:r + dz + H.shape[0], r + dx:r + dx + H.shape[1]] for dz in range(-r, r + 1) for dx in range(-r, r + 1)]
    return np.median(np.stack(vs), axis=0)


def limpiar(H, eje):
    """Rellena huecos (bajo la casa, bajo rocas) y baja los picos sueltos (hojas, copas sin suelo debajo).
    Bajo la casa queda 5 cm por debajo del piso: ahí se camina sobre los pisos de la casa, que tienen su
    propio colisionador, y el terreno no debe asomar entre ellos."""
    falta = ~np.isfinite(H)
    H = np.where(falta, np.nan, H)
    for _ in range(200):
        if not np.isnan(H).any():
            break
        p = np.pad(H, 1, mode='edge')
        vecinos = np.stack([p[1:-1, :-2], p[1:-1, 2:], p[:-2, 1:-1], p[2:, 1:-1]])
        cuenta = (~np.isnan(vecinos)).sum(0)
        suma = np.nansum(vecinos, 0)
        rellena = np.isnan(H) & (cuenta > 0)
        H[rellena] = suma[rellena] / cuenta[rellena]
    H = np.where(np.isnan(H), GY, H)
    med = mediana(H, 2)
    pico = H - med > 0.35
    H = np.where(pico, med, H)
    gx, gz = np.meshgrid(eje, eje)
    casa = falta & (np.hypot(gx, gz) < 15.0)
    H = np.where(casa, np.minimum(H, GY - 0.05), H)
    return H.astype(np.float32), int(falta.sum()), int(pico.sum())


def tema(slug):
    d = os.path.join(SALIDA, slug)
    pos, idx = leer(os.path.join(d, 'cerca_malla.bytes'))
    borde = float(np.hypot(pos[:, 0], pos[:, 2]).max())
    eje, H = alturas(pos, idx, borde)
    H, huecos, picos = limpiar(H, eje)
    con_agua = os.path.exists(os.path.join(d, 'agua_malla.bytes'))
    if con_agua:
        H = np.maximum(H, AGUA_Y)
    n = len(eje)
    tope = min(TOPE, borde - MARGEN_BORDE)

    def altura(x, z):
        ix = int(round(x / PASO)) + (n - 1) // 2
        iz = int(round(z / PASO)) + (n - 1) // 2
        if ix < 0 or iz < 0 or ix >= n or iz >= n:
            return None
        return float(H[iz, ix])

    radios = []
    for k in range(ANGULOS):
        a = 2 * np.pi * k / ANGULOS
        dx, dz = np.sin(a), np.cos(a)
        r = 3.0
        while r < tope:
            h = altura(dx * r, dz * r)
            if h is None or (con_agua and h <= AGUA_Y + 0.03):
                break
            r += 0.25
        radios.append(round(max(6.0, (r - 1.0) if r < tope else tope), 2))

    # Grilla hasta 1,5 m por fuera del límite más lejano (más allá no se llega).
    gx, gz = np.meshgrid(eje, eje)
    pts = np.stack([gx.ravel(), H.ravel(), gz.ravel()], 1)
    usar = (np.hypot(gx, gz) <= max(radios) + 2.0).ravel()
    quad = []
    for iz in range(n - 1):
        for ix in range(n - 1):
            a = iz * n + ix
            b, c, e = a + 1, a + n, a + n + 1
            if usar[a] and usar[b] and usar[c] and usar[e]:
                quad.append((a, c, e, a, e, b))      # Hacia arriba en Unity (mano izquierda).
    quad = np.array(quad, np.int64).reshape(-1, 3)
    vivos, inv = np.unique(quad, return_inverse=True)
    escribir(os.path.join(d, 'suelo_malla.bytes'), pts[vivos], inv.reshape(-1, 3))
    path = os.path.join(d, 'datos.json')
    datos = json.load(open(path, encoding='utf-8'))
    datos['limite'] = radios
    json.dump(datos, open(path, 'w'), indent=1)
    print(f'{slug}: suelo {len(vivos)} puntos, {len(quad)} triángulos; huecos {huecos}, picos {picos}; '
          f'límite {min(radios):.1f}–{max(radios):.1f} m; alturas {H[np.hypot(gx, gz) < max(radios)].min():.2f} '
          f'a {H[np.hypot(gx, gz) < max(radios)].max():.2f}')


if __name__ == '__main__':
    temas = sys.argv[1:] or sorted(t for t in os.listdir(SALIDA) if os.path.isdir(os.path.join(SALIDA, t)))
    for t in temas:
        tema(t)
