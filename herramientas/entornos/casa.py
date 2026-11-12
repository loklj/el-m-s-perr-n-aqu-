# Casa de Folio XR en Blender. Misma planta que HouseGenerator.cs (los colisionadores y los puntos
# de lectura siguen saliendo de ese archivo); aquí solo se hace lo que se ve, con más detalle.
import math
import numpy as np
from fx import Geo, Group, TRS, T, Euler, Sc, _cyl

LIBROS_CALIDOS = [(0.27, 0.40, 0.47), (0.66, 0.33, 0.23), (0.72, 0.60, 0.37), (0.35, 0.48, 0.35),
                  (0.47, 0.35, 0.43), (0.84, 0.76, 0.61), (0.32, 0.48, 0.53), (0.57, 0.44, 0.29)]
LIBROS_OSCUROS = [(0.20, 0.24, 0.32), (0.48, 0.18, 0.16), (0.55, 0.45, 0.25), (0.19, 0.32, 0.24),
                  (0.33, 0.22, 0.30), (0.70, 0.64, 0.52), (0.14, 0.15, 0.17), (0.42, 0.30, 0.20)]


class Casa:
    def __init__(self, g: Geo, tema):
        self.g, self.t = g, tema
        self.h = tema['alto']
        self.wall = 0.18
        self.rng = np.random.default_rng(tema['semilla'])
        self.luces = []     # (posición Unity, potencia relativa) de las lámparas
        self.libros = LIBROS_OSCUROS if tema.get('libros') == 'oscuros' else LIBROS_CALIDOS

    def rnd(self, a=0.0, b=1.0):
        return float(self.rng.uniform(a, b))

    def construir(self):
        self.cascara(); self.escalera(); self.salas()
        return self

    # ───────────── Estructura ─────────────
    def cascara(self):
        g, h, wall = self.g, self.h, self.wall
        g.box('hormigon', (0, -0.22, 0), (12.4, 0.24, 10.4), peso=0.35)
        g.box('suelo', (0, -0.07, 0), (12, 0.14, 10), peso=1.25)
        for pos, size in (((-1.15, h - 0.09, 0), (9.7, 0.18, 10)), ((4.85, h - 0.09, -3.2), (2.3, 0.18, 3.6)),
                          ((4.85, h - 0.09, 4.65), (2.3, 0.18, 0.7)), ((5.85, h - 0.09, 1.45), (0.3, 0.18, 5.7))):
            g.box('suelo', pos, size, peso=1.1)
            # Cielo raso de yeso bajo el entrepiso.
            g.box('techo', (pos[0], h - 0.19, pos[2]), (size[0], 0.02, size[2]), peso=0.8)
        g.box('techo', (0, 2 * h + 0.09, 0), (12.6, 0.18, 10.6), peso=0.45)
        for piso in range(2):
            y = piso * h
            self.fachada((0, y, -5), 12, 0, 4, 1)
            self.fachada((0, y, 5), 12, 0, 4, -1)
            self.fachada((-6, y, 0), 10, 90, 4, -1)
            self.fachada((6, y, 0), 10, 90, 4, -1)
        g.box('madera', (-0.15, 1.05, 2.6), (0.14, 2.1, 2.6))
        g.box('pared', (1.0, h + h * 0.5, 1.8), (wall, h, 6.4))
        g.box('madera', (1.0, h + h - 0.18, -2.65), (0.24, 0.36, 2.5))
        # Balcón del dormitorio.
        g.box('deck', (-3, h - 0.09, -6.15), (6, 0.18, 2.3))
        self.baranda((-6, h, -7.3), (0, h, -7.3))
        self.baranda((-6, h, -7.3), (-6, h, -5))
        self.baranda((0, h, -7.3), (0, h, -5))
        # Terraza y pérgola.
        g.box('deck', (-3, 0.02, -6.95), (6, 0.2, 3.9), peso=1.25)
        for lado in range(2):
            g.box('madera', (-5.75 + lado * 5.5, 1.46, -8.65), (0.16, 2.68, 0.16))
        g.box('madera', (-3, 2.75, -8.65), (5.7, 0.18, 0.18))
        for i in range(10):
            g.box('madera', (-5.7 + i * 0.6, 2.84, -8), (0.09, 0.12, 1.6))

    def fachada(self, centro, largo, yaw, vanos, puerta):
        g, h, wall = self.g, self.h, self.wall
        grp = Group(centro, yaw)
        ancho = largo / vanos
        g.box('pared', (0, h - 0.2, 0), (largo, 0.4, wall), group=grp)
        for i in range(vanos + 1):
            g.box('pared', (-largo * 0.5 + i * ancho, (h - 0.4) * 0.5, 0), (0.2, h - 0.4, wall), group=grp)
        for i in range(vanos):
            x = -largo * 0.5 + (i + 0.5) * ancho
            es_puerta = i == puerta
            abajo = 0.03 if es_puerta else 0.65
            if not es_puerta:
                g.box('pared', (x, 0.3, 0), (ancho - 0.2, 0.6, wall), group=grp)
            g.box('madera', (x, abajo, 0), (ancho - 0.18, 0.08, wall + 0.04), group=grp)
            g.box('madera', (x, h - 0.43, 0), (ancho - 0.18, 0.08, wall + 0.04), group=grp)
            if not es_puerta:
                g.box('metal', (x, (abajo + h - 0.43) * 0.5, 0), (0.035, h - 0.43 - abajo, 0.04), group=grp)

    def baranda(self, a, b):
        g = self.g
        a = np.array(a, float); b = np.array(b, float)
        up = np.array([0, 1.02, 0])
        g.beam('metal', a + up, b + up, 0.045)
        n = math.ceil(np.linalg.norm(b - a) / 0.8)
        for i in range(n + 1):
            pie = a + (b - a) * i / n
            g.box('metal', pie + up * 0.5, (0.035, 1.02, 0.035))

    def escalera(self):
        g, h = self.g, self.h
        pasos = 18
        sube, corre = h / pasos, 5.7 / pasos
        for i in range(pasos):
            top = (i + 1) * sube
            z = -1.4 + (i + 0.5) * corre
            g.box('madera', (4.7, top * 0.5, z), (1.7, top, corre), peso=0.7)
            g.box('metal', (4.7, top - 0.008, z - corre * 0.5 + 0.015), (1.7, 0.016, 0.03))
        self.baranda((3.78, sube, -1.24), (3.78, h, 4.15))
        self.baranda((5.62, sube, -1.24), (5.62, h, 4.15))
        self.baranda((3.65, h, -1.4), (3.65, h, 4.3))
        self.baranda((3.65, h, -1.4), (5.7, h, -1.4))

    # ───────────── Muebles ─────────────
    def salas(self):
        g, h = self.g, self.h
        self.asiento((-3.1, 0, 0.4), 2.6, 0, cojines=True)
        g.box('alfombra', (-3.1, 0.008, -1.6), (3.6, 0.016, 2.5), peso=1.3)
        self.mesa((-3.1, 0, -1.35), 1.4, 0.7, 0.45, adorno=True)
        self.lampara((-4.8, 0, 0.2))
        self.planta((-5.3, 0, -3.6), 1.15)
        self.asiento((1.7, 0, -1), 1.0, 180)
        self.mesa((0.55, 0, -1.0), 0.55, 0.55, 0.53, adorno=True)
        self.lampara((0.5, 0, -0.1))
        self.estante((1.7, 0, 4.55), 3.2, 0)
        self.estante((5.55, 0, -3.25), 2.6, 90)
        g.box('alfombra2', (2.1, 0.006, 1.1), (2.7, 0.012, 3), peso=1.2)
        # Cuadro en el tabique de la sala.
        self.cuadro((-0.235, 1.35, 2.6), 1.5, 0.95, 90, tono=(1.0, 0.92, 0.8))
        # Dormitorio.
        cama = np.array([-2.8, h, 1.25])
        g.box('madera', cama + (0, 0.3, 0), (2.1, 0.5, 2.5))
        g.soft('tela_clara', cama + (0, 0.64, 0), (1.98, 0.22, 2.37), r=0.07, bulge=(0, 0.02, 0), n=7)
        g.soft('tela', cama + (0, 0.78, 1.25), (2.2, 1.25, 0.14), r=0.05, bulge=(0, 0, 0.03))
        g.soft('tela', cama + (0, 0.765, -0.42), (2.04, 0.07, 1.5), r=0.03, bulge=(0, 0.035, 0), n=9)
        for i in range(2):
            g.soft('tela_clara', cama + (-0.48 + i * 0.96, 0.82, 0.83), (0.78, 0.14, 0.45),
                   e=(-8, self.rnd(-4, 4), 0), r=0.06, bulge=(0, 0.06, 0), n=7)
        g.soft('acento', cama + (0.0, 0.86, 0.5), (0.45, 0.12, 0.3), e=(-14, 12, 0), r=0.05, bulge=(0, 0.05, 0))
        self.mesa((-4.35, h, 1.8), 0.65, 0.6, 0.57, adorno=True)
        self.lampara((-4.6, h, 2.7))
        g.box('alfombra', (-2.8, h + 0.008, -1.5), (3.4, 0.016, 1.5), peso=1.2)
        self.planta((-5.3, h, -3.8), 1.2)
        self.cuadro((0.905, h + 1.55, 0.6), 0.9, 1.2, 90, tono=(0.85, 0.95, 1.0))
        self.cuadro((0.905, h + 1.55, 2.3), 0.9, 1.2, 90, tono=(1.0, 0.85, 0.8))
        self.asiento((-2.8, h, -6), 0.9, 0)
        self.mesa((-1.5, h, -6.3), 0.5, 0.5, 0.5)
        self.planta((-5.4, h, -6.5), 0.8)
        # Tumbona de la terraza.
        tum = Group((-3.4, 0.12, -7), 0)
        g.box('madera', (0, 0.30, -0.45), (0.92, 0.12, 1.9), group=tum)
        g.soft('tela', (0, 0.41, -0.45), (0.83, 0.13, 1.82), group=tum, r=0.05, bulge=(0, 0.02, 0), n=7)
        g.soft('tela', (0, 0.73, 0.45), (0.83, 0.7, 0.14), e=(22, 0, 0), group=tum, r=0.05, bulge=(0, 0, 0.02))
        g.soft('acento', (0, 0.80, 0.33), (0.42, 0.26, 0.12), e=(22, 0, 0), group=tum, r=0.05, bulge=(0, 0, 0.04))
        for i in range(2):
            g.box('madera', (0, 0.16, -1.1 + i * 1.5), (0.8, 0.32, 0.12), group=tum)
        self.mesa((-2.15, 0.12, -7.2), 0.55, 0.55, 0.48, adorno=True)
        self.planta((-5.35, 0.12, -8.1), 0.9)
        extras = round(self.t.get('muebles', 1.0) * 4)
        for i in range(extras):
            self.planta((-5.15 + i * 0.9, 0, 4.1), 0.65 + 0.15 * self.rnd())
        # Cortinas en los rincones de la sala y del dormitorio.
        for y in (0.0, h):
            self.cortina((-5.8, y, 3.2), 1.15, 0)
            self.cortina((-5.8, y, -4.35), 1.15, 0)

    def asiento(self, pos, ancho, yaw, cojines=False):
        g = self.g
        grp = Group(pos, yaw)
        g.box('madera', (0, 0.25, 0), (ancho, 0.24, 0.9), group=grp)
        n = max(1, round(ancho / 0.9))
        w = (ancho - 0.13) / n
        for i in range(n):
            x = -(ancho - 0.13) * 0.5 + (i + 0.5) * w
            g.soft('tela', (x, 0.455, -0.03), (w - 0.012, 0.2, 0.79), group=grp, r=0.06, bulge=(0, 0.03, 0), n=6)
            g.soft('tela', (x, 0.74, 0.37), (w - 0.012, 0.56, 0.2), e=(-9, 0, 0), group=grp, r=0.07,
                   bulge=(0, 0, 0.04), n=6)
        for lado in range(2):
            g.soft('tela', ((lado - 0.5) * ancho, 0.55, 0.02), (0.16, 0.38, 0.92), group=grp, r=0.06)
            for fin in range(2):
                g.box('madera', ((lado - 0.5) * (ancho - 0.2), 0.1, (fin - 0.5) * 0.65), (0.1, 0.2, 0.1), group=grp)
        if cojines:
            g.soft('acento', (-ancho * 0.5 + 0.42, 0.72, 0.13), (0.46, 0.44, 0.15), e=(-18, 14, 6), group=grp,
                   r=0.06, bulge=(0, 0, 0.07), n=6)
            g.soft('tela_clara', (ancho * 0.5 - 0.45, 0.72, 0.14), (0.46, 0.44, 0.15), e=(-18, -12, -5), group=grp,
                   r=0.06, bulge=(0, 0, 0.07), n=6)
            # Manta doblada sobre el brazo.
            g.soft('tela_clara', (ancho * 0.5 - 0.02, 0.755, 0.05), (0.3, 0.05, 0.6), group=grp, r=0.02)

    def mesa(self, pos, ancho, fondo, alto, adorno=False):
        g = self.g
        pos = np.array(pos, float)
        g.box('madera', pos + (0, alto, 0), (ancho, 0.07, fondo), grano=(1, 0, 0))
        for x in range(2):
            for z in range(2):
                g.box('metal', pos + ((x - 0.5) * (ancho - 0.14), alto * 0.5, (z - 0.5) * (fondo - 0.14)),
                      (0.045, alto, 0.045))
        if adorno:
            top = alto + 0.035
            yaw = self.rnd(-25, 25)
            for k in range(2):  # Dos libros apilados.
                c = self.libros[int(self.rnd(0, 8))]
                g.box('libros', pos + (ancho * 0.18, top + 0.016 + k * 0.032, 0.02), (0.2 - k * 0.02, 0.03, 0.14),
                      e=(0, yaw + k * 9, 0), color=c)
            # Taza de cerámica.
            g.cyl('maceta', pos + (-ancho * 0.22, top + 0.045, -fondo * 0.12), (0.08, 0.09, 0.08), n=16)

    def lampara(self, pos):
        g = self.g
        pos = np.array(pos, float)
        g.cyl('metal', pos + (0, 0.02, 0), (0.34, 0.04, 0.34), n=24)
        g.cyl('metal', pos + (0, 0.82, 0), (0.028, 1.6, 0.028), n=10)
        g.cyl('pantalla', pos + (0, 1.66, 0), (0.48, 0.34, 0.48), n=28, r1=0.36, caps=False, doble=True)
        self.luces.append((tuple(pos + (0, 1.6, 0)), 1.0))

    def planta(self, pos, alto):
        g = self.g
        pos = np.array(pos, float)
        g.cyl('maceta', pos + (0, alto * 0.16, 0), (alto * 0.36, alto * 0.32, alto * 0.36), n=20, r1=0.62)
        g.cyl('tierra', pos + (0, alto * 0.30, 0), (alto * 0.40, 0.012, alto * 0.40), n=20, peso=0.5)
        tallos = 3 + int(self.rnd(0, 3))
        for s in range(tallos):
            a0 = self.rnd(0, 2 * math.pi)
            lean = self.rnd(0.05, 0.28)
            top = alto * self.rnd(0.75, 1.05)
            base = pos + (math.cos(a0) * 0.03, alto * 0.30, math.sin(a0) * 0.03)
            tip = base + (math.cos(a0) * lean * alto, top - alto * 0.30, math.sin(a0) * lean * alto)
            g.beam('tallo', base, tip, 0.012, peso=0.5)
            hojas = 7 + int(self.rnd(0, 5))
            for i in range(hojas):
                f = 0.25 + 0.75 * (i + self.rnd(0, 0.5)) / hojas
                p = base + (tip - base) * f
                ang = a0 + i * 2.39996 + self.rnd(-0.3, 0.3)
                d = np.array([math.cos(ang), self.rnd(0.15, 0.7), math.sin(ang)])
                largo = alto * self.rnd(0.16, 0.26) * (1.15 - 0.4 * f)
                tono = self.rnd(0.75, 1.15)
                g.leaf('hojas', p, d, largo, largo * self.rnd(0.42, 0.6), droop=self.rnd(0.2, 0.7),
                       color=(tono, tono * self.rnd(0.95, 1.1), tono * 0.9), peso=0.9)

    def estante(self, origen, ancho, yaw):
        g = self.g
        grp = Group(origen, yaw)
        paso = 0.38
        g.box('madera', (0, 1.18, 0.22), (ancho, 2.36, 0.07), group=grp, grano=(0, 0, 1))
        for lado in range(2):
            g.box('madera', ((lado - 0.5) * (ancho - 0.08), 1.18, 0), (0.08, 2.36, 0.5), group=grp)
        for fila in range(7):
            g.box('madera', (0, 0.08 + fila * paso, 0), (ancho, 0.055, 0.5), group=grp)
        dens = self.t.get('muebles', 1.0)
        for fila in range(6):
            x = -ancho * 0.5 + 0.10
            hueco = self.rnd(0, 1) < 0.5
            while x < ancho * 0.5 - 0.13:
                bw = (0.03 + self.rnd() * 0.035) / dens
                if x + bw > ancho * 0.5 - 0.08: break
                alto = 0.2 + self.rnd() * 0.125
                fondo = 0.2 + self.rnd() * 0.1
                c = np.array(self.libros[int(self.rnd(0, 8))]) * self.rnd(0.75, 1.15)
                tilt = 0.0
                if hueco and self.rnd() < 0.04:
                    # Hueco con un adorno o libros acostados.
                    if self.rnd() < 0.5:
                        g.cyl('maceta', (x + 0.09, 0.108 + fila * paso + 0.09, -0.03), (0.11, 0.18, 0.11),
                              group=grp, n=14, r1=0.3)
                    else:
                        for k in range(3):
                            ck = np.array(self.libros[int(self.rnd(0, 8))])
                            g.box('libros', (x + 0.12, 0.108 + fila * paso + 0.018 + k * 0.034, -0.03),
                                  (0.24, 0.032, 0.17), e=(0, self.rnd(-8, 8), 0), group=grp, color=tuple(ck), peso=0.6)
                    x += 0.27
                    continue
                if self.rnd() < 0.06: tilt = self.rnd(6, 13)
                g.box('libros', (x + bw * 0.5 + (alto * 0.5 * math.sin(math.radians(tilt))), 0.108 + fila * paso + alto * 0.5, -0.20 + fondo * 0.5),
                      (bw, alto, fondo), e=(0, 0, -tilt), group=grp, color=tuple(c), peso=0.6, tapa=(tilt == 0))
                x += bw + 0.004 + (alto * math.sin(math.radians(tilt)) if tilt else 0)

    def cuadro(self, pos, ancho, alto, yaw, tono=(1, 1, 1)):
        """Cuadro con marco; yaw gira el frente (local −Z) hacia donde debe mirar."""
        g = self.g
        grp = Group(pos, yaw)
        g.box('madera_oscura', (0, 0, 0), (ancho + 0.07, alto + 0.07, 0.035), group=grp)
        g.box('papel', (0, 0, -0.016), (ancho, alto, 0.012), group=grp)
        g.box('cuadro', (0, 0, -0.021), (ancho - 0.16, alto - 0.16, 0.006), group=grp, color=tono, peso=1.6)

    def cortina(self, pos, ancho, yaw):
        """Cortina liviana con pliegues: cuelga desde h−0.45 hasta casi el piso; corre a lo largo de Z."""
        g, h = self.g, self.h
        n = 48
        alto = h - 0.5
        verts, faces = [], []
        for i in range(n + 1):
            f = i / n
            z = f * ancho
            x = 0.035 * math.sin(f * math.pi * 2 * 7) + 0.012 * math.sin(f * 37.0)
            for y in (0.04, alto):
                # Los pliegues se abren un poco hacia abajo.
                k = 1.0 if y > 1 else 1.35
                verts.append((pos[0] + x * k, pos[1] + y, pos[2] + z))
        for i in range(n):
            a = i * 2
            faces.append((a, a + 1, a + 3, a + 2))
        v = np.array(verts)[:, [0, 2, 1]] * np.array([self.g.plan[0, 0], self.g.plan[2, 2], 1.0])
        g.raw('cortina', v, faces, suave=True, doble=True, peso=0.7)
        g.box('metal', (pos[0], pos[1] + alto + 0.02, pos[2] + ancho * 0.5), (0.025, 0.025, ancho + 0.3))

    def puntos(self):
        """Puntos de lectura (ojos, dirección) como en SetReadingPoints; sirven para las vistas previas."""
        h = self.h
        sx, sz = self.g.plan[0, 0], self.g.plan[2, 2]
        pts = [("sofa", (-3.1, 1.2, 0.4), (0, 0, -1)), ("biblioteca", (1.7, 1.2, -1), (0, 0, 1)),
               ("cama", (-2.8, h + 1.2, 2.1), (0, 0, -1)), ("balcon", (-2.8, h + 1.2, -6), (0, 0, -1)),
               ("terraza", (-3.4, 1.32, -7), (0, 0, -1))]
        return [(n, (p[0] * sx, p[1], p[2] * sz), d) for n, p, d in pts]
