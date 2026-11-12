# Paisaje alrededor de la casa: terreno con relieve real (hasta varios kilómetros), agua, árboles,
# arbustos, rocas y pasto. Lo cercano (menos de 34 m) se hornea y se exporta como malla; lo lejano
# solo aparece en el panorama del cielo.
import math
import numpy as np
import bpy
import bmesh
from fx import Geo, T, Sc, FromUp, Ry, SWAP
from ruido import fbm, suave
from materiales import NT, lin, mul, lerp, sep

GY = -0.28      # Suelo de la casa en Unity (cara superior del colisionador).
AGUA = -0.55    # Nivel del agua respecto al suelo.
CERCA = 34.0    # Radio de la malla horneada.
LEJOS = 4500.0


def _norm(v):
    return v / (np.linalg.norm(v) + 1e-12)


class Paisaje:
    def __init__(self, t):
        self.t, self.k, self.seed = t, t['exterior'], t['semilla']
        self.sx, self.sz = t['ancho'] / 12, t['fondo'] / 10
        self.F = 15.0 * max(self.sx, self.sz)
        self.M = float(t.get('montes', 10))
        self.rng = np.random.default_rng(self.seed + 7)
        self.pal = [lin(c) for c in t['hojas']]
        self.con_agua = self.k in ('lago', 'playa', 'isla')
        self.g = Geo((1, 1))        # cerca: se hornea y exporta
        self.lejos = Geo((1, 1))    # solo panorama
        self.instancias = []        # (objeto variante, posiciones, escalas)

    # ───────────── Relieve ─────────────
    def altura(self, x, z):
        x = np.asarray(x, float); z = np.asarray(z, float)
        s, k, M, F = self.seed, self.k, self.M, self.F
        r = np.hypot(x, z) + 1e-9
        near = suave(F, F + 12, r)
        roll = (fbm(x / 45, z / 45, 4, s) - 0.5) * 2.2 * near
        mid = (fbm(x / 160, z / 160, 4, s + 3) - 0.5) * 14 * suave(F + 8, 220, r)
        big = fbm(x / 650, z / 650, 5, s + 5)
        rid = 1 - np.abs(2 * fbm(x / 1500, z / 1500, 6, s + 9) - 1)
        if k in ('montanas', 'nieve'):
            h = M * 22 * rid ** 2 * suave(260, 2300, r) + 70 * fbm(x / 320, z / 320, 4, s + 2) * suave(70, 520, r)
            return h + roll + mid * 0.5
        if k == 'lago':
            front = suave(0.0, 0.5, -z / r)
            lake = front * suave(F + 3, F + 11, r) * (1 - suave(650, 950, r))
            hills = M * 18 * (0.3 + 0.7 * rid) * suave(700, 2300, r) + 40 * big * suave(120, 700, r) * (1 - lake)
            return (roll + mid * 0.6) * (1 - lake) + hills - lake * 4.0
        if k == 'playa':
            sea = suave(-0.25, 0.35, -z / r)
            bajo = sea * (suave(F + 4, F + 24, r) * 3.0 + suave(40, 500, r) * 25)
            dunas = 1.6 * fbm(x / 22, z / 22, 3, s + 4) * near * (1 - sea)
            return dunas + M * 6 * big * suave(90, 700, r) * (1 - sea) - bajo
        if k == 'isla':
            bajo = suave(F + 8, F + 24, r) * 3.6 + suave(60, 500, r) * 25
            islote = suave(0.58, 0.72, fbm(x / 900, z / 900, 3, s + 6)) * suave(500, 800, r) * (1 - suave(2500, 3200, r))
            return roll * 0.4 - bajo + islote * (M * 9 + 30)
        if k == 'desierto':
            m = fbm(x / 520, z / 520, 4, s + 11)
            mesa = M * 5.5 * (suave(0.52, 0.55, m) + 0.5 * suave(0.63, 0.655, m)) * suave(240, 900, r)
            dunas = (1.4 * np.sin(x * 0.11 + fbm(x / 40, z / 40, 2, s) * 6) + 1.0) * near
            dunas = dunas + 9 * fbm(x / 130, z / 130, 3, s + 1) * suave(40, 300, r)
            return mesa + dunas
        if k == 'ciudad':
            return roll * 0.15
        return M * 10 * suave(0.3, 0.85, big) * suave(90, 800, r) + roll + mid

    def y(self, x, z):
        return GY + self.altura(x, z)

    # ───────────── Mallas de terreno ─────────────
    def _polar(self, radios, a0, a1, segs):
        ang = np.linspace(a0, a1, segs + 1)
        R, A = np.meshgrid(radios, ang, indexing='ij')
        x = np.cos(A) * R; z = np.sin(A) * R
        yv = self.y(x, z)
        verts = np.stack([x, z, yv], -1).reshape(-1, 3)  # Blender: (x, z_unity, y_unity)
        n, m = len(radios), segs + 1
        faces = []
        for i in range(n - 1):
            for j in range(segs):
                a = i * m + j
                faces.append((a, a + m, a + m + 1, a + 1))
        return verts, faces

    def terreno_cerca(self):
        """Parches por sector (islas UV separadas). La franja junto a la casa lleva más texels."""
        bandas = ((np.arange(3.0, 6.01, 0.5), 1.0), (np.arange(6.0, 10.01, 0.5), 1.0), (np.arange(10.0, 16.01, 0.5), 1.0),
                  (np.arange(16.0, 23.01, 1.0), 0.36), (np.arange(23.0, CERCA + 0.01, 1.0), 0.3))
        for radios, peso in bandas:
            for s in range(8):
                a0, a1 = s * math.pi / 4, (s + 1) * math.pi / 4
                v, f = self._polar(radios, a0, a1, 14)
                # UV rectangular en coordenadas polares: el parche llena su rectángulo sin desperdicio.
                ang = np.linspace(a0, a1, 15)
                R, A = np.meshgrid(radios, ang, indexing='ij')
                uv = np.stack([(A - a0) * radios.mean(), R - radios[0]], -1).reshape(-1, 2)
                keep = []
                for q in f:
                    p = v[list(q)]
                    bajo_casa = np.all((np.abs(p[:, 0]) < 6.2 * self.sx - 0.3) & (np.abs(p[:, 1]) < 5.2 * self.sz - 0.3))
                    sumergido = self.con_agua and np.all(p[:, 2] < GY + AGUA - 0.05)
                    if not bajo_casa and not sumergido: keep.append(q)
                # Las caras miran hacia arriba en Blender.
                keep = [q if np.cross(v[q[1]] - v[q[0]], v[q[2]] - v[q[0]])[2] > 0 else q[::-1] for q in keep]
                self.g.raw('terreno', v, keep, atlas='cerca', peso=peso, suave=True, uv=uv)

    def terreno_lejos(self):
        """Terreno completo para el panorama: interior (se oculta al hornear) y exterior."""
        def anillos(r0, r1, paso):
            out = [r0]
            while out[-1] < r1: out.append(out[-1] * paso + 0.02)
            return np.array(out)
        for nombre, radios in (('terreno_dentro', anillos(2.0, CERCA - 0.3, 1.035)),
                               ('terreno_fuera', anillos(CERCA - 0.3, LEJOS, 1.028))):
            v, f = self._polar(radios, 0, 2 * math.pi, 720)
            if np.cross(v[f[0][1]] - v[f[0][0]], v[f[0][2]] - v[f[0][0]])[2] < 0:
                f = [q[::-1] for q in f]
            self.lejos.raw('terreno', v, f, atlas=nombre, suave=True)
        if self.con_agua:
            radios = anillos(8.0, 40000.0, 1.06)
            ang = np.linspace(0, 2 * math.pi, 361)
            R, A = np.meshgrid(radios, ang, indexing='ij')
            x = np.cos(A) * R; z = np.sin(A) * R
            v = np.stack([x, z, np.full_like(x, GY + AGUA)], -1).reshape(-1, 3)
            m = 361
            faces, cols = [], []
            prof = np.clip((AGUA - self.altura(x, z)) / 3.0, 0, 1)
            for i in range(len(radios) - 1):
                for j in range(360):
                    a = i * m + j
                    faces.append((a, a + 1, a + m + 1, a + m))
                    d = float(prof[i, j])
                    cols.append((d, d, d))
            if np.cross(v[faces[0][1]] - v[faces[0][0]], v[faces[0][2]] - v[faces[0][0]])[2] < 0:
                faces = [q[::-1] for q in faces]
            self.lejos.raw('agua', v, faces, atlas='agua', suave=True, color=np.array(cols))

    # ───────────── Vegetación ─────────────
    def _tronco(self, g, atlas, a, b, r0, r1, mat='tronco', n=7, peso=0.6):
        d = b - a
        m = T((a + b) * 0.5) @ FromUp(d) @ Sc((2 * r0, np.linalg.norm(d), 2 * r0))
        g.cyl(mat, None, None, m=m, n=n, r1=0.5 * r1 / r0, caps=False, plan=False, atlas=atlas, peso=peso)

    def _hojas(self, g, atlas, centros, normales, medio, pal=None, mat='follaje', forma=0.58, sombra=None):
        """Hojas (dos caras al exportar): rombos alargados, girados al azar, con color variado."""
        n = len(centros)
        nrm = normales / (np.linalg.norm(normales, axis=1, keepdims=True) + 1e-9)
        ref = np.where(np.abs(nrm[:, 1:2]) > 0.9, np.array([[1.0, 0, 0]]), np.array([[0, 1.0, 0]]))
        tg = np.cross(nrm, ref); tg /= np.linalg.norm(tg, axis=1, keepdims=True) + 1e-9
        bt = np.cross(nrm, tg)
        phi = self.rng.uniform(0, 2 * math.pi, (n, 1))
        tg, bt = tg * np.cos(phi) + bt * np.sin(phi), bt * np.cos(phi) - tg * np.sin(phi)
        s = (medio * self.rng.uniform(0.7, 1.35, n))[:, None]
        quad = np.stack([centros - tg * s, centros - bt * s * forma, centros + tg * s, centros + bt * s * forma], 1).reshape(-1, 3)
        pal = pal or self.pal
        base = np.array(pal)[self.rng.integers(0, len(pal), n)]
        col = base * self.rng.uniform(0.7, 1.25, (n, 1))
        if sombra is not None: col = col * sombra[:, None]
        faces = [(4 * i, 4 * i + 1, 4 * i + 2, 4 * i + 3) for i in range(n)]
        g.raw(mat, quad[:, [0, 2, 1]], faces, atlas=atlas, doble=True, color=col, peso=1.0)

    def arbol(self, g, atlas, p, alto, por_copa=380, hoja=0.12):
        """Árbol de hoja ancha: ramas y copas hechas de cáscaras de hojas alrededor de cada punta."""
        rng = self.rng
        p = np.asarray(p, float)
        puntas = []

        def rama(a, d, largo, radio, nivel):
            b = a + d * largo
            self._tronco(g, atlas, a, b, radio, radio * 0.72, n=7 if nivel >= 2 else 5)
            if nivel == 0:
                puntas.append(b); return
            u = _norm(np.cross(d, [0.3, 0.1, 0.9])); v = np.cross(d, u)
            hijos = 3 if nivel >= 2 else 2 + (rng.random() < 0.5)
            giro = rng.uniform(0, 2 * math.pi)
            for i in range(hijos):
                ang = giro + i * 2 * math.pi / hijos + rng.uniform(-0.4, 0.4)
                abre = rng.uniform(0.5, 0.95)
                nd = d * math.cos(abre) + (u * math.cos(ang) + v * math.sin(ang)) * math.sin(abre)
                nd[1] += 0.12
                rama(b, _norm(nd), largo * rng.uniform(0.6, 0.8), radio * 0.62, nivel - 1)
            if nivel >= 1:
                rama(b, _norm(d + rng.uniform(-0.2, 0.2, 3)), largo * 0.72, radio * 0.66, nivel - 1)

        tilt = _norm(np.array([rng.uniform(-0.08, 0.08), 1.0, rng.uniform(-0.08, 0.08)]))
        rama(p - tilt * 0.15, tilt, alto * 0.3, alto * 0.034, 2)
        pts = np.array(puntas)
        centro = pts.mean(0)
        cs, ns, sh = [], [], []
        for b in pts:
            R = alto * rng.uniform(0.15, 0.22)
            d = rng.normal(size=(por_copa, 3)); d /= np.linalg.norm(d, axis=1, keepdims=True)
            d[:, 1] = np.where(d[:, 1] < -0.35, -d[:, 1], d[:, 1])       # Casi nada por debajo de la copa.
            rr = rng.uniform(0.62, 1.0, (por_copa, 1))
            c = b + d * R * rr * np.array([1.0, 0.72, 1.0])
            afuera = c - centro
            afuera /= np.linalg.norm(afuera, axis=1, keepdims=True) + 1e-9
            ns.append(d * 0.6 + afuera * 0.4 + rng.normal(scale=0.45, size=(por_copa, 3)) + [0, 0.35, 0])
            cs.append(c); sh.append(0.55 + 0.45 * rr[:, 0])
        self._hojas(g, atlas, np.concatenate(cs), np.concatenate(ns), hoja, sombra=np.concatenate(sh))

    def conifera(self, g, atlas, p, alto, nevado=0.0):
        rng = self.rng
        p = np.asarray(p, float)
        self._tronco(g, atlas, p - [0, 0.15, 0], p + [0, alto * 0.97, 0], alto * 0.018 + 0.03, 0.01)
        niveles = int(9 + alto * 1.3)
        base_r = alto * rng.uniform(0.2, 0.26)
        verts, faces, cols = [], [], []
        pal = np.array(self.pal)
        for i in range(niveles):
            f = i / (niveles - 1)
            yb = alto * (0.14 + 0.84 * f)
            rr = base_r * (1 - f) ** 0.85 + 0.12
            ramas = int(7 + 5 * (1 - f))
            giro = rng.uniform(0, 2 * math.pi)
            for j in range(ramas):
                a = giro + j * 2 * math.pi / ramas + rng.uniform(-0.2, 0.2)
                d = np.array([math.cos(a), 0, math.sin(a)])
                side = np.array([-d[2], 0, d[0]])
                largo = rr * rng.uniform(0.8, 1.1)
                cae = rng.uniform(0.25, 0.5) * largo * (1 - 0.6 * f)
                w = largo * rng.uniform(0.3, 0.42)
                o = p + [0, yb, 0]
                tip = o + d * largo - [0, cae, 0]
                midp = o + d * largo * 0.55 - [0, cae * 0.3, 0] + [0, largo * 0.07, 0]
                l = o + d * largo * 0.5 + side * w - [0, cae * 0.45, 0]
                r_ = o + d * largo * 0.5 - side * w - [0, cae * 0.45, 0]
                base = len(verts)
                verts += [o, l, tip, r_, midp]
                faces += [(base, base + 4, base + 1), (base + 1, base + 4, base + 2),
                          (base + 2, base + 4, base + 3), (base + 3, base + 4, base)]
                c = pal[rng.integers(0, len(pal))] * rng.uniform(0.7, 1.2)
                if nevado > 0 and rng.random() < nevado: c = np.array([0.8, 0.83, 0.88]) * rng.uniform(0.85, 1.0)
                cols += [c] * 4
        g.raw('follaje', np.array(verts)[:, [0, 2, 1]], faces, atlas=atlas, doble=True, color=np.array(cols))

    def palmera(self, g, atlas, p, alto):
        rng = self.rng
        p = np.asarray(p, float)
        a = rng.uniform(0, 2 * math.pi)
        lean = np.array([math.cos(a), 0, math.sin(a)]) * alto * rng.uniform(0.12, 0.3)
        prev = p - [0, 0.2, 0]
        for i in range(1, 9):
            f = i / 8
            nxt = p + np.array([0, alto * f, 0]) + lean * f * f
            self._tronco(g, atlas, prev, nxt, 0.17 - 0.07 * f, 0.17 - 0.07 * (f + 0.125), n=8)
            prev = nxt
        verts, faces, cols = [], [], []
        pal = np.array(self.pal)
        for k in range(15):
            yaw = k * 2 * math.pi / 15 + rng.uniform(-0.2, 0.2)
            d = np.array([math.cos(yaw), 0, math.sin(yaw)])
            side = np.array([-d[2], 0, d[0]])
            largo = alto * rng.uniform(0.34, 0.46)
            sube = rng.uniform(0.15, 0.75)
            col = pal[rng.integers(0, len(pal))] * rng.uniform(0.75, 1.2)
            pts = []
            for sgm in range(9):
                f = sgm / 8
                pts.append(prev + d * largo * f + np.array([0, largo * (sube * f - 0.95 * f * f), 0]))
            for sgm in range(8):
                o, e = pts[sgm], pts[sgm + 1]
                f = (sgm + 0.5) / 8
                w = largo * 0.2 * (1 - 0.75 * f) * (0.5 + min(1.0, f * 4) * 0.5)
                for sgn in (-1, 1):
                    tip = (o + e) * 0.5 + side * sgn * w - [0, w * 0.55, 0]
                    base = len(verts)
                    verts += [o, e, tip]
                    faces.append((base, base + 1, base + 2))
                    cols.append(col * rng.uniform(0.9, 1.1))
        g.raw('follaje', np.array(verts)[:, [0, 2, 1]], faces, atlas=atlas, doble=True, color=np.array(cols))

    def arbusto(self, g, atlas, p, tam, pal=None):
        rng = self.rng
        n = int(140 * tam)
        d = rng.normal(size=(n, 3)); d /= np.linalg.norm(d, axis=1, keepdims=True)
        d[:, 1] = np.abs(d[:, 1])
        c = np.asarray(p, float) + [0, tam * 0.15, 0] + d * np.array([tam * 0.75, tam * 0.6, tam * 0.75]) * rng.uniform(0.55, 1.0, (n, 1))
        self._hojas(g, atlas, c, d + rng.normal(scale=0.5, size=(n, 3)), 0.07 + 0.03 * tam, pal)

    def roca(self, g, atlas, p, tam):
        rng = self.rng
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
        v = np.array([vv.co[:] for vv in bm.verts]); f = [tuple(x.index for x in ff.verts) for ff in bm.faces]
        bm.free()
        off = rng.uniform(0, 100, 2)
        k = 0.55 + 0.9 * fbm(v[:, 0] * 1.3 + off[0] + v[:, 2] * 0.7, v[:, 1] * 1.3 + off[1], 3, self.seed)
        v = v * k[:, None] * np.array([tam * rng.uniform(0.8, 1.5), tam * rng.uniform(0.7, 1.3), tam * rng.uniform(0.45, 0.8)])
        yaw = rng.uniform(0, 2 * math.pi); c, s = math.cos(yaw), math.sin(yaw)
        v = np.stack([v[:, 0] * c - v[:, 1] * s, v[:, 0] * s + v[:, 1] * c, v[:, 2]], 1)
        pb = np.array([p[0], p[2], p[1] + tam * 0.1])
        g.raw('roca', v + pb, f, atlas=atlas, suave=True, peso=0.6)

    def cactus(self, g, atlas, p, alto):
        rng = self.rng
        p = np.asarray(p, float)
        self._tronco(g, atlas, p - [0, 0.1, 0], p + [0, alto, 0], 0.19, 0.15, 'cactus', 10, 0.7)
        for sgn in (-1, 1):
            if rng.random() < 0.3: continue
            yb = alto * rng.uniform(0.3, 0.55)
            yaw = rng.uniform(0, 2 * math.pi)
            o = np.array([math.cos(yaw), 0, math.sin(yaw)]) * sgn
            a = p + [0, yb, 0] + o * 0.12
            b = a + o * 0.5 + [0, 0.12, 0]
            self._tronco(g, atlas, a, b, 0.11, 0.11, 'cactus', 8, 0.7)
            self._tronco(g, atlas, b - [0, 0.08, 0], b + [0, alto * rng.uniform(0.25, 0.45), 0], 0.11, 0.09, 'cactus', 8, 0.7)

    def matas(self, g, atlas, puntos):
        """Matas de pasto: hojas triangulares finas que rompen el borde plano del suelo."""
        rng = self.rng
        verts, faces, cols = [], [], []
        suelo = np.array(_pasto(self.t['suelo']))
        for p in puntos:
            for _ in range(5):
                a = rng.uniform(0, 2 * math.pi)
                d = np.array([math.cos(a), 0, math.sin(a)])
                side = np.array([-d[2], 0, d[0]]) * rng.uniform(0.008, 0.016)
                o = p + d * rng.uniform(0, 0.06)
                alto = rng.uniform(0.06, 0.17)
                tip = o + d * alto * rng.uniform(0.2, 0.8) + [0, alto, 0]
                base = len(verts)
                verts += [o - side, o + side, tip]
                faces.append((base, base + 1, base + 2))
                cols.append(suelo * rng.uniform(1.1, 1.9))
        if verts:
            g.raw('pasto_hoja', np.array(verts)[:, [0, 2, 1]], faces, atlas=atlas, doble=True, color=np.array(cols))

    # ───────────── Reparto ─────────────
    def _libre(self, x, z, margen=0.0):
        """True si el punto no cae en la casa, la terraza ni el camino."""
        casa = (abs(x) < 7.3 * self.sx + margen) and (-10.3 * self.sz - margen < z < 6.4 * self.sz + margen)
        camino = abs(x - 1.7 * self.sx) < 1.5 and -27 < z < -5
        return not casa and not camino

    def puntos(self, n, r0, r1, pendiente=0.6, agua=0.35):
        out = []
        for _ in range(n * 8):
            if len(out) >= n: break
            a = self.rng.uniform(0, 2 * math.pi)
            r = math.sqrt(self.rng.uniform(r0 * r0, r1 * r1))
            x, z = math.cos(a) * r, math.sin(a) * r
            if not self._libre(x, z): continue
            hgt = float(self.altura(x, z))
            if self.con_agua and hgt < AGUA + agua: continue
            sl = abs(float(self.altura(x + 1, z)) - float(self.altura(x - 1, z))) + \
                 abs(float(self.altura(x, z + 1)) - float(self.altura(x, z - 1)))
            if sl > pendiente * 2: continue
            out.append(np.array([x, GY + hgt - 0.03, z]))
        return out

    def _bordes(self, n):
        """Puntos pegados al zócalo, la terraza y el camino: ahí el pasto crece sin cortar."""
        rng, out = self.rng, []
        rects = [(-6.2 * self.sx, 6.2 * self.sx, -5.2 * self.sz, 5.2 * self.sz),
                 (-6.0 * self.sx, 0.0, -8.9 * self.sz, -5.0 * self.sz),
                 (1.7 * self.sx - 0.7, 1.7 * self.sx + 0.7, -11.8 * self.sz, -5.6 * self.sz)]
        dentro = lambda x, z: any(r[0] < x < r[1] and r[2] < z < r[3] for r in rects)
        for _ in range(n * 6):
            if len(out) >= n: break
            x0, x1, z0, z1 = rects[int(rng.integers(0, 3))]
            per = 2 * (x1 - x0) + 2 * (z1 - z0)
            s = rng.uniform(0, per)
            d = min(1.4, rng.exponential(0.3)) + 0.04
            if s < x1 - x0: x, z = x0 + s, z0 - d
            elif s < 2 * (x1 - x0): x, z = x0 + s - (x1 - x0), z1 + d
            elif s < 2 * (x1 - x0) + (z1 - z0): x, z = x0 - d, z0 + s - 2 * (x1 - x0)
            else: x, z = x1 + d, z0 + s - 2 * (x1 - x0) - (z1 - z0)
            if dentro(x, z): continue
            hgt = float(self.altura(x, z))
            if self.con_agua and hgt < AGUA + 0.3: continue
            out.append(np.array([x, GY + hgt - 0.02, z]))
        return out

    def construir(self):
        k, g, rng = self.k, self.g, self.rng
        self.terreno_cerca()
        self.terreno_lejos()
        F = self.F
        # Piedras del camino (sus colisionadores están en HouseGenerator).
        for i in range(8):
            g.soft('piedra', (1.7 * self.sx, -0.215, (-6 - i * 0.78) * self.sz), (1.25, 0.13, 0.55), r=0.05, n=4,
                   plan=False, atlas='cerca', peso=0.8, e=(0, rng.uniform(-6, 6), 0))
        cerca = dict(arbol=0, conifera=0, palmera=0, arbusto=10, roca=6, cactus=0, matas=2600)
        if k in ('jardin', 'otono'): cerca.update(arbol=7, conifera=1, arbusto=16)
        elif k == 'bosque': cerca.update(arbol=10, conifera=5, arbusto=18)
        elif k == 'montanas': cerca.update(conifera=10, roca=12, arbusto=6)
        elif k == 'nieve': cerca.update(conifera=11, roca=8, arbusto=0, matas=0)
        elif k == 'lago': cerca.update(arbol=5, conifera=5, arbusto=12, roca=9)
        elif k in ('playa', 'isla'): cerca.update(palmera=7, arbusto=9, roca=6, matas=500)
        elif k == 'desierto': cerca.update(cactus=9, roca=14, arbusto=5, matas=250)
        elif k == 'ciudad': cerca.update(arbol=5, arbusto=12, roca=0)
        nev = 0.35 if k == 'nieve' else 0.0
        for p in self.puntos(cerca['arbol'], F - 1.5, CERCA - 3): self.arbol(g, 'cerca', p, rng.uniform(6.0, 9.5))
        for p in self.puntos(cerca['conifera'], F - 1.5, CERCA - 3): self.conifera(g, 'cerca', p, rng.uniform(7, 12), nev)
        for p in self.puntos(cerca['palmera'], F - 3, CERCA - 4): self.palmera(g, 'cerca', p, rng.uniform(5.5, 8.5))
        for p in self.puntos(cerca['arbusto'], F - 5, CERCA - 2): self.arbusto(g, 'cerca', p, rng.uniform(0.6, 1.3))
        for p in self.puntos(cerca['roca'], F - 4, CERCA - 2, 1.5): self.roca(g, 'cerca', p, rng.uniform(0.3, 1.1))
        for p in self.puntos(cerca['cactus'], F - 4, CERCA - 2): self.cactus(g, 'cerca', p, rng.uniform(1.6, 3.4))
        self.matas(g, 'cerca', self.puntos(cerca['matas'] // 6, 7.5, F + 3, 1.0) + self._bordes(cerca['matas'] // 3))
        if k == 'lago': self.muelle()
        if k == 'ciudad': self.ciudad()
        self._variantes_lejos(nev)
        return self

    def muelle(self):
        g = self.g
        deck = GY + AGUA + 0.45
        g.box('deck', (1.7, deck, -19), (1.9, 0.1, 12), plan=False, atlas='cerca')
        for i in range(6):
            for lado in (-1, 1):
                g.cyl('madera_oscura', (1.7 + lado * 0.85, deck - 0.9, -13.5 - i * 2.2), (0.16, 2.0, 0.16), n=8,
                      plan=False, atlas='cerca', peso=0.5)

    def ciudad(self):
        """Edificios lejanos con ventanas encendidas (solo panorama) y farolas junto al camino."""
        rng = self.rng
        self.farolas = []
        n = 0
        for _ in range(900):
            if n >= 260: break
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(75, 950)
            x, z = math.cos(a) * r, math.sin(a) * r
            # Parque abierto frente a la casa; el centro de la ciudad queda al frente, más lejos.
            centro = math.exp(-((x + 120) ** 2 + (z + 620) ** 2) / (2 * 260.0 ** 2))
            if r < 140 and rng.random() < 0.6: continue
            w, d = rng.uniform(16, 42), rng.uniform(16, 42)
            alto = rng.uniform(14, 45) + 190 * centro * rng.uniform(0.3, 1.0) ** 1.5
            tono = rng.uniform(0.5, 1.0)
            self.lejos.box('edificio', (x, GY + alto * 0.5 - 1, z), (w, alto, d), plan=False, atlas='edificios',
                           color=(tono, rng.uniform(0, 1), rng.uniform(0, 1)), tapa=False)
            n += 1
        for i in range(6):
            p = np.array([(0.5 if i % 2 == 0 else 2.9) * self.sx, GY, (-7.0 - i * 1.7) * self.sz])
            self.g.cyl('metal', p + [0, 1.55, 0], (0.07, 3.1, 0.07), n=8, plan=False, atlas='cerca', peso=0.6)
            self.g.soft('farol', p + [0, 3.18, 0], (0.3, 0.2, 0.3), r=0.06, n=3, plan=False, atlas='cerca', peso=1.2)
            self.farolas.append(p + [0, 2.95, 0])

    def _variantes_lejos(self, nev):
        """Árboles lejanos: pocas variantes repetidas miles de veces sobre el terreno."""
        k, rng = self.k, self.rng
        mezcla = dict(jardin=('arbol', 0.006, 600), otono=('arbol', 0.012, 700), bosque=('mixto', 0.03, 800),
                      montanas=('conifera', 0.02, 1100), nieve=('conifera', 0.02, 1000), lago=('mixto', 0.02, 1100),
                      playa=('palmera', 0.004, 400), isla=('palmera', 0.01, 3200), desierto=('cactus', 0.0015, 300),
                      ciudad=('arbol', 0.004, 250)).get(k)
        tipo, dens, radio = mezcla
        tipos = ['arbol', 'conifera'] if tipo == 'mixto' else [tipo]
        variantes = []
        for tp in tipos:
            for i in range(3):
                gv = Geo((1, 1))
                if tp == 'arbol': self.arbol(gv, 'var', (0, 0, 0), rng.uniform(7, 10), por_copa=520, hoja=0.11)
                elif tp == 'conifera': self.conifera(gv, 'var', (0, 0, 0), rng.uniform(9, 13), nev)
                elif tp == 'palmera': self.palmera(gv, 'var', (0, 0, 0), rng.uniform(6, 9))
                else: self.cactus(gv, 'var', (0, 0, 0), rng.uniform(2, 3.5))
                variantes.append(gv)
        # Posiciones: densidad que baja con la distancia, solo en tierra firme y laderas suaves.
        n = int(dens * math.pi * (radio ** 2 - CERCA ** 2) * 0.55)
        n = min(n, 60000)
        a = rng.uniform(0, 2 * math.pi, n * 3)
        r = np.sqrt(rng.uniform((CERCA + 1.5) ** 2, radio ** 2, n * 3)) * rng.uniform(0.35, 1.0, n * 3) ** 0.5
        r = np.maximum(r, CERCA + 1.5)
        x, z = np.cos(a) * r, np.sin(a) * r
        hgt = self.altura(x, z)
        sl = np.abs(self.altura(x + 2, z) - self.altura(x - 2, z)) + np.abs(self.altura(x, z + 2) - self.altura(x, z - 2))
        ok = sl < 3.2
        if self.con_agua: ok &= hgt > AGUA + 0.5
        if k in ('montanas', 'nieve'): ok &= hgt < self.M * 9
        if tipo != 'cactus' and k != 'isla':
            ok &= fbm(x / 120, z / 120, 3, self.seed + 21) > 0.36   # Claros y manchas de bosque.
        idx = np.nonzero(ok)[0][:n]
        self.far_points = (x[idx], z[idx], hgt[idx], rng.uniform(0.7, 1.35, len(idx)), rng.uniform(0, 2 * math.pi, len(idx)))
        self.far_variants = variantes


def instanciar(variantes_objs, pts):
    """Reparte las variantes sobre el terreno con instancias por cara (rotación y tamaño al azar)."""
    x, z, hgt, esc, yaw = pts
    nv = len(variantes_objs)
    creados = []
    for vi, grupo in enumerate(variantes_objs):
        sel = np.nonzero(np.arange(len(x)) % nv == vi)[0]
        if not len(sel): continue
        c = np.stack([x[sel], z[sel], GY + hgt[sel] - 0.1], 1)
        s = esc[sel][:, None]
        verts, faces = [], []
        for k in range(3):
            ang = yaw[sel] + k * 2 * math.pi / 3
            verts.append(c + np.stack([np.cos(ang), np.sin(ang), np.zeros(len(sel))], 1) * s)
        v = np.stack(verts, 1).reshape(-1, 3)
        faces = [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(sel))]
        me = bpy.data.meshes.new('puntos'); me.from_pydata(v.tolist(), [], faces); me.update()
        holder = bpy.data.objects.new('instancias', me)
        bpy.context.scene.collection.objects.link(holder)
        holder.instance_type = 'FACES'
        holder.use_instance_faces_scale = True
        # Un triángulo equilátero de radio 1 tiene área 1,299: la raíz da la escala 1.
        holder.instance_faces_scale = 1.0 / math.sqrt(1.299)
        holder.show_instancer_for_render = False
        for ob in grupo:
            ob.parent = holder
        creados.append(holder)
    return creados


def _pasto(c):
    """Los verdes del tema son de pantalla: como reflectancia real se bajan y se desaturan un poco."""
    c = lin(c)
    lum = 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
    k = 0.62 if lum < 0.45 else 0.85          # Arena y nieve casi no se tocan.
    return tuple((v * 0.8 + lum * 0.2) * k for v in c)


def materiales(t, lejos=True):
    m = {}
    suelo, suelo2, roca = _pasto(t['suelo']), _pasto(t['suelo2']), lin(t['roca'])
    nieve = lin(t.get('nieve', (0.95, 0.96, 0.98)))
    k = t['exterior']
    mat = bpy.data.materials.new('terreno'); nt = NT(mat)
    geo = nt.t.nodes['Geometry']
    px, py, pz = sep(nt, nt.pos)
    slope = sep(nt, geo.outputs['Normal'])[2]
    dist = nt.vm('LENGTH', nt.comb(px, py, 0.0))
    parches = nt.noise(nt.pos, 0.09, 4.0, 0.6)
    medio = nt.noise(nt.pos, 0.9, 5.0, 0.65)
    fino = nt.noise(nt.pos, 55.0, 4.0, 0.7)
    manchas = nt.noise(nt.pos, 0.33, 3.0, 0.5)
    c = nt.mix(nt.ramp(parches, [(0.36, (0, 0, 0)), (0.64, (1, 1, 1))]), suelo, suelo2)
    c = nt.mix(nt.ramp(manchas, [(0.5, (0, 0, 0)), (0.75, (1, 1, 1))]), c, mul(lerp(suelo, suelo2, 0.5), 0.6))
    tono = nt.m('ADD', nt.m('MULTIPLY', medio, 0.5), nt.m('MULTIPLY', fino, 0.5))
    c = nt.mix(1.0, c, nt.mix(tono, (0.62, 0.62, 0.62), (1.38, 1.38, 1.38)), 'MULTIPLY')
    # Manto de bosque a lo lejos, donde ya no hay árboles sueltos.
    if k in ('jardin', 'otono', 'bosque', 'montanas', 'lago', 'nieve'):
        hoja = np.mean([lin(h) for h in t['hojas']], axis=0)
        bosque = nt.m('MULTIPLY', nt.ss(500.0, 1100.0, dist),
                      nt.ramp(nt.noise(nt.pos, 0.006, 4.0, 0.6), [(0.35, (0, 0, 0)), (0.55, (1, 1, 1))]))
        c = nt.mix(nt.m('MULTIPLY', bosque, 0.85), c, nt.mix(medio, mul(tuple(hoja), 0.45), mul(tuple(hoja), 0.9)))
    # Roca en laderas empinadas.
    estrias = nt.noise(nt.comb(px, py, nt.m('MULTIPLY', pz, 6.0)), 0.05, 5.0, 0.7)
    rc = nt.mix(nt.m('ADD', nt.m('MULTIPLY', estrias, 0.6), nt.m('MULTIPLY', fino, 0.4)), mul(roca, 0.55), mul(roca, 1.35))
    lim = 0.80 if k not in ('desierto',) else 0.86
    c = nt.mix(nt.m('SUBTRACT', 1.0, nt.ss(lim - 0.16, lim, slope)), c, rc)
    if k in ('montanas', 'nieve', 'lago'):
        linea = dict(montanas=330.0, nieve=-50.0, lago=300.0)[k]
        alto = nt.m('ADD', pz, nt.m('MULTIPLY', nt.noise(nt.pos, 0.012, 3.0), 160.0))
        nv = nt.m('MULTIPLY', nt.ss(linea, linea + 120.0, alto), nt.ss(0.45, 0.7, slope))
        c = nt.mix(nv, c, nt.mix(fino, mul(nieve, 0.9), nieve))
    if k in ('lago', 'playa', 'isla'):
        orilla = nt.m('SUBTRACT', 1.0, nt.ss(GY + AGUA + 0.05, GY + AGUA + (0.5 if k == 'lago' else 1.3), pz))
        arena = mul(suelo2, 1.0) if k != 'lago' else lerp(suelo2, (0.25, 0.2, 0.14), 0.6)
        c = nt.mix(orilla, c, nt.mix(fino, mul(arena, 0.8), mul(arena, 1.1)))
        mojado = nt.m('SUBTRACT', 1.0, nt.ss(GY + AGUA - 0.05, GY + AGUA + 0.12, pz))
        c = nt.mix(nt.m('MULTIPLY', mojado, 0.45), c, (0.02, 0.02, 0.015))
    nt.finish(c, 1.0, nt.bump(nt.m('ADD', fino, nt.m('MULTIPLY', medio, 2.0)), 0.5, 0.02))
    m['terreno'] = mat

    mat = bpy.data.materials.new('follaje'); nt = NT(mat)
    c = nt.mix(nt.noise(nt.pos, 30.0, 2.0), nt.attr('col'), (0, 0, 0))
    p = nt.finish(nt.mix(0.25, nt.attr('col'), c), 0.6)
    m['follaje'] = mat
    mat = bpy.data.materials.new('pasto_hoja'); nt = NT(mat)
    nt.finish(nt.attr('col'), 0.7)
    m['pasto_hoja'] = mat

    mat = bpy.data.materials.new('tronco'); nt = NT(mat)
    px, py, pz = sep(nt, nt.pos)
    f = nt.noise(nt.comb(nt.m('MULTIPLY', px, 9.0), nt.m('MULTIPLY', py, 9.0), pz), 6.0, 5.0, 0.7)
    corteza = lerp((0.16, 0.11, 0.075), (0.3, 0.26, 0.22), 0.35 if k in ('playa', 'isla', 'nieve') else 0.0)
    nt.finish(nt.mix(f, mul(corteza, 0.5), mul(corteza, 1.5)), 0.95, nt.bump(f, 0.8, 0.02))
    m['tronco'] = mat

    mat = bpy.data.materials.new('roca'); nt = NT(mat)
    f = nt.noise(nt.pos, 3.0, 6.0, 0.7)
    g2 = nt.noise(nt.pos, 30.0, 3.0, 0.6)
    nt.finish(nt.mix(nt.m('ADD', nt.m('MULTIPLY', f, 0.65), nt.m('MULTIPLY', g2, 0.35)), mul(roca, 0.5), mul(roca, 1.4)),
              0.95, nt.bump(f, 0.9, 0.03))
    m['roca'] = mat
    mat = bpy.data.materials.new('piedra'); nt = NT(mat)
    f = nt.noise(nt.pos, 12.0, 5.0, 0.7)
    nt.finish(nt.mix(f, (0.17, 0.165, 0.155), (0.36, 0.35, 0.33)), 0.9, nt.bump(f, 0.4, 0.005))
    m['piedra'] = mat
    mat = bpy.data.materials.new('cactus'); nt = NT(mat)
    px, py, pz = sep(nt, nt.pos)
    hoja = lin(t['follaje'])
    nt.finish(nt.mix(nt.noise(nt.pos, 20.0), mul(hoja, 0.6), mul(hoja, 1.1)), 0.7)
    m['cactus'] = mat

    if t['exterior'] == 'ciudad':
        # Fachadas: pisos cada 3,4 m y ventanas cada 2,8 m; una de cada tres está encendida.
        mat = bpy.data.materials.new('edificio'); nt = NT(mat)
        geo = nt.t.nodes['Geometry']
        nx, ny, nz = sep(nt, geo.outputs['Normal'])
        px, py, pz = sep(nt, nt.pos)
        hcoord = nt.m('ADD', nt.m('MULTIPLY', nt.m('ABSOLUTE', nx), py), nt.m('MULTIPLY', nt.m('ABSOLUTE', ny), px))
        fu, fv = nt.m('DIVIDE', hcoord, 2.8), nt.m('DIVIDE', pz, 3.4)
        dentro = nt.m('MULTIPLY', nt.m('LESS_THAN', nt.m('ABSOLUTE', nt.m('SUBTRACT', nt.m('FRACT', fu), 0.5)), 0.33),
                      nt.m('LESS_THAN', nt.m('ABSOLUTE', nt.m('SUBTRACT', nt.m('FRACT', fv), 0.5)), 0.28))
        dentro = nt.m('MULTIPLY', dentro, nt.m('LESS_THAN', nt.m('ABSOLUTE', nz), 0.5))
        cr, cg, cb = sep(nt, nt.attr('col'))
        azar = nt.white(nt.comb(nt.m('FLOOR', fu), nt.m('FLOOR', fv), nt.m('MULTIPLY', cg, 97.0)))
        tibio = nt.white(nt.comb(nt.m('FLOOR', fv), nt.m('FLOOR', fu), nt.m('MULTIPLY', cb, 53.0)))
        encendida = nt.m('MULTIPLY', dentro, nt.m('LESS_THAN', azar, 0.34))
        luz = nt.mix(tibio, (1.0, 0.62, 0.3), (0.75, 0.85, 1.0))
        muro = nt.mix(cr, (0.012, 0.013, 0.016), (0.05, 0.05, 0.055))
        base = nt.mix(dentro, muro, (0.004, 0.005, 0.008))
        pb = nt.finish(base, 0.6)
        em = nt.mix(encendida, (0, 0, 0), luz)
        nt.t.links.new(em, pb.inputs['Emission Color'])
        pb.inputs['Emission Strength'].default_value = t.get('ventanas', 0.9)
        m['edificio'] = mat
        mat = bpy.data.materials.new('farol'); nt = NT(mat)
        nt.finish((0.8, 0.75, 0.6), 0.8, None, (1.0, 0.8, 0.55), 14.0)
        m['farol'] = mat
    if t['exterior'] in ('lago', 'playa', 'isla'):
        agua = lin(t['agua'])
        mat = bpy.data.materials.new('agua'); nt = NT(mat)
        prof = sep(nt, nt.attr('col'))[0]
        px, py, pz = sep(nt, nt.pos)
        dist = nt.vm('LENGTH', nt.comb(px, py, 0.0))
        olas = nt.noise(nt.pos, 1.4, 3.0, 0.6)
        mar = nt.noise(nt.pos, 0.08, 3.0, 0.5)
        claro = lerp(agua, (0.35, 0.75, 0.7), 0.55) if t['exterior'] != 'lago' else lerp(agua, (0.2, 0.3, 0.25), 0.4)
        c = nt.mix(nt.m('POWER', prof, 0.6), claro, mul(agua, 0.35))
        p = nt.finish(c, 0.045, nt.bump(nt.m('ADD', olas, nt.m('MULTIPLY', mar, 3.0)),
                                       0.35 if t['exterior'] != 'lago' else 0.16, 0.25))
        p.inputs['Specular IOR Level'].default_value = 0.5
        p.inputs['IOR'].default_value = 1.33
        m['agua'] = mat
    return m
