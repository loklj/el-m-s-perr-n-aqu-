# Entorno de prueba 1 — casa moderna cálida

Esta rama (`entornos/prueba-1`) añade un primer entorno para medir la lectura en VR en Quest 3. La app sigue arrancando en passthrough. El objetivo es probar **una casa de las 20 previstas**; los otros 19 temas se añadirán después. Esta entrega no inicia el Hito 2 ni cambia el lector de PDF.

La casa tiene una planta de 12 × 10 m y dos pisos comunicados por una escalera: sala de estar y biblioteca con estanterías llenas abajo, dormitorio con balcón arriba, a 3,15 m. Una terraza junto al jardín completa las zonas de lectura. El estilo usa madera clara, paredes crema, ventanales, plantas y lámparas decorativas; fuera hay pasto, vegetación y relieve de pocos polígonos bajo un cielo de día. Los ventanales se representan con huecos y marcos, sin superficies de vidrio transparente que añadan sobrepintado.

Todo se construye al ejecutar la app con mallas propias y texturas pequeñas generadas por código, sin modelos, texturas, paquetes nuevos, archivos `.meta` ni `asmdef` en el repositorio. Unity generará sus propios `.meta` al importar los scripts en el proyecto local.

## Instalar la prueba

Partir del proyecto del Hito 1 que ya funciona con **Unity 6000.3, URP 17, Meta XR Core SDK 207 y OpenXR**. Con Unity cerrado y sin cambios locales pendientes en los archivos que se copiarán:

```bash
cd ~/folio-xr
git fetch origin
git checkout entornos/prueba-1
git pull --ff-only
cp -r hito1/Assets/. unity/Assets/
```

Abrir Unity, compilar y reinstalar el APK siguiendo los pasos 8 y 9 de [HITO1_GUIA.md](HITO1_GUIA.md). Se conserva la escena existente: `FolioEnvironmentController` se crea automáticamente mediante `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` si no existe. Necesita el libro, el `OVRCameraRig`, la cámara principal y el `OVRPassthroughLayer` de los Building Blocks del Hito 1.

No hay que crear otro rig, arrastrar componentes a la escena ni repetir el menú de creación del libro. Los materiales del libro que ya están referenciados por la escena sirven de origen para los materiales del entorno.

## Controles y puntos de lectura

| Control | Acción |
| --- | --- |
| Clic del stick izquierdo | Alterna entre passthrough y casa, una vez por pulsación |
| Stick izquierdo a la izquierda | Punto de lectura anterior, solo dentro de la casa |
| Stick izquierdo a la derecha | Punto de lectura siguiente, solo dentro de la casa |
| Stick izquierdo arriba/abajo | Ajuste existente de nitidez; sigue funcionando en ambos modos |
| Clic del stick derecho | Recolocación existente del libro frente a la vista |

El desplazamiento horizontal actúa cuando `abs(x) > 0,7` y se rearma cuando `abs(x) < 0,3`. Mantener el stick inclinado no repite saltos; pasar directamente de un extremo al otro tampoco los repite sin volver antes al centro. El controlador solo lee el eje X y el clic del mando izquierdo; `FolioBook.cs` conserva el eje Y. Una diagonal que supere ambos umbrales puede cambiar de punto y de nitidez.

El recorrido es circular:

| Orden | Zona | Asiento |
| --- | --- | --- |
| 1 | Sala de estar, piso 1 | Sofá |
| 2 | Biblioteca, piso 1 | Sillón |
| 3 | Dormitorio, piso 2 | Cama |
| 4 | Balcón, piso 2 | Silla |
| 5 | Terraza del jardín | Tumbona |

Cada punto define la posición de los ojos, aproximadamente 1,2 m sobre el piso de su zona, y una dirección horizontal. Al saltar, se gira el rig solamente alrededor del eje Y y se corrige su posición usando `centerEyeAnchor`: la altura real y el desplazamiento local del visor no se suman otra vez a la altura del asiento. A continuación se llama a `FolioBook.PlaceInFrontOfView()` y se muestra temporalmente el nombre del punto con un `TextMesh` y `LegacyRuntime.ttf`.

Cada entrada empieza en el sofá. La casa se genera en la primera entrada y las siguientes reutilizan su raíz con `SetActive`. Al entrar se desactiva el passthrough y la cámara usa un fondo sólido opaco. Al salir se oculta la casa, vuelve el passthrough, se restauran la posición y rotación originales del rig y los valores exactos de `clearFlags` y `backgroundColor` que tenía la cámara antes de entrar, y se recoloca el libro.

## Generación y datos

Los scripts están en `hito1/Assets/FolioXR/Scripts/Environments/`, bajo `FolioXR.Environments`:

- `EnvironmentTheme` contiene los datos: paleta, medidas, densidad de muebles y semilla.
- `EnvironmentThemes.All` contiene un único tema en esta entrega.
- `HouseGenerator.Build(EnvironmentTheme theme, Transform parent)` construye la casa y devuelve un `HouseBuildResult` con `Root`, `ReadingPoints`, `TriangleCount`, `MeshCount` y `MaterialCount`. Su `Dispose()` libera la raíz y los recursos que genera.
- `FolioEnvironmentController` gestiona los mandos, el modo de visualización, la colocación del rig, el aviso del punto y el registro de FPS.

La semilla permite repetir la decoración sin depender del estado aleatorio de otros sistemas. Cada `ReadingPoint` tiene `Name`, `EyePosition` y `ViewDirection`; posición y dirección se expresan respecto a la raíz generada, de modo que el controlador las transforma al espacio de la escena.

Para añadir uno de los otros 19 temas, copiar la entrada de datos existente en `EnvironmentThemes.All` y cambiar estos campos:

| Campos de `EnvironmentTheme` | Uso y límites |
| --- | --- |
| `Name`, `Seed` | Nombre del estilo y semilla de decoración |
| `HouseWidth`, `HouseDepth` | Ancho de 10–18 m y fondo de 9–16 m |
| `StoreyHeight`, `WallThickness` | Altura entre pisos de 2,8–4 m y grosor de paredes de 0,12–0,30 m |
| `FurnitureDensity` | Densidad de decoración, entre 0,5 y 1,5; las cinco zonas se conservan |
| `Wall`, `Wood`, `Fabric`, `Metal`, `Foliage`, `BookPalette` | Colores de materiales y paleta de lomos de libros |
| `SkyHorizon`, `SkyZenith` | Degradado del cielo |
| `AmbientSky`, `AmbientEquator`, `AmbientGround` | Luz ambiente Trilight |

La estructura de dos pisos y cinco zonas la resuelve el mismo generador; no hace falta duplicar el código de construcción ni añadir recursos externos. Mantener los valores dentro de los límites admitidos y volver a comprobar geometría, vistas y rendimiento. Esta prueba usa el primer tema y no incorpora todavía un selector de entornos.

## Presupuesto y materiales

| Recurso del entorno | Límite |
| --- | --- |
| Triángulos | 150.000 |
| Draw calls | 60 |
| Materiales | 8, incluido el texto temporal |
| Dimensión de cada textura procedural | 256 px como máximo |
| Sombras en tiempo real | Ninguna |

Al terminar la construcción se combinan las mallas por material con `Mesh.CombineMeshes`; si la cantidad de vértices lo exige, se usa `IndexFormat.UInt32`. La geometría usa siete materiales: madera, crema, tapizado, metal, vegetación, atlas de libros y cielo; el aviso comparte el material de la fuente como octavo. Las texturas son de 128 × 128 px para madera y lomos, y 16 × 128 px para el cielo. Las lámparas son geometría decorativa: no añaden luces ni sombras. Se usa la luz direccional existente sin sombras y luz ambiente de `RenderSettings` en modo `Trilight`.

Los materiales se clonan de la tapa (URP Lit) y las páginas (URP Unlit) del `FolioBook` encontrado en la escena. El generador no depende de `Shader.Find` ni de un shader `Skybox/Procedural` que pueda desaparecer del build. El cielo es una esfera con caras hacia dentro, material Unlit clonado y una textura de degradado generada en memoria. No se modifican los materiales originales del libro.

La revisión estática da una cota conservadora de 22.160 triángulos para la geometría incluso con densidad 1,5: menos de 1.500 cajas (12 triángulos), 60 cilindros (40), 100 octaedros (8) y el cielo (960). El rótulo queda holgadamente dentro de los 1.000 triángulos reservados. No es una medición de Unity. El generador rechaza resultados que excedan 149.000 triángulos, siete mallas o siete materiales. Los siete lotes y el rótulo requieren ocho dibujos de color por vista (16 con dos vistas separadas), antes de las pasadas adicionales de URP; el límite de 60 debe confirmarse con el renderer del proyecto en el visor.

La navegación de esta prueba se hace entre los cinco puntos. La arquitectura es decorativa, sin colisionadores ni locomoción continua. Al salir también se restauran el alcance de cámara, la luz ambiente, las reflexiones y las sombras previas.

Al generar se registra:

```text
[FolioXR] Entorno: triángulos=..., mallas=..., materiales=..., generación=... ms
```

Mientras la casa está activa se registra el promedio de FPS cada cinco segundos. Los contadores de geometría corresponden al entorno generado; los FPS corresponden a la app en ejecución. El libro, manos, mandos y las pasadas de render de URP pueden añadir trabajo aparte: la cantidad de mallas combinadas no es una medición de draw calls del visor.

## Comprobación pendiente en Unity y Quest 3

Esta entrega se revisa a mano en la nube. **No se ha compilado Unity ni medido rendimiento en Quest 3.** Las métricas que imprime el código deben recogerse en el APK, no interpretarse como resultados ya obtenidos. La revisión de Claude prevista en `PROGRESO.md` también queda pendiente antes de compilar.

1. Importar los scripts y comprobar que Unity no muestra errores de compilación; compilar un APK para confirmar que los materiales y la fuente siguen incluidos.
2. Abrir la app: debe verse el passthrough y el libro, sin casa. Pulsar el stick izquierdo: deben aparecer el sofá, el cielo de día y el libro frente a la vista.
3. Recorrer los cinco puntos en ambos sentidos. Revisar muebles, caras visibles, ventanas, hueco de escalera, alturas, balcones y vistas; probar sentado y con el visor desplazado respecto al centro físico del rig.
4. Mantener cada dirección y el clic: debe producirse una sola acción. Volver al centro debe permitir otra. Verificar que arriba/abajo sigue ajustando nitidez y que los controles del PDF funcionan en ambos modos.
5. Alternar varias veces: debe generarse una sola casa y recuperarse el passthrough sin duplicar objetos; comparar la pose del rig y el fondo de la cámara antes y después de cada visita.
6. Capturar `adb logcat -s Unity FolioXR AndroidRuntime` y conservar la línea de generación y varios intervalos de FPS de cada punto. Comparar nitidez 1,4 y 1,8 durante una sesión sostenida; la media de cinco segundos puede ocultar tirones individuales.
7. En un Development Build conectado, usar **Profiler → Rendering** y **Frame Debugger** para comprobar draw calls, triángulos y pasadas reales. Distinguir los renderers del entorno del libro, manos y mandos; comprobar también el total de la app. Revisar CPU/GPU y memoria al entrar por primera vez y después de varias alternancias.

Los riesgos principales por validar son el tirón de la primera generación, la fidelidad de materiales y fuente en Android, el coste de dibujar mallas combinadas grandes, los tiempos de cuadro con nitidez alta y el comportamiento del tracking al recentrar o reanudar. Las cifras de presupuesto no sustituyen estas pruebas.
