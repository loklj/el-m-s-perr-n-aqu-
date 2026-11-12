# Materiales procedurales de Blender para hornear. Todo original: sin texturas ni assets externos.
import bpy


def lin(c):
    """sRGB (como los colores de EnvironmentTheme.cs) a lineal."""
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c[:3])


def mul(c, k):
    return tuple(min(1.0, v * k) for v in c)


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


class NT:
    """Atajos para armar árboles de nodos sin repetir tanto."""

    def __init__(self, mat):
        mat.use_nodes = True
        self.t = mat.node_tree
        self.t.nodes.clear()
        self.out = self.t.nodes.new('ShaderNodeOutputMaterial')
        self.pos = self.t.nodes.new('ShaderNodeNewGeometry').outputs['Position']

    def n(self, kind, **props):
        node = self.t.nodes.new(kind)
        for k, v in props.items(): setattr(node, k, v)
        return node

    def put(self, sock, v):
        if v is None: return
        if hasattr(v, 'is_output') or isinstance(v, bpy.types.NodeSocket): self.t.links.new(v, sock)
        elif isinstance(v, (tuple, list)):
            sock.default_value = tuple(v) + (1.0,) * (len(sock.default_value) - len(v))
        else: sock.default_value = v

    def m(self, op, a, b=None, c=None):
        node = self.n('ShaderNodeMath', operation=op)
        self.put(node.inputs[0], a); self.put(node.inputs[1], b); self.put(node.inputs[2], c)
        return node.outputs[0]

    def ss(self, lo, hi, x):
        """smoothstep(lo, hi, x) con el nodo Map Range."""
        node = self.n('ShaderNodeMapRange', interpolation_type='SMOOTHSTEP')
        self.put(node.inputs['Value'], x); self.put(node.inputs['From Min'], lo); self.put(node.inputs['From Max'], hi)
        return node.outputs['Result']

    def vm(self, op, a, b=None, s=None):
        node = self.n('ShaderNodeVectorMath', operation=op)
        self.put(node.inputs[0], a); self.put(node.inputs[1], b)
        if s is not None: self.put(node.inputs['Scale'], s)
        return node.outputs['Value' if op in ('DOT_PRODUCT', 'LENGTH') else 'Vector']

    def comb(self, x, y, z):
        node = self.n('ShaderNodeCombineXYZ')
        self.put(node.inputs[0], x); self.put(node.inputs[1], y); self.put(node.inputs[2], z)
        return node.outputs[0]

    def noise(self, vec, scale, detail=4.0, rough=0.55, dim='3D'):
        node = self.n('ShaderNodeTexNoise', noise_dimensions=dim)
        self.put(node.inputs['Vector'], vec)
        node.inputs['Scale'].default_value = scale
        node.inputs['Detail'].default_value = detail
        node.inputs['Roughness'].default_value = rough
        return node.outputs['Fac'] if 'Fac' in node.outputs else node.outputs[0]

    def white(self, vec):
        node = self.n('ShaderNodeTexWhiteNoise', noise_dimensions='3D')
        self.put(node.inputs['Vector'], vec)
        return node.outputs['Value']

    def mix(self, fac, a, b, mode='MIX'):
        node = self.n('ShaderNodeMix', data_type='RGBA', blend_type=mode)
        self.put(node.inputs['Factor'], fac); self.put(node.inputs['A'], a); self.put(node.inputs['B'], b)
        return node.outputs['Result']

    def ramp(self, fac, stops):
        node = self.n('ShaderNodeValToRGB')
        el = node.color_ramp.elements
        while len(el) < len(stops): el.new(0.5)
        for e, (p, c) in zip(el, stops):
            e.position = p
            e.color = tuple(c) + (1.0,) if len(c) == 3 else c
        self.put(node.inputs['Fac'], fac)
        return node.outputs['Color']

    def attr(self, name, out='Color'):
        node = self.n('ShaderNodeAttribute', attribute_name=name)
        return node.outputs[out]

    def bump(self, height, strength=0.2, dist=0.002, bevel=0.0):
        node = self.n('ShaderNodeBump')
        node.inputs['Strength'].default_value = strength
        node.inputs['Distance'].default_value = dist
        self.put(node.inputs['Height'], height)
        return node.outputs['Normal']

    def finish(self, color, rough=0.85, normal=None, emit=None, emit_strength=0.0):
        p = self.n('ShaderNodeBsdfPrincipled')
        self.put(p.inputs['Base Color'], color)
        p.inputs['Roughness'].default_value = rough
        if 'Specular IOR Level' in p.inputs: p.inputs['Specular IOR Level'].default_value = 0.25
        if normal is not None: self.t.links.new(normal, p.inputs['Normal'])
        if emit is not None:
            self.put(p.inputs['Emission Color'], emit)
            p.inputs['Emission Strength'].default_value = emit_strength
        self.t.links.new(p.outputs[0], self.out.inputs[0])
        return p


def sep(nt, v):
    node = nt.n('ShaderNodeSeparateXYZ')
    nt.put(node.inputs[0], v)
    return node.outputs


def _solid(name, color, rough=0.85, noise=0.08, scale=60.0, bevel=0.004, bump=0.15):
    mat = bpy.data.materials.new(name); nt = NT(mat)
    f = nt.noise(nt.pos, scale)
    c = nt.mix(f, mul(color, 1 - noise), mul(color, 1 + noise))
    nt.finish(c, rough, nt.bump(f, bump, 0.001, bevel))
    return mat


def _wood(name, color, planks=None, stretch=10.0, scale=22.0, contrast=0.35, bevel=0.004, axis=None):
    """Madera con veta a lo largo del atributo 'grano'. planks=(ancho, largo, junta) hace tablas a lo largo de X."""
    mat = bpy.data.materials.new(name); nt = NT(mat)
    p = nt.pos
    g = axis if axis is not None else nt.attr('grano', 'Vector')
    along = nt.vm('DOT_PRODUCT', p, g)
    q = nt.vm('SUBTRACT', p, nt.vm('SCALE', g, s=nt.m('MULTIPLY', along, 1 - 1 / stretch)))
    tone, gap = 0.5, None
    if planks:
        w, length, joint = planks
        x, y, _ = sep(nt, p)
        row = nt.m('FLOOR', nt.m('DIVIDE', y, w))
        xo = nt.m('ADD', nt.m('DIVIDE', x, length), nt.m('FRACT', nt.m('MULTIPLY', row, 0.37)))
        col = nt.m('FLOOR', xo)
        cell = nt.comb(col, row, 3.0)
        tone = nt.white(cell)
        q = nt.vm('ADD', q, nt.vm('SCALE', cell, s=3.17))  # Veta distinta en cada tabla.
        fy = nt.m('FRACT', nt.m('DIVIDE', y, w))
        fx = nt.m('FRACT', xo)
        gap = nt.m('MAXIMUM', nt.m('LESS_THAN', fy, joint / w), nt.m('LESS_THAN', fx, joint / length))
    fine = nt.noise(q, scale, 5.0, 0.6)
    rings = nt.noise(q, scale * 0.22, 2.0, 0.5)
    grain = nt.m('ADD', nt.m('MULTIPLY', fine, 0.6), nt.m('MULTIPLY', rings, 0.4))
    c = nt.ramp(grain, [(0.25, mul(color, 1 - contrast)), (0.55, color), (0.8, mul(color, 1 + contrast * 0.5))])
    if planks:
        c = nt.mix(nt.m('MULTIPLY', tone, 0.55), c, mul(color, 0.62), 'MIX')
        c = nt.mix(nt.m('MULTIPLY', gap, 0.85), c, mul(color, 0.12))
    nt.finish(c, 0.7, nt.bump(grain, 0.12, 0.001, bevel))
    return mat


def _fabric(name, color, scale=420.0, var=0.1):
    mat = bpy.data.materials.new(name); nt = NT(mat)
    weave = nt.noise(nt.pos, scale, 2.0, 0.7)
    blot = nt.noise(nt.pos, 6.0, 3.0)
    f = nt.m('ADD', nt.m('MULTIPLY', weave, 0.6), nt.m('MULTIPLY', blot, 0.4))
    c = nt.mix(f, mul(color, 1 - var), mul(color, 1 + var))
    nt.finish(c, 0.95, nt.bump(weave, 0.25, 0.0008))
    return mat


def _rug(name, base, line, period=0.32, width=0.018):
    mat = bpy.data.materials.new(name); nt = NT(mat)
    x, y, _ = sep(nt, nt.pos)
    u = nt.m('ADD', x, y); v = nt.m('SUBTRACT', x, y)
    def band(t):
        f = nt.m('FRACT', nt.m('DIVIDE', t, period))
        return nt.m('LESS_THAN', nt.m('ABSOLUTE', nt.m('SUBTRACT', f, 0.5)), width / period)
    lines = nt.m('MAXIMUM', band(u), band(v))
    weave = nt.noise(nt.pos, 300.0, 2.0, 0.7)
    blot = nt.noise(nt.pos, 3.0, 3.0)
    c = nt.mix(lines, base, line)
    shade = nt.mix(nt.m('MULTIPLY', nt.m('ADD', weave, blot), 0.5), (0.78, 0.78, 0.78), (1.18, 1.18, 1.18))
    c = nt.mix(1.0, c, shade, 'MULTIPLY')
    nt.finish(c, 1.0, nt.bump(weave, 0.3, 0.001))
    return mat


def crear(t):
    """Todos los materiales de la casa para el tema t (colores sRGB en t)."""
    pared, madera, tela = lin(t['pared']), lin(t['madera']), lin(t['tela'])
    metal, hoja = lin(t['metal']), lin(t['follaje'])
    acento = lin(t.get('acento', (0.80, 0.62, 0.30)))
    crema = lerp(pared, (0.85, 0.82, 0.76), 0.5)
    m = {}
    m['pared'] = _solid('pared', pared, 0.92, 0.035, 35.0, 0.006)
    m['techo'] = _solid('techo', lerp(pared, (0.9, 0.9, 0.9), 0.6), 0.95, 0.02, 30.0, 0.003)
    m['hormigon'] = _solid('hormigon', (0.42, 0.41, 0.39), 0.9, 0.12, 14.0, 0.008, 0.4)
    m['suelo'] = _wood('suelo', madera, planks=(0.14, 1.9, 0.0025), contrast=0.28)
    m['deck'] = _wood('deck', lerp(madera, (0.36, 0.33, 0.30), 0.55), planks=(0.125, 3.2, 0.006), contrast=0.4)
    m['madera'] = _wood('madera', mul(madera, 0.9))
    m['madera_oscura'] = _wood('madera_oscura', mul(madera, 0.32))
    m['metal'] = _solid('metal', lerp(metal, (0.03, 0.03, 0.03), 0.3), 0.45, 0.05, 80.0, 0.002, 0.05)
    m['tela'] = _fabric('tela', tela)
    m['tela_clara'] = _fabric('tela_clara', crema, var=0.06)
    m['acento'] = _fabric('acento', acento)
    m['alfombra'] = _rug('alfombra', lerp(tela, crema, 0.55), mul(crema, 1.1))
    m['alfombra2'] = _rug('alfombra2', mul(crema, 0.95), lerp(tela, crema, 0.4), 0.45, 0.03)
    m['cortina'] = _fabric('cortina', (0.86, 0.85, 0.82), 260.0, 0.04)
    m['papel'] = _solid('papel', (0.88, 0.87, 0.84), 0.95, 0.01, 50.0, 0.0)
    m['maceta'] = _solid('maceta', lerp(crema, (0.75, 0.5, 0.36), t.get('terracota', 0.35)), 0.6, 0.05, 25.0, 0.003)
    m['tierra'] = _solid('tierra', (0.05, 0.035, 0.025), 1.0, 0.3, 90.0, 0.0, 0.5)
    m['tallo'] = _solid('tallo', lerp(hoja, (0.2, 0.14, 0.08), 0.5), 0.8, 0.1, 60.0, 0.0)

    # Libros: color por pieza; el canto de las hojas (cara superior) va en papel.
    mat = bpy.data.materials.new('libros'); nt = NT(mat)
    geo = nt.t.nodes['Geometry']
    up = nt.m('GREATER_THAN', sep(nt, geo.outputs['Normal'])[2], 0.9)
    lines = nt.m('LESS_THAN', nt.m('FRACT', nt.m('MULTIPLY', sep(nt, nt.pos)[2], 9.0)), 0.06)
    c = nt.mix(nt.m('MULTIPLY', lines, 0.6), nt.attr('col'), (0.75, 0.62, 0.3))
    c = nt.mix(up, c, (0.82, 0.78, 0.68))
    nt.finish(c, 0.8, nt.bump(nt.noise(nt.pos, 200.0), 0.1, 0.0005, 0.0015))
    m['libros'] = mat

    # Hojas de las plantas: verde del tema por el tono de cada hoja.
    mat = bpy.data.materials.new('hojas'); nt = NT(mat)
    c = nt.mix(1.0, nt.attr('col'), mul(hoja, 0.9), 'MULTIPLY')
    nt.finish(c, 0.55)
    m['hojas'] = mat

    # Pantalla de lámpara: se enciende en los temas con lámparas.
    mat = bpy.data.materials.new('pantalla'); nt = NT(mat)
    nt.finish((0.86, 0.80, 0.68), 0.9, None, (1.0, 0.72, 0.42), t.get('lamparas', 0.0) * 6.0)
    m['pantalla'] = mat

    # Cuadros: manchas abstractas con la paleta del tema.
    mat = bpy.data.materials.new('cuadro'); nt = NT(mat)
    warp = nt.noise(nt.pos, 1.3, 3.0, 0.6)
    f = nt.noise(nt.vm('ADD', nt.pos, nt.comb(warp, warp, nt.m('MULTIPLY', warp, 2.0))), 1.9, 5.0, 0.62)
    c = nt.ramp(f, [(0.28, mul(tela, 0.5)), (0.42, crema), (0.52, acento), (0.62, lerp(hoja, crema, 0.3)), (0.75, mul(metal, 1.2))])
    c = nt.mix(1.0, c, nt.attr('col'), 'MULTIPLY')
    nt.finish(c, 0.9, nt.bump(nt.noise(nt.pos, 90.0), 0.2, 0.001))
    m['cuadro'] = mat
    return m
