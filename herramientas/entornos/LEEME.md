# Generador de entornos horneados

Crea, con Blender, los archivos que Folio XR muestra como entorno: la casa, el terreno cercano y un cielo panorámico, con la luz ya calculada. Todo es procedural y original: no usa modelos, texturas ni fotos de terceros.

## Requisitos

```bash
pip install bpy numpy opencv-python
```

`bpy` es Blender como módulo de Python (probado con 5.3). No necesita tarjeta gráfica: hornea con la CPU.

## Uso

```bash
cd herramientas/entornos
python3 generar.py moderna-calida --vista sofa,terraza     # vistas rápidas de la escena (segundos)
python3 generar.py moderna-calida --todo                   # hornea, hace el panorama, exporta y comprueba
```

Los nombres de tema están en `temas.py` y son los mismos de `EnvironmentTheme.cs` (campo `Baked`). Si se cambia un color o una medida en un archivo, hay que cambiarlo en el otro.

Opciones de `--todo`: `--tam` (lado del atlas, 4096), `--spp` (muestras de la casa, 28), `--pano` (ancho del panorama, 6144) y `--spp-pano` (16). Con dos núcleos, un tema tarda unos 35 a 45 minutos. Si la corrida se corta, al repetirla reutiliza los horneados ya guardados en `/tmp/bl/cache`.

La caché y los temporales ocupan unos 2 GB. Si el disco de `/tmp` tiene poco espacio, indica otra carpeta con `FOLIO_CACHE=/ruta/con/espacio`. La tarjeta gráfica se usa sola si hay; `FOLIO_GPU=0` fuerza el procesador.

## Qué produce

En `hito1/Assets/FolioXR/Resources/Entornos/<tema>/`:

| Archivo | Contenido |
| --- | --- |
| `casa_malla.bytes` y `casa_luz.jpg` | Casa y muebles, con su atlas de color y luz |
| `cerca_malla.bytes` y `cerca_luz.jpg` | Terreno hasta 34 m, árboles, arbustos, rocas y pasto |
| `cielo.jpg` | Panorama de 360° con el cielo y todo el paisaje lejano |
| `datos.json` | Exposición, centro del panorama y densidad de texels |

Además deja en `/tmp/bl/cache/final_<tema>_<vista>.jpg` imágenes «como en Unity» (mallas con su textura, sin luz, y el panorama de fondo) para revisar el resultado sin el visor.

## Cómo funciona

1. **Casa (`casa.py`).** Misma planta que `HouseGenerator.cs`, que sigue creando los colisionadores y los puntos de lectura. Aquí se agrega lo que se ve: cojines redondeados, cortinas, cuadros, libros variados y plantas con hojas.
2. **Paisaje (`paisaje.py`).** Una función de altura por tipo de exterior, con cerros y montañas a escala real (hasta 4,5 km). Lo cercano es malla; lo lejano (terreno, miles de árboles repetidos, agua, edificios) solo se dibuja en el panorama.
3. **Materiales (`materiales.py`).** Nodos procedurales: tablas de madera, telas, alfombras, pasto, roca, nieve y agua.
4. **Luz.** Cielo físico de Blender con sol; de noche, luna y lámparas. La exposición se mide desde los puntos de lectura y es la misma para las tres texturas.
5. **Horneado.** Cada cara recibe su lugar en el atlas; las hojas usan celdas de 4 píxeles. Se hornea luz directa e indirecta difusa con Cycles y se pasa a sRGB con curva fílmica.
6. **Panorama (`cielo.py`).** Se renderiza el paisaje con fondo transparente y se compone con el cielo: base física, sol, nubes, bruma por distancia y, de noche, estrellas, vía láctea y luna.

## Formato de malla FXM1

`"FXM1"`, número de vértices y de índices (uint32), posiciones (float32 × 3, coordenadas de Unity), UV (float32 × 2) e índices (uint32). Lo lee `BakedEnvironment.cs`.
