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
import struct
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
SALIDA = os.environ.get('FOLIO_SALIDA', os.path.join(REPO, 'hito1', 'Assets', 'FolioXR', 'Resources', 'Entornos'))
CACHE = os.environ.get('FOLIO_CACHE', '/tmp/bl/cache')
CELDAS = {'follaje', 'hojas', 'pasto_hoja'}
CELDA_PX = 4


def log(*a):
    print('[entorno]', *a, flush=True)


def ub(p):
    """Punto o dirección de Unity a Blender."""
    return Vector((p[0], p[2], p[1]))


# ───────────── Escena ─────────────
def escena(t, lejos=True, estantes='vacios'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Los archivos temporales de Cycles (cientos de MB por horneado) van junto a la caché, no a /tmp:
    # así basta con FOLIO_CACHE para usar un disco con espacio.
    temporal = os.path.join(CACHE, 'tmp')
    os.makedirs(temporal, exist_ok=True)
    bpy.context.preferences.filepaths.temporary_directory = temporal
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    if os.environ.get('FOLIO_GPU', '1') != '0': usar_gpu(sc)
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 6
    sc.cycles.diffuse_bounces = 4
    sc.cycles.glossy_bounces = 3
    sc.cycles.caustics_reflective = sc.cycles.caustics_refractive = False
    sc.cycles.use_auto_tile = True
    sc.cycles.tile_size = 2048
    sc.view_settings.view_transform = 'Raw'
    sc.render.image_settings.file_format = 'OPEN_EXR'
    sc.render.image_settings.color_depth = '32'
    sc.render.resolution_percentage = 100
    g = fx.Geo((t['ancho'] / 12, t['fondo'] / 10))
    c = casa.Casa(g, t, estantes).construir()
    P = paisaje.Paisaje(t).construir()
    casa_objs = g.build()
    faltan = None
    if estantes == 'marcados':
        # Caras de la madera de las estanterías que los libros tapaban del todo y por eso se quitaban:
        # con el estante vacío hacen falta. Quedan en un objeto aparte, fuera del atlas de siempre.
        faltan = _copia_marcada(casa_objs)
        fx.remove_hidden([faltan], [b for b, m in zip(g.boxes, g.box_marca) if m != 1])
    quitadas = fx.remove_hidden(casa_objs, g.boxes)
    if faltan is not None: _dejar_las_que_faltan(faltan, casa_objs)
    cerca = P.g.build()
    lejos_objs = P.lejos.build()
    mats = materiales.crear(t)
    mats.update(paisaje.materiales(t))
    var_objs = [gv.build() for gv in P.far_variants] if lejos else []
    for o in casa_objs + cerca + lejos_objs + [x for grp in var_objs for x in grp] + ([faltan] if faltan else []):
        o.data.materials.append(mats[o['mat']])
    inst = paisaje.instanciar(var_objs, P.far_points) if lejos else []
    ctx = dict(t=t, casa=c, P=P, casa_objs=casa_objs, cerca=cerca, lejos=lejos_objs, inst=inst, mats=mats, faltan=faltan)
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
        if tipo == 'atardecer': sky.aerosol_density = 1.4
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


_GPU = None


def usar_gpu(sc):
    """Usa la tarjeta gráfica si hay (OptiX o CUDA en NVIDIA, HIP en AMD, oneAPI en Intel). FOLIO_GPU=0 la desactiva."""
    global _GPU
    if _GPU is None:
        _GPU = ''
        try:
            prefs = bpy.context.preferences.addons['cycles'].preferences
            for tipo in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI'):
                try:
                    prefs.compute_device_type = tipo
                except TypeError:
                    continue
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == tipo]
                if gpus:
                    for d in prefs.devices: d.use = d.type == tipo
                    _GPU = f'{tipo}: ' + ', '.join(d.name for d in gpus)
                    break
        except Exception as e:
            log('no se pudo revisar la tarjeta gráfica:', e)
        log('tarjeta gráfica:', _GPU or 'ninguna, se usa la CPU')
    if _GPU:
        sc.cycles.device = 'GPU'


def leer_exr(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    a = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)


def guardar_jpg(path, rgb8, calidad=92):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    cv2.imwrite(path, rgb8[::-1, :, ::-1], [cv2.IMWRITE_JPEG_QUALITY, calidad])


def render(path, w, h, spp, transparente=False, cache=False):
    """Renderiza a EXR. Con cache=True reutiliza el EXR de una corrida anterior con el mismo nombre."""
    if cache and os.path.exists(path + '.exr'):
        log('se reutiliza', os.path.basename(path) + '.exr')
        return leer_exr(path + '.exr')
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
    if 'sky' in ctx:
        # Tope por el cielo: si la casa queda oscura (sol bajo), subir la exposición quemaba el cielo
        # entero a blanco. El horizonte (mediana, lejos del sol) no debe pasar de ~1,35 tras exponer.
        sc = bpy.context.scene
        mallas = [o for o in sc.objects if o.type == 'MESH']
        antes = {o: o.hide_render for o in mallas}
        for o in mallas: o.hide_render = True
        ctx['sky'].sun_disc = False
        pano = camara(pano=True)
        a = render(f'{CACHE}/medir_cielo', 512, 256, 1)[..., :3]
        ctx['sky'].sun_disc = True
        for o, h in antes.items(): o.hide_render = h
        camara()
        banda = (a @ np.array([0.2126, 0.7152, 0.0722]))[int(256 * 0.51):int(256 * 0.58)]
        tope = 1.35 / max(1e-6, float(np.median(banda)))
        if tope < E: log(f'exposición limitada por el cielo: {E:.4f} → {tope:.4f}')
        E = min(E, tope)
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
    luz = a.reshape(tam, tam, 4)[..., :3].copy()
    del a
    # La imagen de 32 bits ocupa cientos de MB: se suelta antes del siguiente paso.
    for slot in ob.material_slots:
        node = slot.material.node_tree.nodes.get('horno')
        if node: node.image = None
    bpy.data.images.remove(img)
    return luz


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
        base = render(f'{CACHE}/{t["slug"]}_base', 1024, 512, 1, cache=True)[..., :3]
        ctx['sky'].sun_disc = True
        for o, h in oculto.items(): o.hide_render = h
    # 2) Tierra: la casa no se ve (ya existe en 3D), pero sí da sombra y reflejo. Lo cercano horneado
    # se apaga: sus parches de suelo tapaban el terreno interior y lo dejaban negro, y ese suelo se ve
    # en el visor más allá de la malla cercana cuando uno lee lejos del punto del panorama.
    casa_ob, cerca_ob = ctx['fijos']
    # La casa tampoco: el ojo está dentro de ella y el suelo bajo el piso salía negro en su sombra.
    casa_ob.hide_render = True
    cerca_ob.hide_render = True
    for o in todos:
        if o.name.startswith('terreno_dentro'): o.hide_render = False
    tierra = render(f'{CACHE}/{t["slug"]}_tierra3_{ancho}_{spp}', ancho, alto, spp, transparente=True, cache=True)
    cerca_ob.hide_render = False; casa_ob.hide_render = False
    # 3) Distancia, para la bruma.
    vl = bpy.context.view_layer
    dm = bpy.data.materials.new('distancia'); dm.use_nodes = True
    nt = dm.node_tree; nt.nodes.clear()
    cd = nt.nodes.new('ShaderNodeCameraData'); em = nt.nodes.new('ShaderNodeEmission'); out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(cd.outputs['View Distance'], em.inputs['Strength']); nt.links.new(em.outputs[0], out.inputs[0])
    vl.material_override = dm
    sc.cycles.use_denoising = False
    cerca_ob.hide_render = True; casa_ob.hide_render = True
    dist = render(f'{CACHE}/{t["slug"]}_dist3', 2048, 1024, 1, transparente=True, cache=True)[..., 0]
    sc.cycles.use_denoising = True
    vl.material_override = None
    cerca_ob.hide_render = False; casa_ob.hide_render = False
    # 4) Composición por franjas de filas: a 6144 × 3072 el panorama completo en float64 no cabe en 8 GB.
    dist = cv2.resize(dist, (ancho, alto), interpolation=cv2.INTER_LINEAR)
    rgb = cielo.componer(t, base, tierra, dist, ancho, alto, E)
    del tierra, dist
    return rgb, ojo


def vistas_estantes(t):
    """Ojo y dirección frente a cada estantería (biblioteca y la del rincón de la escalera)."""
    sx, sz = t['ancho'] / 12, t['fondo'] / 10
    return {'estante1': ((1.7 * sx, 1.45, 2.25 * sz), (0, -0.05, 1)),
            'estante2': ((3.35 * sx, 1.45, -3.25 * sz), (1, -0.05, 0)),
            'estante1cerca': ((1.0 * sx, 1.35, 3.55 * sz), (0.25, -0.1, 1))}


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
    pts.update(vistas_estantes(ctx['t']))
    for n in nombres:
        mirar(co, *pts[n])
        sc.render.resolution_x, sc.render.resolution_y = 1100, 680
        sc.cycles.samples = 6
        sc.render.filepath = f'{CACHE}/final_{ctx["t"]["slug"]}_{n}.jpg'
        bpy.ops.render.render(write_still=True)
        log('comprobación', sc.render.filepath)

# ───────────── Estanterías vacías sobre un tema ya horneado ─────────────
MADERA_ESTANTE = (2, 3)   # Marcas de fx.Geo: 1 = libros y adornos pintados, 2 = madera, 3 = laterales.


def _marcas(me):
    a = np.zeros(len(me.polygons), np.int32)
    capa = me.attributes.get('marca')
    if capa is not None: capa.data.foreach_get('value', a)
    return a


def _quitar_caras(ob, fuera):
    """Borra de la malla las caras que cumplen «fuera» (recibe la cara de bmesh y su marca).
    Las UV y los atributos de las demás no cambian."""
    import bmesh
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    capa = bm.faces.layers.int['marca']
    caras = [f for f in bm.faces if fuera(f, f[capa])]
    bmesh.ops.delete(bm, geom=caras, context='FACES')
    bm.to_mesh(me); bm.free(); me.update()
    return len(caras)


def _copia(ob, nombre):
    otro = ob.copy(); otro.data = ob.data.copy(); otro.name = nombre
    bpy.context.scene.collection.objects.link(otro)
    for k in ('atlas', 'peso', 'doble', 'mat', 'uv_propia'):
        if k in ob: otro[k] = ob[k]
    return otro


def _copia_marcada(objs):
    """Copia, en un objeto aparte, de toda la madera de las estanterías (antes de quitar caras tapadas)."""
    con = [o for o in objs if np.isin(_marcas(o.data), MADERA_ESTANTE).any()]
    if len(con) != 1: raise SystemExit(f'se esperaba la madera de las estanterías en un solo grupo y hay {len(con)}')
    otro = _copia(con[0], 'faltan')
    _quitar_caras(otro, lambda f, m: m not in MADERA_ESTANTE)
    return otro


def _clave(centro, normal):
    return tuple(np.round(np.asarray(centro) * 1e4).astype(np.int64)) + tuple(np.round(np.asarray(normal) * 10).astype(np.int64))


def _dejar_las_que_faltan(faltan, objs):
    """Deja en «faltan» solo las caras que no están en la casa (las que los libros tapaban)."""
    estan = set()
    for o in objs:
        m = _marcas(o.data)
        for poly in o.data.polygons:
            if m[poly.index] in MADERA_ESTANTE: estan.add(_clave(poly.center, poly.normal))
    _quitar_caras(faltan, lambda f, m: _clave(f.calc_center_median(), f.normal) in estan)


def _texels(me, tam):
    """Texels del atlas que ocupan las caras de la malla (fila 0 abajo, como las imágenes de Blender)."""
    uv = _uv(me)
    n = len(me.polygons)
    ls = np.empty(n, np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(n, np.int32); me.polygons.foreach_get('loop_total', lt)
    m = np.zeros((tam, tam), np.uint8)
    pts = np.round((uv * tam - 0.5) * 16).astype(np.int32)      # El centro del texel i está en i + 0,5.
    for a_, c_ in zip(ls, lt):
        cv2.fillConvexPoly(m, pts[a_:a_ + c_], 1, shift=4)
    return m


def _densidad(me, tam):
    """Texels por metro de las caras de una malla ya ubicada en el atlas."""
    uv = _uv(me)
    n = len(me.polygons)
    a3 = np.empty(n, np.float32); me.polygons.foreach_get('area', a3)
    ls = np.empty(n, np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(n, np.int32); me.polygons.foreach_get('loop_total', lt)
    nxt = np.arange(len(uv)) + 1
    nxt[ls + lt - 1] = ls
    cruz = uv[:, 0] * uv[nxt, 1] - uv[nxt, 0] * uv[:, 1]
    auv = np.abs(np.add.reduceat(cruz, ls)) * 0.5
    return math.sqrt(float(auv.sum()) / max(1e-9, float(a3.sum()))) * tam


def _mover_laterales(ob, laterales):
    """Lleva cada lateral de estantería de su caja de antes a la que sobresale 3 mm (las UV no cambian)."""
    me = ob.data
    co = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    m = _marcas(me)
    usados = sorted({v for poly in me.polygons if m[poly.index] == 3 for v in poly.vertices})
    if not usados: return 0
    idx = np.array(usados)
    movidos = 0
    for antes, despues in laterales:
        loc = (np.c_[co[idx], np.ones(len(idx))] @ np.linalg.inv(antes).T)[:, :3]
        dentro = np.all(np.abs(loc) <= 0.5 + 1e-4, axis=1)
        co[idx[dentro]] = (np.c_[loc[dentro], np.ones(int(dentro.sum()))] @ despues.T)[:, :3]
        movidos += int(dentro.sum())
    me.vertices.foreach_set('co', co.ravel()); me.update()
    return movidos


def _ubicar(ob, ocupado, tam, dens, margen=5):
    """Da a cada cara (rectángulo) de «ob» un lugar libre del atlas, a «dens» texels por metro.
    ocupado: texels ya usados (se actualiza). Devuelve cuántas caras ubicó."""
    me = ob.data
    n = len(me.polygons)
    if n == 0: return 0
    uv = _uv(me).astype(np.float64)
    co = np.empty(len(me.vertices) * 3, np.float64); me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
    occ = (cv2.dilate(ocupado, np.ones((7, 7), np.uint8)) > 0).astype(np.int32)
    orden = sorted(range(n), key=lambda i: -me.polygons[i].area)
    for i in orden:
        poly = me.polygons[i]
        if poly.loop_total != 4: raise SystemExit('una cara de estantería que falta no es un rectángulo')
        ls = poly.loop_start
        p = co[lv[ls:ls + 4]]
        w, h = np.linalg.norm(p[1] - p[0]) * dens, np.linalg.norm(p[3] - p[0]) * dens
        puesto = False
        for girar in (False, True):
            a_, b_ = (w, h) if not girar else (h, w)
            A, B = int(math.ceil(a_)) + 2 * margen, int(math.ceil(b_)) + 2 * margen
            if A > tam or B > tam: continue
            I = np.zeros((tam + 1, tam + 1), np.int64)
            I[1:, 1:] = occ.cumsum(0).cumsum(1)
            S = I[B:, A:] - I[:-B, A:] - I[B:, :-A] + I[:-B, :-A]
            ys, xs = np.nonzero(S == 0)
            if not len(ys): continue
            y0, x0 = int(ys[0]), int(xs[0])
            occ[y0:y0 + B, x0:x0 + A] = 1
            ocupado[y0 + margen:y0 + B - margen, x0 + margen:x0 + A - margen] = 1
            x0 += margen; y0 += margen
            esquinas = ([(x0, y0), (x0 + w, y0), (x0 + w, y0 + h), (x0, y0 + h)] if not girar else
                        [(x0, y0), (x0, y0 + w), (x0 + h, y0 + w), (x0 + h, y0)])
            uv[ls:ls + 4] = np.array(esquinas) / tam
            puesto = True
            break
        if not puesto: raise SystemExit('no queda lugar en el atlas para una cara de estantería; hay que hornear el tema entero (--todo)')
    me.uv_layers.active.data.foreach_set('uv', uv.astype(np.float32).ravel())
    return n


def luz_de_estantes(ob, luz, casa_, t, E, tam):
    """Luz que llega a cada fila de las estanterías, para teñir en Unity los libros que deja la persona
    (se dibujan sin luz). Sale del canto delantero de las tablas, que mira a la sala como los lomos.
    Devuelve 36 números: por estantería (biblioteca, rincón) y fila (de abajo hacia arriba), el factor
    rojo, verde y azul por el que se multiplica el color del libro en pantalla."""
    me = ob.data
    m = _marcas(me)
    uv = _uv(me)
    centros = np.array([p.center[:] for p in me.polygons])
    normales = np.array([p.normal[:] for p in me.polygons])
    madera = np.array(materiales.lin(t['madera']))[:3] * 0.9
    por_tabla = {}
    for numero, fila, centro in casa_.frentes:
        d = np.linalg.norm(centros - centro, axis=1)
        d[~np.isin(m, MADERA_ESTANTE)] = 1e9
        i = int(np.argmin(d))
        if d[i] > 2e-3: continue
        poly = me.polygons[i]
        pts = np.round((uv[poly.loop_start:poly.loop_start + poly.loop_total] * tam - 0.5) * 16).astype(np.int32)
        mask = np.zeros((tam, tam), np.uint8)
        cv2.fillConvexPoly(mask, pts, 1, shift=4)
        mask = cv2.erode(mask, np.ones((3, 3), np.uint8)) > 0
        if mask.sum() < 20: continue
        por_tabla[(numero, fila)] = luz[mask].mean(0) / madera
    k = 0.35                                    # Un libro de color medio.
    ref = 1.055 * k ** (1 / 2.4) - 0.055
    fuera = []
    for numero in range(2):
        for fila in range(6):
            cerca = [por_tabla[c] for c in ((numero, fila), (numero, fila + 1)) if c in por_tabla]
            if not cerca: fuera += [1.0, 1.0, 1.0]; continue
            y = cielo.aces(np.mean(cerca, 0) * k * E)
            y = np.where(y <= 0.0031308, y * 12.92, 1.055 * np.power(np.maximum(y, 1e-9), 1 / 2.4) - 0.055)
            fuera += [round(float(v), 3) for v in np.clip(y / ref, 0.04, 1.25)]
    return fuera


def vaciar_estantes(t, a):
    """Quita de un tema ya horneado lo que llenaba las estanterías (libros y adornos pintados) sin rehacer
    el atlas: repite la casa con el mismo reparto de UV, borra esas caras, hornea de nuevo solo la madera de
    las estanterías (antes quedaba en sombra detrás de los libros) y la pega en casa_luz.jpg."""
    t0 = time.time()
    slug = t['slug']
    out = os.path.join(SALIDA, slug)
    datos = json.load(open(f'{out}/datos.json'))
    E, tam = float(datos['exposicion']), int(datos['atlas'])
    # Originales: se guardan la primera vez para poder repetir el paso.
    origen = os.path.join(CACHE, 'original', slug)
    os.makedirs(origen, exist_ok=True)
    for nombre in ('casa_malla.bytes', 'casa_luz.jpg'):
        if not os.path.exists(f'{origen}/{nombre}'):
            with open(f'{out}/{nombre}', 'rb') as f, open(f'{origen}/{nombre}', 'wb') as g_: g_.write(f.read())
    ctx = escena(t, lejos=False, estantes='marcados')
    for o in ctx['lejos']:
        if o.name.startswith('terreno_dentro'): o.hide_render = True
    ctx['E'] = E
    casa_ob = unir_y_empaquetar(ctx['casa_objs'], 'casa', tam)
    cerca_ob = unir_y_empaquetar(ctx['cerca'], 'cerca', tam)
    prueba = f'{CACHE}/{slug}_prueba.bytes'
    exportar(casa_ob, prueba)
    if open(prueba, 'rb').read() != open(f'{origen}/casa_malla.bytes', 'rb').read():
        raise SystemExit(f'{slug}: la casa generada no coincide con la malla horneada; hay que hornear el tema entero (--todo).')
    os.remove(prueba)
    marcas = _marcas(casa_ob.data)
    faltan = ctx['faltan']
    log(f'{slug}: {int((marcas == 1).sum())} caras de libros y adornos, {int(np.isin(marcas, MADERA_ESTANTE).sum())} de madera '
        f'de estantería y {len(faltan.data.polygons)} que los libros tapaban')
    estantes = _copia(casa_ob, 'estantes')
    _quitar_caras(estantes, lambda f, m: m not in MADERA_ESTANTE)
    _quitar_caras(casa_ob, lambda f, m: m != 0)
    ajeno = _texels(casa_ob.data, tam)
    if len(faltan.data.polygons):
        desenvolver(faltan)
        ocupado = np.maximum(ajeno, _texels(estantes.data, tam))
        _ubicar(faltan, ocupado, tam, _densidad(estantes.data, tam))
        _activar(estantes); faltan.select_set(True)
        bpy.ops.object.join()
        estantes = bpy.context.view_layer.objects.active
    else:
        bpy.data.objects.remove(faltan)
    movidos = _mover_laterales(estantes, ctx['casa'].laterales)
    log(f'{slug}: {movidos} vértices de laterales movidos')
    luz = horneado_guardado(estantes, tam, a.spp, f'{CACHE}/{slug}_estantes_{tam}.npy')
    datos['luz_estantes'] = luz_de_estantes(estantes, luz, ctx['casa'], t, E, tam)
    log(f'{slug}: luz de las filas (biblioteca, de abajo hacia arriba): ' + ' '.join(f'{v:.2f}' for v in datos['luz_estantes'][1:18:3])
        + ' · rincón: ' + ' '.join(f'{v:.2f}' for v in datos['luz_estantes'][19:36:3]))
    propio = _texels(estantes.data, tam) > 0
    cerca_de = cv2.dilate(propio.astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
    libre = cv2.dilate(ajeno, np.ones((3, 3), np.uint8)) == 0
    donde = propio | (cerca_de & libre)
    atlas = cv2.imread(f'{origen}/casa_luz.jpg')[::-1, :, ::-1].copy()
    if atlas.shape[0] != tam: raise SystemExit(f'{slug}: casa_luz.jpg mide {atlas.shape[0]} px y se esperaban {tam}.')
    atlas[donde] = cielo.a_srgb(luz, E)[donde]
    guardar_jpg(f'{out}/casa_luz.jpg', atlas, 93)
    log(f'{slug}: {int(donde.sum())} texels nuevos ({donde.mean() * 100:.1f} % del atlas)')
    # La malla sale sin los libros pintados; la madera vuelve a ser parte de la casa.
    _activar(casa_ob); estantes.select_set(True)
    bpy.ops.object.join()
    casa_ob = bpy.context.view_layer.objects.active
    tris = exportar(casa_ob, f'{out}/casa_malla.bytes')
    with open(f'{out}/cerca_malla.bytes', 'rb') as f: f.seek(8); tris += int(np.frombuffer(f.read(4), np.uint32)[0]) // 3
    datos['triangulos'] = int(tris)
    with open(f'{out}/datos.json', 'w') as f: json.dump(datos, f, indent=1)
    ojo = tuple(datos['ojo'])
    comprobar(ctx, [(casa_ob, f'{out}/casa_luz.jpg'), (cerca_ob, f'{out}/cerca_luz.jpg')], f'{out}/cielo.jpg',
              ['estante1', 'estante2', 'estante1cerca'], ojo)
    log(f'{slug}: estanterías vacías en {(time.time() - t0) / 60:.1f} min')
    memoria()


# ───────────── Agua cercana y hojas a oscuras (retoques sin hornear) ─────────────
def _leer_malla(path):
    b = open(path, 'rb').read()
    nv, ni = (int(v) for v in np.frombuffer(b, np.uint32, 2, 4))
    pos = np.frombuffer(b, np.float32, nv * 3, 12).reshape(-1, 3)
    uv = np.frombuffer(b, np.float32, nv * 2, 12 + nv * 12).reshape(-1, 2)
    idx = np.frombuffer(b, np.uint32, ni, 12 + nv * 20).reshape(-1, 3)
    return pos, uv, idx


def malla_de_agua(P, path, radio=147.0, cerca=36.0):
    """Superficie del agua (lago, mar) en coordenadas de Unity. El panorama solo es exacto mirado desde su
    centro: desde el balcón o caminando, donde debía verse agua se veía el fondo. Es un plano a la altura
    del agua; cerca de la casa cubre todo lo que pueda quedar bajo el agua (el terreno, que sí tiene
    relieve, lo tapa donde asoma: la orilla sale sola) y más lejos, donde la tierra solo está pintada en el
    panorama, llega hasta la orilla. Unity lo pinta con reflejo del cielo (FolioXRWater.shader).
    Devuelve los triángulos escritos (0 si el tema no tiene agua)."""
    if not P.con_agua: return 0
    y = paisaje.GY + paisaje.AGUA
    # Cerca (hasta «cerca» m): celdas de una grilla polar, con margen; el terreno tapa lo que sobre.
    radios = [3.0]
    while radios[-1] < cerca: radios.append(min(cerca, radios[-1] + 0.8))
    radios = np.array(radios)
    ang = np.linspace(0, 2 * math.pi, 361)
    R, A = np.meshgrid(radios, ang, indexing='ij')
    x, z = np.cos(A) * R, np.sin(A) * R
    alto = P.altura(x, z)
    rm, am = (radios[:-1] + radios[1:]) * 0.5, (ang[:-1] + ang[1:]) * 0.5
    Rm, Am = np.meshgrid(rm, am, indexing='ij')
    centro = P.altura(np.cos(Am) * Rm, np.sin(Am) * Rm)
    minimo = np.minimum(np.minimum(alto[:-1, :-1], alto[:-1, 1:]), np.minimum(alto[1:, :-1], alto[1:, 1:]))
    pone = np.minimum(minimo, centro) < paisaje.AGUA + 0.3
    pos = [np.stack([x, np.full_like(x, y), z], -1).reshape(-1, 3)]
    m = len(ang)
    ii, jj = np.nonzero(pone)
    a = ii * m + jj
    tri = [np.stack([a, a + 1, a + m + 1], 1), np.stack([a, a + m + 1, a + m], 1)]
    # Lejos: donde la tierra solo está pintada en el panorama, el agua llega justo hasta la orilla. Antes eran
    # celdas sueltas y la orilla salía en escalones (17 nov 2026): ahora, en cada dirección, se
    # busca la orilla con precisión de 10 cm (720 direcciones) y la franja de agua termina ahí.
    na = 720
    ang2 = np.linspace(0, 2 * math.pi, na + 1)[:-1]
    paso = 0.1
    rr = np.arange(cerca, radio + paso, paso)
    Rr, Aa = np.meshgrid(rr, ang2, indexing='ij')
    tierra = P.altura(np.cos(Aa) * Rr, np.sin(Aa) * Rr) >= paisaje.AGUA - 0.02
    # En cada dirección, el primer tramo de agua después de «cerca»: desde donde empieza hasta la orilla
    # siguiente (o el final). Así también se cubre el mar que corre paralelo a la costa.
    agua = ~tierra
    hay = agua.any(0)
    ini = np.where(hay, agua.argmax(0), 0)
    despues = tierra & (np.arange(len(rr))[:, None] > ini[None, :])
    fin_i = np.where(despues.any(0), despues.argmax(0), len(rr) - 1)
    desde = np.where(hay, rr[np.maximum(ini - 1, 0)], cerca)
    fin = np.where(hay, rr[fin_i], cerca)
    k = 14                                                             # Puntos por franja.
    t = np.linspace(0, 1, k)
    r2 = desde[None, :] + (fin - desde)[None, :] * (t[:, None] ** 1.5)  # Más puntos del lado de la casa.
    x2, z2 = np.cos(ang2)[None, :] * r2, np.sin(ang2)[None, :] * r2
    base = len(pos[0])
    pos.append(np.stack([x2, np.full_like(x2, y), z2], -1).reshape(-1, 3))
    fila, col = np.meshgrid(np.arange(k - 1), np.arange(na), indexing='ij')
    sig = (col + 1) % na
    usar = (hay[col] | hay[sig]).ravel()
    p00 = (base + fila * na + col).ravel()[usar]; p01 = (base + fila * na + sig).ravel()[usar]
    p10 = (base + (fila + 1) * na + col).ravel()[usar]; p11 = (base + (fila + 1) * na + sig).ravel()[usar]
    # Mismo sentido de giro que las celdas cercanas (de ángulo a ángulo + 1, de radio a radio + 1).
    tri += [np.stack([p00, p01, p11], 1), np.stack([p00, p11, p10], 1)]
    pos = np.concatenate(pos)
    tri = np.concatenate(tri).astype(np.uint32)
    if not len(tri): return 0
    usados, inv = np.unique(tri, return_inverse=True)
    pos = pos[usados]
    with open(path, 'wb') as f:
        f.write(b'FXM1')
        f.write(np.array([len(pos), inv.size], np.uint32).tobytes())
        f.write(pos.astype(np.float32).tobytes()); f.write(np.zeros((len(pos), 2), np.float32).tobytes())
        f.write(inv.astype(np.uint32).tobytes())
    return len(tri)


def P_tiene_agua(ctx):
    return bool(ctx['P'].con_agua)


def contrahuellas(malla):
    """Repone las contrahuellas que le faltan a la escalera. El horneado original quitaba las de la mitad
    de arriba (remove_hidden las daba por tapadas por el peldaño anterior) y se veía a través de la
    escalera. Cada una toma el color de la última contrahuella que sí está. Devuelve cuántas agregó."""
    with open(malla, 'rb') as f: d = f.read()
    nv, ni = struct.unpack('<II', d[4:12])
    pos = np.frombuffer(d, '<f4', nv * 3, 12).reshape(-1, 3).copy()
    uv = np.frombuffer(d, '<f4', nv * 2, 12 + nv * 12).reshape(-1, 2).copy()
    idx = np.frombuffer(d, '<u4', ni, 12 + nv * 20).reshape(-1, 3).copy()
    T = pos[idx]
    n = np.cross(T[:, 1] - T[:, 0], T[:, 2] - T[:, 0])
    n /= np.linalg.norm(n, axis=1)[:, None] + 1e-12
    ex, ey, ez = (np.ptp(T[..., k], axis=1) for k in range(3))
    c = T.mean(1)
    # Huellas: caras hacia arriba, anchas y de un paso de fondo.
    h = (n[:, 1] > 0.99) & (ex > 1.2) & (ex < 2.6) & (ez > 0.2) & (ez < 0.45) & (c[:, 1] > 0.1)
    pasos = {}
    for k in np.nonzero(h)[0]:
        y = round(float(c[k, 1]), 2)
        p = pasos.setdefault(y, [1e9, -1e9, 1e9, -1e9])
        p[0] = min(p[0], T[k, :, 0].min()); p[1] = max(p[1], T[k, :, 0].max())
        p[2] = min(p[2], T[k, :, 2].min()); p[3] = max(p[3], T[k, :, 2].max())
    ys = sorted(pasos)
    # La escalera es la cadena más larga de huellas que suben de a un paso y avanzan hacia +Z.
    mejor, actual = [], []
    for y in ys:
        if actual and 0.1 < y - actual[-1] < 0.24 and abs(pasos[y][0] - pasos[actual[-1]][0]) < 0.02 \
                and 0.2 < pasos[y][2] - pasos[actual[-1]][2] < 0.45:
            actual.append(y)
        else:
            if len(actual) > len(mejor): mejor = actual
            actual = [y]
    if len(actual) > len(mejor): mejor = actual
    if len(mejor) < 8: return 0
    frente = np.nonzero(n[:, 2] < -0.99)[0]

    def cubre(p):
        """Triángulo que mira a −Z y contiene el punto p (o −1)."""
        t = T[frente]
        cerca_ = np.abs(t[:, 0, 2] - p[2]) < 0.004
        v0, v1, v2 = t[:, 1] - t[:, 0], t[:, 2] - t[:, 0], p - t[:, 0]
        d00, d01, d11 = (v0 * v0).sum(1), (v0 * v1).sum(1), (v1 * v1).sum(1)
        d20, d21 = (v2 * v0).sum(1), (v2 * v1).sum(1)
        den = d00 * d11 - d01 * d01 + 1e-20
        b, g = (d11 * d20 - d01 * d21) / den, (d00 * d21 - d01 * d20) / den
        ok = np.nonzero(cerca_ & (b >= -1e-4) & (g >= -1e-4) & (b + g <= 1 + 1e-4))[0]
        return int(frente[ok[0]]) if len(ok) else -1

    def uv_en(k, p):
        """UV del punto p según el triángulo k (la cara es plana y su UV, afín)."""
        t = pos[idx[k]]; u = uv[idx[k]]
        v0, v1, v2 = t[1] - t[0], t[2] - t[0], p - t[0]
        d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        b, g = (d11 * d20 - d01 * d21) / den, (d00 * d21 - d01 * d20) / den
        return u[0] * (1 - b - g) + u[1] * b + u[2] * g

    nuevos_p, nuevos_uv, nuevos_i = [], [], []
    ref = None                                                       # (triángulo, y de arriba, z) de la última que está.
    for a, b in zip(mejor[:-1], mejor[1:]):
        x0, x1, z = pasos[b][0], pasos[b][1], pasos[b][2]
        k = cubre(np.array([(x0 + x1) * 0.5, b - 0.05, z], np.float32))
        if k >= 0:
            ref = (k, b, z)
            continue
        if ref is None: continue
        base = len(pos) + len(nuevos_p)
        esquinas = [(x0, a), (x1, a), (x1, b), (x0, b)]
        for x, y in esquinas:
            nuevos_p.append((x, y, z))
            # La misma franja (los centímetros de arriba) de la contrahuella de referencia.
            q = np.array([min(max(x, x0 + 0.01), x1 - 0.01), ref[1] - min(max(b - y, 0.006), b - a - 0.006), ref[2]], np.float32)
            nuevos_uv.append(uv_en(ref[0], q))
        tri = [(base, base + 2, base + 1), (base, base + 3, base + 2)]
        # Que miren a −Z, con el mismo sentido de giro que el resto de la malla.
        e = [np.array([x, y, z]) for x, y in esquinas]
        if np.cross(e[2] - e[0], e[1] - e[0])[2] > 0: tri = [(q[0], q[2], q[1]) for q in tri]
        nuevos_i += tri
    if not nuevos_p: return 0
    pos = np.concatenate([pos, np.array(nuevos_p, np.float32)])
    uv = np.concatenate([uv, np.array(nuevos_uv, np.float32)])
    idx = np.concatenate([idx, np.array(nuevos_i, np.uint32)])
    with open(malla, 'wb') as f:
        f.write(b'FXM1')
        f.write(np.array([len(pos), idx.size], np.uint32).tobytes())
        f.write(pos.astype(np.float32).tobytes()); f.write(uv.astype(np.float32).tobytes())
        f.write(idx.astype(np.uint32).tobytes())
    return len(nuevos_p) // 4


def aclarar_hojas(malla, atlas_jpg, tam):
    """Las hojas se hornean por una sola cara y Unity las muestra por las dos: la mitad quedaba negra
    (su cara horneada miraba hacia adentro de la planta). Cada hoja muy oscura toma el color de las hojas
    iluminadas de su misma planta, atenuado. Devuelve cuántas celdas cambió."""
    pos, uv, idx = _leer_malla(malla)
    t_uv = uv[idx] * tam                                            # (n, 3, 2) en texels
    chico = (np.ptp(t_uv[..., 0], axis=1) < CELDA_PX) & (np.ptp(t_uv[..., 1], axis=1) < CELDA_PX)
    cx = np.floor(t_uv[..., 0].mean(1) / CELDA_PX).astype(np.int64)
    cy = np.floor(t_uv[..., 1].mean(1) / CELDA_PX).astype(np.int64)
    # Las celdas de hoja llenan filas completas desde abajo; se descartan triángulos chicos de otras islas.
    filas = 0
    cols = tam // CELDA_PX
    cuenta = np.bincount(cy[chico], minlength=cols)
    while filas < cols and cuenta[filas] > cols * 0.5: filas += 1
    if filas < cols and cuenta[filas] > 0 and filas > 0: filas += 1    # Última fila, a medio llenar.
    hoja = chico & (cy < filas)
    if not hoja.any(): return 0
    img = cv2.imread(atlas_jpg)
    if img is None or img.shape[0] != tam: return 0
    arr = img[::-1].astype(np.float32)                                   # Fila 0 abajo, como las UV.
    cxh, cyh = cx[hoja], cy[hoja]
    col = (arr[cyh * CELDA_PX + 1, cxh * CELDA_PX + 1] + arr[cyh * CELDA_PX + 2, cxh * CELDA_PX + 2] +
           arr[cyh * CELDA_PX + 1, cxh * CELDA_PX + 2] + arr[cyh * CELDA_PX + 2, cxh * CELDA_PX + 1]) / 4
    lum = col.max(1)
    centro = pos[idx[hoja]].mean(1)
    planta = np.floor(centro / 1.6).astype(np.int64)
    clave = planta[:, 0] * 73856093 ^ planta[:, 1] * 19349663 ^ planta[:, 2] * 83492791
    orden = np.argsort(clave, kind='stable')
    cortes = np.flatnonzero(np.diff(clave[orden])) + 1
    # Color de hoja iluminada de todo el atlas, por si una planta entera quedó a oscuras.
    general = col[lum >= np.percentile(lum, 90)].mean(0)
    cambiadas = 0
    for grupo in np.split(orden, cortes):
        l = lum[grupo]
        meta = col[grupo[l >= np.percentile(l, 88)]].mean(0)
        if meta.max() < 0.45 * general.max(): meta = meta * 0.4 + general * 0.33
        ml = float(meta.max())
        if ml < 10: continue
        oscuras = grupo[l < 0.38 * ml]
        if not len(oscuras): continue
        # Hoja en sombra: entre un 30 % y un 46 % de la iluminada, conservando algo de su variación.
        f = 0.30 + 0.16 * np.clip(lum[oscuras] / (0.38 * ml), 0, 1)
        nuevo = meta[None, :] * f[:, None]
        for (x_, y_), c in zip(zip(cxh[oscuras], cyh[oscuras]), nuevo):
            arr[y_ * CELDA_PX:(y_ + 1) * CELDA_PX, x_ * CELDA_PX:(x_ + 1) * CELDA_PX] = c
        cambiadas += len(oscuras)
    if cambiadas:
        cv2.imwrite(atlas_jpg, np.clip(arr[::-1], 0, 255).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 93])
    return cambiadas


def retocar(t, a):
    """Retoques sobre un tema ya horneado, sin hornear nada: hojas a oscuras y malla del agua cercana."""
    slug = t['slug']
    out = os.path.join(SALIDA, slug)
    datos = json.load(open(f'{out}/datos.json'))
    tam = int(datos['atlas'])
    if a.solo == 'escalera':
        n = contrahuellas(f'{out}/casa_malla.bytes')
        log(f'{slug}: {n} contrahuellas repuestas en la escalera')
        return
    if a.solo == 'hojas':
        for parte in ('casa', 'cerca'):
            if datos.get('hojas_' + parte): continue                     # Ya aclaradas: repetirlo las iría lavando.
            n = aclarar_hojas(f'{out}/{parte}_malla.bytes', f'{out}/{parte}_luz.jpg', tam)
            datos['hojas_' + parte] = int(n) + 1
            log(f'{slug}: {n} hojas a oscuras aclaradas en {parte}_luz.jpg')
    else:
        ctx = escena(t, lejos=False)
        n = malla_de_agua(ctx['P'], f'{out}/agua_malla.bytes')
        if n and t.get('agua') is not None: datos['agua'] = [round(float(v), 4) for v in t['agua'][:3]]
        log(f'{slug}: agua cercana, {n} triángulos')
    with open(f'{out}/datos.json', 'w') as f: json.dump(datos, f, indent=1)


def memoria():
    import resource
    log(f'memoria máxima: {resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1e6:.1f} GB')


def todo(t, a):
    t0 = time.time()
    # Para hornear la casa o el terreno cercano no hacen falta los miles de árboles lejanos (solo
    # aportan sombra a cientos de metros) y con ellos el proceso puede pasar de 8 GB.
    ctx = escena(t, lejos=a.solo in (None, 'pano'))
    sc = bpy.context.scene
    slug = t['slug']
    out = os.path.join(SALIDA, slug)
    # Al hornear, el terreno interior del panorama estorba (coincide con los parches cercanos).
    for o in ctx['lejos']:
        if o.name.startswith('terreno_dentro'): o.hide_render = True
    # Cada horneado puede ir en un proceso aparte (--solo): con 8 GB no caben los dos seguidos.
    fe = f'{CACHE}/{t["slug"]}_E.txt'
    datos_previos = os.path.join(SALIDA, slug, 'datos.json')
    if a.solo == 'pano' and os.path.exists(datos_previos):
        E = float(json.load(open(datos_previos))['exposicion'])   # La misma con que se hornearon los atlas.
    elif os.path.exists(fe): E = float(open(fe).read())
    else:
        E = exposicion(ctx)
        with open(fe, 'w') as f: f.write(repr(E))
    ctx['E'] = E
    casa_ob = unir_y_empaquetar(ctx['casa_objs'], 'casa', a.tam)
    cerca_ob = unir_y_empaquetar(ctx['cerca'], 'cerca', a.tam)
    ctx['fijos'] = [casa_ob, cerca_ob]
    if a.solo == 'cerca':
        horneado_guardado(cerca_ob, a.tam, max(16, a.spp // 2), f'{CACHE}/{slug}_cerca_{a.tam}.npy')
        return memoria()
    if a.solo == 'pano':
        # Solo rehace el cielo (cielo.jpg); casa y cercano quedan como están en Resources.
        pano, _ = panorama(ctx, a.pano, a.spp_pano, E)
        guardar_jpg(f'{out}/cielo.jpg', cielo.a_srgb(pano, E), 92)
        log(f'{slug}: panorama listo en {(time.time() - t0) / 60:.1f} min')
        return memoria()
    luz_casa = horneado_guardado(casa_ob, a.tam, a.spp, f'{CACHE}/{slug}_casa_{a.tam}.npy')
    if a.solo == 'casa': return memoria()
    luz_cerca = horneado_guardado(cerca_ob, a.tam, max(16, a.spp // 2), f'{CACHE}/{slug}_cerca_{a.tam}.npy')
    guardar_jpg(f'{out}/casa_luz.jpg', cielo.a_srgb(luz_casa, E), 93)
    guardar_jpg(f'{out}/cerca_luz.jpg', cielo.a_srgb(luz_cerca, E), 93)
    tris = exportar(casa_ob, f'{out}/casa_malla.bytes') + exportar(cerca_ob, f'{out}/cerca_malla.bytes')
    pano, ojo = panorama(ctx, a.pano, a.spp_pano, E)
    guardar_jpg(f'{out}/cielo.jpg', cielo.a_srgb(pano, E), 92)
    datos = dict(tema=slug, exposicion=E, ojo=list(ojo), triangulos=int(tris), atlas=a.tam, panorama=a.pano,
                 texels_casa=casa_ob['texels'], texels_cerca=cerca_ob['texels'],
                 sol=[float(v) for v in cielo.sol(t)[[0, 2, 1]]],
                 luz_estantes=luz_de_estantes(casa_ob, luz_casa, ctx['casa'], t, E, a.tam))
    # Retoques que no dependen del horneado: hojas que quedaron a oscuras y agua desde la orilla.
    for parte in ('casa', 'cerca'):
        datos['hojas_' + parte] = aclarar_hojas(f'{out}/{parte}_malla.bytes', f'{out}/{parte}_luz.jpg', a.tam) + 1
    contrahuellas(f'{out}/casa_malla.bytes')
    if t.get('agua') is not None and P_tiene_agua(ctx): datos['agua'] = [round(float(v), 4) for v in t['agua'][:3]]
    if not malla_de_agua(ctx['P'], f'{out}/agua_malla.bytes') and os.path.exists(f'{out}/agua_malla.bytes'):
        os.remove(f'{out}/agua_malla.bytes')
    with open(f'{out}/datos.json', 'w') as f: json.dump(datos, f, indent=1)
    import suelo
    suelo.tema(slug)                                                   # Suelo caminable y límite (escribe datos.json).
    comprobar(ctx, [(casa_ob, f'{out}/casa_luz.jpg'), (cerca_ob, f'{out}/cerca_luz.jpg')], f'{out}/cielo.jpg',
              a.vista.split(','), ojo)
    log(f'{slug} listo en {(time.time() - t0) / 60:.1f} min')
    memoria()


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('tema')
    ap.add_argument('--vista', default='sofa,terraza,cama,jardin')
    ap.add_argument('--todo', action='store_true')
    ap.add_argument('--solo', choices=['casa', 'cerca', 'pano', 'estantes', 'hojas', 'agua', 'escalera'],
                    help='hornea solo ese atlas y termina (se guarda en caché); pano rehace el cielo; estantes vacía '
                         'las estanterías de un tema ya horneado')
    ap.add_argument('--tam', type=int, default=4096)
    ap.add_argument('--spp', type=int, default=28)
    ap.add_argument('--pano', type=int, default=6144)
    ap.add_argument('--spp-pano', type=int, default=16, dest='spp_pano')
    ap.add_argument('--ancho', type=int, default=960)
    ap.add_argument('--medir', action='store_true', help='vistas con la exposición fija del tema (como en el horneado)')
    a = ap.parse_args()
    os.makedirs(CACHE, exist_ok=True)
    tema = temas.POR_SLUG[a.tema]
    if a.solo in ('hojas', 'agua', 'escalera'): retocar(tema, a)
    elif a.solo == 'estantes': vaciar_estantes(tema, a)
    elif a.todo or a.solo: todo(tema, a)
    else:
        ctx = escena(tema)
        for o in ctx['lejos']:
            if o.name.startswith('terreno_dentro'): o.hide_render = True
        if a.medir: ctx['E'] = exposicion(ctx)
        vistas(ctx, a.vista.split(','), a.spp, a.ancho)
