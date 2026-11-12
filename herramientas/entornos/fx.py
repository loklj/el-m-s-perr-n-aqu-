# Folio XR — utilidades para generar entornos en Blender (bpy como módulo de Python).
# La geometría se escribe en coordenadas de Unity (x derecha, y arriba, z adelante) para que
# coincida con los colisionadores y puntos de lectura de HouseGenerator.cs; aquí se pasa a Blender.
import math
import numpy as np
import bpy

SWAP = np.array([[1, 0, 0, 0], [0, 0, 1, 0], [0, 1, 0, 0], [0, 0, 0, 1]], float)  # Unity <-> Blender


def T(p):
    m = np.eye(4); m[:3, 3] = p; return m


def Sc(s):
    m = np.eye(4); m[0, 0], m[1, 1], m[2, 2] = s; return m


def Rx(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    m = np.eye(4); m[1, 1], m[1, 2], m[2, 1], m[2, 2] = c, -s, s, c; return m


def Ry(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    m = np.eye(4); m[0, 0], m[0, 2], m[2, 0], m[2, 2] = c, s, -s, c; return m


def Rz(d):
    c, s = math.cos(math.radians(d)), math.sin(math.radians(d))
    m = np.eye(4); m[0, 0], m[0, 1], m[1, 0], m[1, 1] = c, -s, s, c; return m


def Euler(x, y, z):
    """Igual que Quaternion.Euler de Unity (orden Z, X, Y)."""
    return Ry(y) @ Rx(x) @ Rz(z)


def TRS(p, e=(0, 0, 0), s=(1, 1, 1)):
    return T(p) @ Euler(*e) @ Sc(s)


def Group(p, yaw=0.0):
    return T(p) @ Ry(yaw)


def FromUp(d):
    """Rotación que lleva (0,1,0) a la dirección d (como Quaternion.FromToRotation)."""
    d = np.asarray(d, float); d = d / np.linalg.norm(d)
    up = np.array([0.0, 1.0, 0.0])
    v = np.cross(up, d); c = float(np.dot(up, d))
    m = np.eye(4)
    if np.linalg.norm(v) < 1e-9:
        if c < 0: m[:3, :3] = np.diag([1, -1, -1])
        return m
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    m[:3, :3] = np.eye(3) + k + k @ k * (1.0 / (1.0 + c))
    return m


# ───────────── Plantillas (en ejes de Unity, caras con normal hacia fuera en Blender) ─────────────
def _to_blender(verts_u):
    v = np.asarray(verts_u, float)
    return v[:, [0, 2, 1]]


_CUBE_B = np.array([(-.5, -.5, -.5), (.5, -.5, -.5), (.5, .5, -.5), (-.5, .5, -.5),
                    (-.5, -.5, .5), (.5, -.5, .5), (.5, .5, .5), (-.5, .5, .5)], float)
_CUBE_F = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]


def _cyl(n=20, r0=0.5, r1=0.5, caps=True, estrias=0.0):
    """Cilindro o tronco de cono, eje Z de Blender (= Y de Unity), altura 1 centrada.
    estrias > 0 alterna el radio de los vértices (costillas de cactus)."""
    a = np.linspace(0, 2 * math.pi, n, endpoint=False)
    k = 1.0 - estrias * (np.arange(n) % 2)
    bottom = np.stack([np.cos(a) * r0 * k, np.sin(a) * r0 * k, np.full(n, -0.5)], 1)
    top = np.stack([np.cos(a) * r1 * k, np.sin(a) * r1 * k, np.full(n, 0.5)], 1)
    v = np.concatenate([bottom, top])
    f = [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
    if caps:
        f.append(tuple(range(n - 1, -1, -1)))
        f.append(tuple(range(n, 2 * n)))
    return v, f


class Geo:
    """Acumula geometría por grupo: (atlas, material, peso de texels, suave, doble cara)."""

    def __init__(self, plan=(1.0, 1.0)):
        self.groups = {}
        self.plan = Sc((plan[0], 1.0, plan[1]))  # _planScale de la casa
        self.boxes = []  # Matrices (Blender) de las cajas: sirven para quitar caras tapadas.
        self.rng = np.random.default_rng(1)

    def _g(self, key):
        g = self.groups.get(key)
        if g is None:
            g = self.groups[key] = dict(v=[], f=[], c=[], d=[], n=0)
        return g

    def raw(self, mat, verts_b, faces, atlas='casa', peso=1.0, suave=False, doble=False,
            color=(1, 1, 1), grano=(1, 0, 0), uv=None):
        """Añade vértices ya en coordenadas de Blender. uv: una coordenada por vértice, en metros."""
        g = self._g((atlas, mat, peso, suave, doble))
        if uv is not None: g.setdefault('uv', []).append(np.asarray(uv, float))
        base = g['n']
        g['v'].append(np.asarray(verts_b, float))
        por_cara = hasattr(color, 'shape') and getattr(color, 'ndim', 1) == 2
        for k, f in enumerate(faces):
            g['f'].append(tuple(base + i for i in f))
            g['c'].append(tuple(color[k]) if por_cara else color); g['d'].append(grano)
        g['n'] += len(verts_b)

    def mesh(self, mat, m_u, verts_b, faces, plan=True, **kw):
        """Plantilla en Blender + matriz de Unity (como Add/AddWorld de HouseGenerator)."""
        m = (self.plan @ m_u) if plan else m_u
        mb = SWAP @ m @ SWAP
        v = np.c_[verts_b, np.ones(len(verts_b))] @ mb.T
        if 'grano' not in kw:
            # Veta a lo largo del eje más largo de la pieza (en Blender).
            axes = mb[:3, :3]
            lengths = np.linalg.norm(axes, axis=0)
            a = axes[:, int(np.argmax(lengths))]
            kw['grano'] = tuple(a / (np.linalg.norm(a) + 1e-12))
        self.raw(mat, v[:, :3], faces, **kw)

    def box(self, mat, pos, size, e=(0, 0, 0), group=None, **kw):
        m = TRS(pos, e, size)
        if group is not None: m = group @ m
        if kw.get('tapa', True):
            self.boxes.append(SWAP @ ((self.plan @ m) if kw.get('plan', True) else m) @ SWAP)
        kw.pop('tapa', None)
        self.mesh(mat, m, _CUBE_B, _CUBE_F, **kw)

    def cyl(self, mat, pos, size, e=(0, 0, 0), group=None, n=20, r1=0.5, caps=True, m=None, estrias=0.0, **kw):
        v, f = _cyl(n, 0.5, r1, caps, estrias)
        mm = TRS(pos, e, size) if m is None else m
        if group is not None: mm = group @ mm
        kw.setdefault('suave', True)
        self.mesh(mat, mm, v, f, **kw)

    def beam(self, mat, a, b, w, **kw):
        a = np.asarray(a, float); b = np.asarray(b, float); d = b - a
        m = T((a + b) * 0.5) @ FromUp(d) @ Sc((w, np.linalg.norm(d), w))
        self.mesh(mat, m, _CUBE_B, _CUBE_F, **kw)

    def soft(self, mat, pos, size, e=(0, 0, 0), group=None, r=0.04, bulge=(0, 0, 0), n=5, plan=True, **kw):
        """Caja redondeada y, si se pide, inflada (cojines, colchón, almohadas). Tamaños en metros."""
        h = np.asarray(size, float) * 0.5
        r = min(r, h.min() * 0.98)
        coords = []
        for a in range(3):
            inner = np.linspace(-(h[a] - r), h[a] - r, n)
            coords.append(np.concatenate([[-h[a], -h[a] + 0.35 * r], inner, [h[a] - 0.35 * r, h[a]]]))
        index, verts, faces = {}, [], []

        def vid(i, j, k):
            key = (i, j, k)
            if key not in index:
                index[key] = len(verts)
                verts.append((coords[0][i], coords[1][j], coords[2][k]))
            return index[key]

        m_ = len(coords[0]) - 1
        for axis in range(3):
            b, c = (axis + 1) % 3, (axis + 2) % 3
            for side in (0, m_):
                for i in range(m_):
                    for j in range(m_):
                        def at(u, w):
                            idx = [0, 0, 0]; idx[axis] = side; idx[b] = u; idx[c] = w
                            return vid(*idx)
                        q = (at(i, j), at(i + 1, j), at(i + 1, j + 1), at(i, j + 1))
                        faces.append(q if side == m_ else q[::-1])
        p = np.array(verts, float)
        inner = np.clip(p, -(h - r), h - r)
        d = p - inner
        ln = np.linalg.norm(d, axis=1, keepdims=True)
        v = np.where(ln > 1e-9, inner + r * d / np.maximum(ln, 1e-9), p)
        for a in range(3):
            if bulge[a] == 0: continue
            b, c = (a + 1) % 3, (a + 2) % 3
            w = (1 - (p[:, b] / h[b]) ** 2) * (1 - (p[:, c] / h[c]) ** 2)
            v[:, a] += np.sign(p[:, a]) * (np.abs(p[:, a]) / h[a]) ** 2 * bulge[a] * np.clip(w, 0, 1)
        m = TRS(pos, e)
        if group is not None: m = group @ m
        if plan: m = self.plan @ m
        mb = SWAP @ m @ SWAP
        vb = np.c_[v[:, [0, 2, 1]], np.ones(len(v))] @ mb.T
        # Orienta cada cara hacia fuera en coordenadas de Blender (la forma es convexa).
        lb = v[:, [0, 2, 1]]
        fixed = []
        for f in faces:
            a_, b_, c_ = lb[f[0]], lb[f[1]], lb[f[2]]
            fixed.append(f if np.dot(np.cross(b_ - a_, c_ - a_), (a_ + b_ + c_) / 3) > 0 else f[::-1])
        faces = fixed
        kw.setdefault('suave', True)
        self.raw(mat, vb[:, :3], faces, **kw)

    def leaf(self, mat, base, direction, length, width, droop=0.3, **kw):
        """Hoja de 4 triángulos con pliegue central, en coordenadas de Unity (sin escala de planta)."""
        base = np.asarray(base, float) * np.array([self.plan[0, 0], 1.0, self.plan[2, 2]])
        d = np.asarray(direction, float); d = d / np.linalg.norm(d)
        side = np.cross(d, [0, 1, 0])
        if np.linalg.norm(side) < 1e-6: side = np.array([1.0, 0, 0])
        side /= np.linalg.norm(side)
        up = np.cross(side, d)
        mid = base + d * length * 0.5 + up * length * 0.06
        tip = base + d * length - up * length * droop * 0.5
        l = mid - up * width * 0.12 + side * width * 0.5
        r_ = mid - up * width * 0.12 - side * width * 0.5
        v = _to_blender([base, l, tip, r_, mid])
        kw.setdefault('doble', True)
        self.raw(mat, v, [(0, 4, 1), (1, 4, 2), (2, 4, 3), (3, 4, 0)], **kw)

    # ───────────── Objetos de Blender ─────────────
    def build(self):
        """Crea un objeto por grupo. Devuelve [(obj, atlas, peso, doble)]."""
        out = []
        for (atlas, mat, peso, suave, doble), g in self.groups.items():
            if not g['f']: continue
            me = bpy.data.meshes.new(f'{atlas}_{mat}')
            me.from_pydata(np.concatenate(g['v']).tolist(), [], g['f'])
            me.validate(); me.update()
            col = me.color_attributes.new('col', 'FLOAT_COLOR', 'CORNER')
            dirs = me.attributes.new('grano', 'FLOAT_VECTOR', 'CORNER')
            counts = np.array([len(f) for f in g['f']])
            cc = np.repeat(np.c_[np.array(g['c'], float), np.ones(len(g['c']))], counts, axis=0)
            dd = np.repeat(np.array(g['d'], float), counts, axis=0)
            col.data.foreach_set('color', cc.ravel()); dirs.data.foreach_set('vector', dd.ravel())
            if suave:
                me.polygons.foreach_set('use_smooth', [True] * len(me.polygons))
            propia = 'uv' in g and len(g['uv']) == len(g['v'])
            if propia:
                por_vertice = np.concatenate(g['uv'])
                lazos = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lazos)
                capa = me.uv_layers.new(name='UVMap')
                capa.data.foreach_set('uv', por_vertice[lazos].astype(np.float32).ravel())
            ob = bpy.data.objects.new(me.name, me)
            bpy.context.scene.collection.objects.link(ob)
            ob['atlas'], ob['peso'], ob['doble'], ob['mat'] = atlas, peso, doble, mat
            ob['uv_propia'] = bool(propia)
            out.append(ob)
        return out


def remove_hidden(objs, boxes, eps=0.0015):
    """Quita caras tapadas por una caja pegada (bases sobre el piso, libros contra el estante...)."""
    import bmesh
    if not boxes: return 0
    inv = np.array([np.linalg.inv(m) for m in boxes])
    corners = np.c_[_CUBE_B, np.ones(8)]
    world = np.einsum('bij,kj->bki', np.array(boxes), corners)[:, :, :3]
    lo, hi = world.min(1) - 1e-4, world.max(1) + 1e-4
    removed = 0
    for ob in objs:
        me = ob.data
        if not len(me.polygons): continue
        pts, owner = [], []
        for poly in me.polygons:
            n = np.array(poly.normal); c = np.array(poly.center)
            vs = [np.array(me.vertices[i].co) for i in poly.vertices]
            for p in [c] + [c + (v - c) * 0.8 for v in vs[:4]]:
                pts.append(p + n * eps); owner.append(poly.index)
        pts = np.array(pts); owner = np.array(owner)
        inside = np.zeros(len(pts), bool)
        for b in range(len(boxes)):
            cand = np.nonzero(np.all((pts >= lo[b]) & (pts <= hi[b]), axis=1) & ~inside)[0]
            if not len(cand): continue
            local = np.c_[pts[cand], np.ones(len(cand))] @ inv[b].T
            inside[cand[np.all(np.abs(local[:, :3]) <= 0.5 + 1e-6, axis=1)]] = True
        total = np.bincount(owner, minlength=len(me.polygons))
        hit = np.bincount(owner, weights=inside, minlength=len(me.polygons))
        drop = np.nonzero(hit == total)[0]
        if len(drop):
            bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[bm.faces[int(i)] for i in drop], context='FACES')
            bm.to_mesh(me); bm.free(); me.update()
            removed += len(drop)
    return removed
