#!/usr/bin/env python3
# Folio XR — genera un entorno horneado con Blender (pip install bpy numpy opencv-python).
#
#   python3 generar.py moderna-calida --vista sofa,terraza        # vistas rápidas de la escena
#   python3 generar.py moderna-calida --todo                      # hornea, panorama, exporta y comprueba
#
# Salida: hito1/Assets/FolioXR/Resources/Entornos/<tema>/ (mallas .bytes, texturas .jpg y datos.json).
# Los colisionadores y puntos de lectura siguen saliendo de HouseGenerator.cs: aquí solo lo que se ve.
import argparse
import json
import math
import os
import sys
import time

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import numpy as np
import bpy
import cv2
from mathutils import Vector

import fx, casa, materiales, paisaje, cielo, temas

REPO = os.path.abspath(os.path.join(AQUI, '..', '..'))
SALIDA = os.path.join(REPO, 'hito1', 'Assets', 'FolioXR', 'Resources', 'Entornos')
CACHE = os.environ.get('FOLIO_CACHE', '/tmp/bl/cache')
CELDAS = {'follaje', 'hojas', 'pasto_hoja'}
CELDA_PX = 4


def log(*a):
    print('[entorno]', *a, flush=True)


def ub(p):
    """Punto o dirección de Unity a Blender."""
    return Vector((p[0], p[2], p[1]))


# ───────────── Escena ─────────────
def escena(t, lejos=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 6
    sc.cycles.diffuse_bounces = 4
    sc.cycles.glossy_bounces = 3
    sc.cycles.caustics_reflective = sc.cycles.caustics_refractive = False
    sc.view_settings.view_transform = 'Raw'
    sc.render.image_settings.file_format = 'OPEN_EXR'
    sc.render.image_settings.color_depth = '32'
    sc.render.resolution_percentage = 100
    g = fx.Geo((t['ancho'] / 12, t['fondo'] / 10))
    c = casa.Casa(g, t).construir()
    P = paisaje.Paisaje(t).construir()
    casa_objs = g.build()
    quitadas = fx.remove_hidden(casa_objs, g.boxes)
    cerca = P.g.build()
    lejos_objs = P.lejos.build()
    mats = materiales.crear(t)
    mats.update(paisaje.materiales(t))
    var_objs = [gv.build() for gv in P.far_variants]
    for o in casa_objs + cerca + lejos_objs + [x for grp in var_objs for x in grp]:
        o.data.materials.append(mats[o['mat']])
    inst = paisaje.instanciar(var_objs, P.far_points) if lejos else []
    if not lejos:
        for grp in var_objs:
            for o in grp: o.hide_render = True
    ctx = dict(t=t, casa=c, P=P, casa_objs=casa_objs, cerca=cerca, lejos=lejos_objs, inst=inst, mats=mats)
    log(f"casa: {sum(len(o.data.polygons) for o in casa_objs)} caras ({quitadas} tapadas quitadas); "
        f"cerca: {sum(len(o.data.polygons) for o in cerca)}; árboles lejanos: {len(P.far_points[0])}")
    mundo(ctx)
    return ctx


def mundo(ctx):
    t, sc = ctx['t'], bpy.context.scene
    w = bpy.data.worlds.new('mundo'); sc.world = w; w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes['Background']
    s = cielo.sol(t)
    tipo = t['cielo']
    if tipo != 'noche':
        sky = nt.nodes.new('ShaderNodeTexSky')
        sky.sky_type = 'MULTIPLE_SCATTERING'
        sky.sun_elevation = math.radians(t['elev'])
        sky.sun_rotation = math.radians(90 - t['acimut'])
        sky.sun_disc = True
        sky.sun_size = math.radians(2.4)      # Sol algo más grande: sombras con borde suave.
        if tipo == 'atardecer': sky.aerosol_density = 2.2
        if tipo == 'nublado':
            sky.sun_intensity = 0.04
            hs = nt.nodes.new('ShaderNodeHueSaturation'); hs.inputs['Saturation'].default_value = 0.2
            nt.links.new(sky.outputs[0], hs.inputs['Color']); nt.links.new(hs.outputs[0], bg.inputs[0])
            bg.inputs[1].default_value = 1.7
        else:
            nt.links.new(sky.outputs[0], bg.inputs[0])
        ctx['sky'] = sky
    else:
        bg.inputs[0].default_value = (0.011, 0.017, 0.038, 1)
        bg.inputs[1].default_value = 1.0
        luna = bpy.data.lights.new('luna', 'SUN'); luna.energy = 0.06
        luna.color = materiales.lin(t['sol']); luna.angle = math.radians(1.5)
        lo = bpy.data.objects.new('luna', luna); sc.collection.objects.link(lo)
        lo.rotation_euler = Vector(s).to_track_quat('Z', 'Y').to_euler()
    for p in getattr(ctx['P'], 'farolas', []):
        li = bpy.data.lights.new('farola', 'POINT')
        li.energy = 14.0; li.color = (1.0, 0.78, 0.5); li.shadow_soft_size = 0.12
        ob = bpy.data.objects.new('farola', li); sc.collection.objects.link(ob)
        ob.location = ub(p)
    pot = t.get('lamparas', 0.0)
    if pot > 0:
        sx, sz = t['ancho'] / 12, t['fondo'] / 10
        for pos, k in ctx['casa'].luces:
            li = bpy.data.lights.new('lampara', 'POINT')
            li.energy = (9.0 if tipo == 'noche' else 220.0) * pot * k
            li.color = (1.0, 0.70, 0.42); li.shadow_soft_size = 0.09
            ob = bpy.data.objects.new('lampara', li); sc.collection.objects.link(ob)
            ob.location = ub((pos[0] * sx, pos[1], pos[2] * sz))


def leer_exr(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)


def guardar_jpg(path, rgb8, calidad=92):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    cv2.imwrite(path, rgb8[::-1, :, ::-1], [cv2.IMWRITE_JPEG_QUALITY, calidad])


def render(path, w, h, spp, transparente=False):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.cycles.samples = spp
    sc.render.film_transparent = transparente
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.filepath = path
    t0 = time.time()
    bpy.ops.render.render(write_still=True)
    log(f'render {os.path.basename(path)} {w}x{h} {spp} spp: {time.time() - t0:.0f} s')
    return leer_exr(path + '.exr')


def camara(pano=False):
    sc = bpy.context.scene
    co = sc.camera
    if co is None:
        cam = bpy.data.cameras.new('cam'); co = bpy.data.objects.new('cam', cam)
        sc.collection.objects.link(co); sc.camera = co
    cam = co.data
    cam.clip_start, cam.clip_end = 0.05, 100000
    if pano:
        cam.type = 'PANO'; cam.panorama_type = 'EQUIRECTANGULAR'
        co.rotation_euler = (math.pi / 2, 0, 0)
    else:
        cam.type = 'PERSP'; cam.lens = 15; cam.sensor_width = 36
    return co


def mirar(co, ojo, direccion):
    co.location = ub(ojo)
    co.rotation_euler = ub(direccion).to_track_quat('-Z', 'Y').to_euler()


def vistas(ctx, nombres, spp, ancho=960, sufijo=''):
    co = camara()
    extra = {'jardin': ((2.5, 1.6, -12.5), (-0.5, 0.05, 0.8)), 'arriba': ((-16, 9, -20), (0.55, -0.3, 0.75))}
    pts = {n: (e, d) for n, e, d in ctx['casa'].puntos()}
    pts.update(extra)
    for n in nombres:
        ojo, d = pts[n]
        mirar(co, ojo, d)
        a = render(f'{CACHE}/vista_{n}', ancho, int(ancho * 0.62), spp)[..., :3]
        lum = a @ np.array([0.2126, 0.7152, 0.0722])
        E = ctx.get('E') or 0.30 / max(1e-6, float(np.percentile(lum, 55)))
        out = f'{CACHE}/vista_{ctx["t"]["slug"]}_{n}{sufijo}.jpg'
        guardar_jpg(out, cielo.a_srgb(a, E), 88)
        log('vista', out, f'E={E:.3f}')


def exposicion(ctx):
    """Exposición común del tema: se mide en vistas pequeñas desde los puntos de lectura, como haría una cámara."""
    t = ctx['t']
    co = camara()
    lums = []
    for n, ojo, d in ctx['casa'].puntos():
        if n not in ('sofa', 'terraza', 'cama'): continue
        mirar(co, ojo, d)
        a = render(f'{CACHE}/medir', 256, 160, 12)[..., :3]
        lums.append((a @ np.array([0.2126, 0.7152, 0.0722])).ravel())
    objetivo = dict(dia=0.27, atardecer=0.22, nublado=0.27, noche=0.075)[t['cielo']] * t.get('exp', 1.0)
    E = objetivo / max(1e-6, float(np.percentile(np.concatenate(lums), 55)))
    log(f'exposición E={E:.4f}')
    return E


# ───────────── UV, horneado y exportación ─────────────
def _activar(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def _uv(me):
    n = len(me.loops)
    a = np.empty(n * 2, np.float32); me.uv_layers.active.data.foreach_get('uv', a)
    return a.reshape(n, 2)


def desenvolver(ob):
    """UV métrica: 1 unidad = 1 m × peso. Las hojas quedan pendientes (celdas)."""
    me = ob.data
    doble = me.attributes.new('doble', 'INT', 'FACE')
    doble.data.foreach_set('value', [1 if ob['doble'] else 0] * len(me.polygons))
    celda = me.attributes.new('celda', 'INT', 'FACE')
    es_celda = ob['mat'] in CELDAS
    celda.data.foreach_set('value', [1 if es_celda else 0] * len(me.polygons))
    if not me.uv_layers: me.uv_layers.new(name='UVMap')
    if es_celda: return
    if ob.get('uv_propia'):
        uv = _uv(me)
        me.uv_layers.active.data.foreach_set('uv', (uv * float(ob['peso'])).ravel())
        return
    _activar(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(42), island_margin=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    uv = _uv(me)
    a3 = np.empty(len(me.polygons), np.float32); me.polygons.foreach_get('area', a3)
    ls = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_total', lt)
    nxt = np.arange(len(uv)) + 1
    nxt[ls + lt - 1] = ls
    cross = uv[:, 0] * uv[nxt, 1] - uv[nxt, 0] * uv[:, 1]
    auv = abs(float(cross.sum())) * 0.5
    if auv <= 0: return
    k = math.sqrt(float(a3.sum()) / auv) * float(ob['peso'])
    me.uv_layers.active.data.foreach_set('uv', (uv * k).ravel())


def unir_y_empaquetar(objs, nombre, tam):
    """Une los objetos de un atlas, empaqueta las islas y reparte las celdas de hojas en una franja."""
    for o in objs: desenvolver(o)
    _activar(objs[0])
    for o in objs: o.select_set(True)
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = nombre
    me = ob.data
    n = len(me.polygons)
    celda = np.empty(n, np.int32); me.attributes['celda'].data.foreach_get('value', celda)
    ls = np.empty(n, np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(n, np.int32); me.polygons.foreach_get('loop_total', lt)
    # Empaqueta solo las caras normales.
    me.polygons.foreach_set('hide', (celda == 1).tolist())
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=False, margin_method='FRACTION', margin=0.001, shape_method='AABB', scale=True)
    bpy.ops.mesh.reveal()
    bpy.ops.object.mode_set(mode='OBJECT')
    uv = _uv(me)
    cells = np.nonzero(celda == 1)[0]
    cols = tam // CELDA_PX
    filas = int(math.ceil(len(cells) / cols))
    vc = filas * CELDA_PX / tam
    por_lazo = np.repeat(celda, lt)
    uv[por_lazo == 0] = uv[por_lazo == 0] * (1 - vc) + np.array([0.0, vc])
    if len(cells):
        px = 1.0 / tam
        plantilla = {3: [(0.8, 0.8), (3.2, 0.8), (2.0, 3.2)], 4: [(0.8, 0.8), (3.2, 0.8), (3.2, 3.2), (0.8, 3.2)]}
        for i, f in enumerate(cells):
            cx, cy = (i % cols) * CELDA_PX, (i // cols) * CELDA_PX
            pts = plantilla.get(int(lt[f]), plantilla[4])
            for k in range(int(lt[f])):
                p = pts[min(k, len(pts) - 1)]
                uv[ls[f] + k] = ((cx + p[0]) * px, (cy + p[1]) * px)
    me.uv_layers.active.data.foreach_set('uv', uv.ravel())
    a3 = np.empty(n, np.float32); me.polygons.foreach_get('area', a3)
    nxt = np.arange(len(uv)) + 1
    nxt[ls + lt - 1] = ls
    cruz = uv[:, 0] * uv[nxt, 1] - uv[nxt, 0] * uv[:, 1]
    auv = np.abs(np.add.reduceat(cruz, ls)) * 0.5
    normal = celda == 0
    densidad = math.sqrt(auv[normal].sum() / max(1e-9, a3[normal].sum())) * tam
    log(f'{nombre}: {n} caras, {len(cells)} celdas de hoja ({filas} filas), área {a3[normal].sum():.0f} m², '
        f'ocupación {auv[normal].sum() * 100:.0f} %, {densidad:.0f} texels/m de media')
    ob['texels'] = float(densidad)
    return ob


def hornear(ob, tam, spp):
    sc = bpy.context.scene
    img = bpy.data.images.new('luz_' + ob.name, tam, tam, alpha=False, float_buffer=True)
    for slot in ob.material_slots:
        nt = slot.material.node_tree
        node = nt.nodes.get('horno') or nt.nodes.new('ShaderNodeTexImage')
        node.name = 'horno'; node.image = img
        nt.nodes.active = node
    _activar(ob)
    sc.cycles.samples = spp
    sc.render.bake.margin = 6
    sc.render.bake.margin_type = 'EXTEND'
    t0 = time.time()
    bpy.ops.object.bake(type='COMBINED', pass_filter={'DIFFUSE', 'EMIT', 'DIRECT', 'INDIRECT'}, use_clear=True)
    log(f'horneado {ob.name} {tam}² {spp} spp: {time.time() - t0:.0f} s')
    a = np.empty(tam * tam * 4, np.float32); img.pixels.foreach_get(a)
    return a.reshape(tam, tam, 4)[..., :3]


def horneado_guardado(ob, tam, spp, path):
    """Hornea o, si una corrida anterior se cortó después de este paso, reutiliza su resultado."""
    firma = f'{len(ob.data.polygons)}-{len(ob.data.loops)}-{spp}'
    if os.path.exists(path) and os.path.exists(path + '.txt') and open(path + '.txt').read() == firma:
        log('se reutiliza', os.path.basename(path))
        return np.load(path).astype(np.float32)
    luz = hornear(ob, tam, spp)
    np.save(path, luz.astype(np.float16))
    with open(path + '.txt', 'w') as f: f.write(firma)
    return luz


def exportar(ob, path):
    """FXM1: uint32 vértices, uint32 índices, posiciones float32 (Unity), UV float32, índices uint32."""
    me = ob.data
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tri = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get('loops', tri)
    tpoly = np.empty(nt, np.int32); me.loop_triangles.foreach_get('polygon_index', tpoly)
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    uv = _uv(me)
    doble = np.empty(len(me.polygons), np.int32); me.attributes['doble'].data.foreach_get('value', doble)
    clave = np.c_[lv[tri].astype(np.float64), np.round(uv[tri] * 65536)]
    _, primero, inv = np.unique(clave, axis=0, return_index=True, return_inverse=True)
    pos = co[lv[tri[primero]]][:, [0, 2, 1]]          # Blender (x, y, z) → Unity (x, z, y)
    uvs = uv[tri[primero]]
    idx = inv.reshape(-1, 3)[:, [0, 2, 1]]            # El intercambio de ejes invierte el sentido.
    # Las hojas y telas finas se ven por ambos lados porque el material de Unity no descarta caras
    # traseras (Cull Off); duplicar triángulos aquí costaría el doble sin ganar nada.
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(b'FXM1')
        f.write(np.array([len(pos), idx.size], np.uint32).tobytes())
        f.write(pos.astype(np.float32).tobytes()); f.write(uvs.astype(np.float32).tobytes())
        f.write(idx.astype(np.uint32).tobytes())
    log(f'{os.path.basename(path)}: {len(pos)} vértices, {len(idx)} triángulos, {os.path.getsize(path) / 1e6:.1f} MB')
    return len(idx)


# ───────────── Panorama ─────────────
def panorama(ctx, ancho, spp, E):
    t, sc = ctx['t'], bpy.context.scene
    alto = ancho // 2
    ojo = (-2.0 * t['ancho'] / 12, 1.6, -2.0 * t['fondo'] / 10)
    co = camara(pano=True)
    co.location = ub(ojo)
    todos = [o for o in sc.objects if o.type == 'MESH']
    # 1) Cielo base (sin disco solar ni objetos).
    base = None
    if t['cielo'] != 'noche':
        oculto = {o: o.hide_render for o in todos}
        for o in todos: o.hide_render = True
        ctx['sky'].sun_disc = False
        base = render(f'{CACHE}/base', 1024, 512, 1)[..., :3]
        ctx['sky'].sun_disc = True
        for o, h in oculto.items(): o.hide_render = h
    # 2) Tierra: la casa y lo cercano no se ven (ya existen en 3D), pero sí dan sombra y reflejo.
    for o in ctx['fijos']:
        o.visible_camera = False
    for o in todos:
        if o.name.startswith('terreno_dentro'): o.hide_render = False
    tierra = render(f'{CACHE}/tierra', ancho, alto, spp, transparente=True)
    # 3) Distancia, para la bruma.
    vl = bpy.context.view_layer
    dm = bpy.data.materials.new('distancia'); dm.use_nodes = True
    nt = dm.node_tree; nt.nodes.clear()
    cd = nt.nodes.new('ShaderNodeCameraData'); em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(cd.outputs['View Distance'], em.inputs['Strength']); nt.links.new(em.outputs[0], out.inputs[0])
    vl.material_override = dm
    sc.cycles.use_denoising = False
    dist = render(f'{CACHE}/dist', 2048, 1024, 1, transparente=True)[..., 0]
    sc.cycles.use_denoising = True
    vl.material_override = None
    dist = cv2.resize(dist, (ancho, alto), interpolation=cv2.INTER_LINEAR)
    # 4) Composición.
    sky = cielo.cielo(t, base, ancho, alto, E)
    alfa = np.clip(tierra[..., 3], 0, 1)
    rgb = cielo.bruma(tierra[..., :3], alfa, dist, sky, t, E) + sky * (1 - alfa[..., None])
    return rgb, ojo


def comprobar(ctx, objs_tex, pano_jpg, nombres, ojo):
    """Vista «como en Unity»: mallas con su textura horneada sin luz y el panorama de fondo."""
    sc = bpy.context.scene
    for o in list(sc.objects):
        if o.type == 'LIGHT' or (o.type == 'MESH' and o not in [x for x, _ in objs_tex]): o.hide_render = True
    for ob, jpg in objs_tex:
        m = bpy.data.materials.new('final'); m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = bpy.data.images.load(jpg)
        em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
        nt.links.new(tex.outputs[0], em.inputs[0]); nt.links.new(em.outputs[0], out.inputs[0])
        ob.data.materials.clear(); ob.data.materials.append(m)
        ob.hide_render = False; ob.visible_camera = True
    w = sc.world.node_tree
    for n in list(w.nodes):
        if n.type not in ('BACKGROUND', 'OUTPUT_WORLD'): w.nodes.remove(n)
    env = w.nodes.new('ShaderNodeTexEnvironment'); env.image = bpy.data.images.load(pano_jpg)
    mp = w.nodes.new('ShaderNodeMapping'); tc = w.nodes.new('ShaderNodeTexCoord')
    mp.inputs['Rotation'].default_value = (0, 0, math.radians(-90))   # El centro de la imagen mira a +Y.
    w.links.new(tc.outputs['Generated'], mp.inputs[0]); w.links.new(mp.outputs[0], env.inputs[0])
    bg = w.nodes['Background']; w.links.new(env.outputs[0], bg.inputs[0]); bg.inputs[1].default_value = 1.0
    sc.view_settings.view_transform = 'Standard'
    sc.render.image_settings.file_format = 'JPEG'; sc.render.image_settings.color_mode = 'RGB'
    sc.cycles.use_denoising = False; sc.cycles.max_bounces = 0; sc.render.film_transparent = False
    co = camara()
    pts = {n: (e, d) for n, e, d in ctx['casa'].puntos()}
    pts.update({'jardin': ((2.5, 1.6, -12.5), (-0.5, 0.05, 0.8)), 'arriba': ((-16, 9, -20), (0.55, -0.3, 0.75))})
    for n in nombres:
        mirar(co, *pts[n])
        sc.render.resolution_x, sc.render.resolution_y = 1100, 680
        sc.cycles.samples = 6
        sc.render.filepath = f'{CACHE}/final_{ctx["t"]["slug"]}_{n}.jpg'
        bpy.ops.render.render(write_still=True)
        log('comprobación', sc.render.filepath)


def todo(t, a):
    t0 = time.time()
    ctx = escena(t)
    sc = bpy.context.scene
    slug = t['slug']
    out = os.path.join(SALIDA, slug)
    # Al hornear, el terreno interior del panorama estorba (coincide con los parches cercanos).
    for o in ctx['lejos']:
        if o.name.startswith('terreno_dentro'): o.hide_render = True
    E = ctx['E'] = exposicion(ctx)
    casa_ob = unir_y_empaquetar(ctx['casa_objs'], 'casa', a.tam)
    cerca_ob = unir_y_empaquetar(ctx['cerca'], 'cerca', a.tam)
    ctx['fijos'] = [casa_ob, cerca_ob]
    luz_casa = horneado_guardado(casa_ob, a.tam, a.spp, f'{CACHE}/{slug}_casa_{a.tam}.npy')
    luz_cerca = horneado_guardado(cerca_ob, a.tam, max(16, a.spp // 2), f'{CACHE}/{slug}_cerca_{a.tam}.npy')
    guardar_jpg(f'{out}/casa_luz.jpg', cielo.a_srgb(luz_casa, E), 93)
    guardar_jpg(f'{out}/cerca_luz.jpg', cielo.a_srgb(luz_cerca, E), 93)
    tris = exportar(casa_ob, f'{out}/casa_malla.bytes') + exportar(cerca_ob, f'{out}/cerca_malla.bytes')
    pano, ojo = panorama(ctx, a.pano, a.spp_pano, E)
    guardar_jpg(f'{out}/cielo.jpg', cielo.a_srgb(pano, E), 92)
    datos = dict(tema=slug, exposicion=E, ojo=list(ojo), triangulos=int(tris), atlas=a.tam, panorama=a.pano,
                 texels_casa=casa_ob['texels'], texels_cerca=cerca_ob['texels'],
                 sol=[float(v) for v in cielo.sol(t)[[0, 2, 1]]])
    with open(f'{out}/datos.json', 'w') as f: json.dump(datos, f, indent=1)
    comprobar(ctx, [(casa_ob, f'{out}/casa_luz.jpg'), (cerca_ob, f'{out}/cerca_luz.jpg')], f'{out}/cielo.jpg',
              a.vista.split(','), ojo)
    log(f'{slug} listo en {(time.time() - t0) / 60:.1f} min')


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('tema')
    ap.add_argument('--vista', default='sofa,terraza,cama,jardin')
    ap.add_argument('--todo', action='store_true')
    ap.add_argument('--tam', type=int, default=4096)
    ap.add_argument('--spp', type=int, default=28)
    ap.add_argument('--pano', type=int, default=6144)
    ap.add_argument('--spp-pano', type=int, default=16, dest='spp_pano')
    ap.add_argument('--ancho', type=int, default=960)
    a = ap.parse_args()
    os.makedirs(CACHE, exist_ok=True)
    tema = temas.POR_SLUG[a.tema]
    if a.todo: todo(tema, a)
    else:
        ctx = escena(tema)
        for o in ctx['lejos']:
            if o.name.startswith('terreno_dentro'): o.hide_render = True
        vistas(ctx, a.vista.split(','), a.spp, a.ancho)
