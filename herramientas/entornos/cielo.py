# Cielo del panorama: base física de Blender + sol, nubes, estrellas y luna pintados con numpy.
import math
import numpy as np
from ruido import fbm, suave


def direcciones(W, H):
    """Dirección (Blender) de cada píxel; fila 0 = abajo. u = 0,5 + atan2(x, y)/2π."""
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    lon = (u - 0.5) * 2 * math.pi
    lat = (v - 0.5) * math.pi
    LON, LAT = np.meshgrid(lon, lat)
    return np.stack([np.sin(LON) * np.cos(LAT), np.cos(LON) * np.cos(LAT), np.sin(LAT)], -1)


def sol(t):
    a, e = math.radians(t['acimut']), math.radians(t['elev'])
    return np.array([math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)])  # Blender


def aces(x):
    x = np.maximum(x, 0)
    return np.clip(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14), 0, 1)


def a_srgb(lin_rgb, E):
    """Lineal de escena → 8 bits sRGB con exposición E y curva fílmica."""
    y = aces(lin_rgb * E)
    return (np.where(y <= 0.0031308, y * 12.92, 1.055 * np.power(np.maximum(y, 1e-9), 1 / 2.4) - 0.055) * 255 + 0.5).astype(np.uint8)


def _ampliar(img, W, H):
    import cv2
    return cv2.resize(img.astype(np.float32), (W, H), interpolation=cv2.INTER_CUBIC)


def cielo(t, base, W, H, E):
    """base: render del cielo de Blender sin disco solar (float, fila 0 abajo), o None de noche."""
    d = direcciones(W, H)
    s = sol(t)
    seed = t['semilla']
    cos = np.clip(d @ s, -1, 1)
    ang = np.degrees(np.arccos(cos))
    z = d[..., 2]
    tipo = t['cielo']
    scol = np.array(t['sol']) ** 2.2
    if tipo == 'noche':
        up = np.clip(z, 0, 1)[..., None]
        zen, hor = np.array([0.004, 0.007, 0.018]), np.array([0.016, 0.026, 0.055])
        if t['exterior'] == 'ciudad': hor = np.array([0.075, 0.05, 0.04])
        sky = (hor + (zen - hor) * up ** 0.5) / E
        L0 = 0.03 / E
    else:
        sky = np.maximum(_ampliar(base, W, H), 0)
        hb = base[base.shape[0] // 2 + 2: base.shape[0] // 2 + 12]
        L0 = float((hb @ np.array([0.2126, 0.7152, 0.0722])).mean())
    # ── Nubes sobre un plano: se aplanan hacia el horizonte ──
    cob = float(t.get('nubes', 0.3))
    k = 1.0 / (np.maximum(z, 0) + 0.07)
    px, py = d[..., 0] * k * 0.55, d[..., 1] * k * 0.55
    warp = fbm(px * 0.6 + 5, py * 0.6 - 3, 3, seed) - 0.5
    px2, py2 = px + warp * 0.9, py - warp * 0.7
    n = fbm(px2 + 11, py2 - 5, 7, seed + 1, 0.55)
    sp = s[:2] / (np.linalg.norm(s[:2]) + 1e-9) * 0.22
    n2 = fbm(px2 + 11 + sp[0], py2 - 5 + sp[1], 5, seed + 1, 0.55)
    alpha = suave(1 - cob - 0.06, 1 - cob + 0.2, n) * suave(0.0, 0.07, z)
    lit = suave(0.07, -0.07, n2 - n)
    base_n = suave(0.15, 0.5, alpha)       # centro denso: base más oscura
    if tipo == 'dia':
        c_lit, c_sh = L0 * 2.1 * np.array([1.0, 0.98, 0.95]), L0 * 0.8 * np.array([0.72, 0.78, 0.9])
    elif tipo == 'atardecer':
        c_lit, c_sh = L0 * 2.6 * np.array([1.0, 0.56, 0.3]), L0 * 0.42 * np.array([0.55, 0.45, 0.62])
    elif tipo == 'nublado':
        c_lit, c_sh = L0 * 1.25 * np.array([0.95, 0.96, 1.0]), L0 * 0.7 * np.array([0.8, 0.83, 0.9])
        alpha = np.maximum(alpha, 0.93 * suave(-0.02, 0.1, z))
    else:
        c_lit, c_sh = L0 * 1.4 * np.array([0.5, 0.55, 0.75]), L0 * 0.5 * np.array([0.35, 0.38, 0.5])
    hacia = (0.5 + 0.5 * cos)[..., None]
    shade = (lit * (1 - 0.45 * base_n))[..., None]
    ccol = c_sh + (c_lit - c_sh) * shade * (0.55 + 0.45 * hacia)
    if tipo in ('dia', 'atardecer'):
        cirros = 0.3 * suave(0.5, 0.78, fbm(px * 0.25 + 40, py * 1.3 + 7, 5, seed + 9)) * suave(0.03, 0.25, z)
        sky = sky + (c_lit * 0.7 - sky) * cirros[..., None]
    out = sky + (ccol - sky) * alpha[..., None]
    libre = (1 - 0.88 * alpha)[..., None]
    if tipo == 'noche':
        # Estrellas: puntos con brillo variado; vía láctea como franja difusa con más estrellas.
        rng = np.random.default_rng(seed)
        mv = np.array([0.45, 0.82, 0.35]); mv /= np.linalg.norm(mv)
        banda = np.exp(-((d @ mv) / 0.2) ** 2)
        via = banda * (0.35 + 0.65 * fbm(d[..., 0] * 7 + 3, d[..., 1] * 7 + d[..., 2] * 5, 5, seed + 4)) ** 2
        tenue = 0.25 if t['exterior'] == 'ciudad' else 1.0
        out += (np.array([0.05, 0.055, 0.075]) / E) * via[..., None] * libre * tenue * suave(0.0, 0.25, z)[..., None]
        ns = 9000
        v3 = rng.normal(size=(ns * 2, 3)); v3 /= np.linalg.norm(v3, axis=1, keepdims=True)
        v3 = v3[v3[:, 2] > 0.01]
        dens = 0.35 + 1.5 * np.exp(-((v3 @ mv) / 0.22) ** 2)
        v3 = v3[rng.random(len(v3)) < dens / 1.85][:ns]
        mag = rng.pareto(2.2, len(v3)) * 0.12 + 0.05
        col = np.array([1.0, 0.96, 0.9]) + rng.normal(scale=0.08, size=(len(v3), 3))
        u = 0.5 + np.arctan2(v3[:, 0], v3[:, 1]) / (2 * math.pi); vv = 0.5 + np.arcsin(v3[:, 2]) / math.pi
        xi = np.clip((u * W).astype(int), 1, W - 2); yi = np.clip((vv * H).astype(int), 1, H - 2)
        capa = np.zeros((H, W, 3), np.float32)
        b = (np.minimum(mag, 2.5)[:, None] * col * tenue * suave(0.01, 0.2, v3[:, 2])[:, None]).astype(np.float32)
        np.add.at(capa, (yi, xi), b)
        import cv2
        capa = cv2.GaussianBlur(capa, (0, 0), max(0.6, W / 8192 * 1.1)) * (2.2 * max(1.0, (W / 4096) ** 2))
        out += capa / E * libre
        luna = suave(0.62, 0.52, ang)
        mares = 0.78 + 0.22 * fbm(d[..., 0] * 260, d[..., 1] * 260 + d[..., 2] * 190, 3, seed)
        out += (np.array([0.9, 0.9, 0.84]) / E) * (luna * mares)[..., None] * libre
        out += (np.array([0.06, 0.07, 0.1]) / E) * (np.exp(-ang / 2.5) + 0.35 * np.exp(-ang / 12))[..., None] * libre
    elif tipo != 'nublado':
        disco = suave(0.33, 0.27, ang)[..., None]
        halo = (5.0 * np.exp(-ang / 1.0) + 1.1 * np.exp(-ang / 6.0) + 0.25 * np.exp(-ang / 25.0))[..., None]
        out += L0 * halo * scol * (0.35 + 0.65 * libre)
        out += L0 * 90.0 * disco * libre
    else:
        out += L0 * 0.5 * np.exp(-ang / 18.0)[..., None]
    if t['exterior'] == 'ciudad':
        out += (np.array([0.35, 0.2, 0.1]) * 0.12 / E) * (np.exp(-np.maximum(z, 0) / 0.09))[..., None]
    return out.astype(np.float32)


def bruma(tierra, alfa, dist, cielo_img, t, E):
    """Perspectiva aérea: lo lejano se funde con el color del horizonte en esa dirección."""
    H, W = alfa.shape
    fila = cielo_img[int(H * 0.5 + H * 0.012)]
    import cv2
    k = max(3, W // 16) | 1
    ancha = np.concatenate([fila[-k:], fila, fila[:k]])            # El panorama da la vuelta: borde circular.
    fila = cv2.blur(np.tile(ancha[None], (3, 1, 1)), (k, 1))[1][k:-k]
    alcance = dict(dia=9000.0, atardecer=5500.0, nublado=2600.0, noche=4000.0)[t['cielo']]
    if t['exterior'] in ('playa', 'isla'): alcance *= 1.6
    f = (1 - np.exp(-dist / alcance))[..., None]
    return tierra * (1 - f) + fila[None] * f * alfa[..., None]
