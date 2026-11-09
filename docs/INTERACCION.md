# Interacción: libro con física, movimiento libre y herramientas de muñeca

Este documento es la especificación para que Codex (GPT-6 Astra) programe por tandas y Claude revise cada una. Sale de la prueba del usuario del 9 de noviembre de 2026 y de sus respuestas.

## Resultado de la prueba (9 de noviembre de 2026, 04:15)

- La casa le parece bien como idea. En el visor se ven bien el cielo, los árboles, la pérgola y el libro.
- El agarre **funciona con el mando, pero no con las manos**. Probó con pellizco y con la mano completa.
- El menú le gusta, pero **las letras son demasiado grandes para los botones**: «Casa moderna cálida», «Sofá de la sala» y «Sillón de la biblioteca» se salen del botón.

## Lo que pidió el usuario

> «quiero que tenga movimiento libre por el entorno, quiero que el libro tenga físicas realistas, también que cuando se agarre con las 2 manos o mandos y los alejes se haga más grande y cuando lo acerques se haga más pequeño y también implementa una lupa, una libreta, varios lápices de colores y tinta invisible y una linterna triple (linterna normal, láser y ultravioleta), una regla, estos estarán en las muñecas»

## Decisiones del usuario

| Tema | Decisión |
| --- | --- |
| Movimiento | **Stick y teletransporte**. Con mandos, el stick izquierdo camina y el derecho gira por pasos. Con las manos, se apunta y se salta. La nitidez y los lugares de lectura pasan al menú dentro de la casa; en passthrough el stick izquierdo sigue ajustando la nitidez |
| Herramientas | **Reloj en la muñeca izquierda**: íconos alrededor de la muñeca. Se toman con la otra mano y se devuelven soltándolos cerca de la muñeca |
| Dónde se escribe | **En cualquier superficie, en la libreta y en los libros** (no en el aire) |
| Agarre con manos | Falló tanto con pellizco como con la mano completa |

## Tanda A — Arreglos y libro: menú, agarre con manos, física y tamaño con dos manos

**1. Letras del menú simple**
- Un `TextMesh` por botón, centrado (`MiddleCenter`) dentro de su rectángulo.
- El texto se achica hasta caber en el ancho y el alto del botón, con un máximo de 2 líneas y márgenes de 3 mm. Partir de 0,8 cm en categorías y 0,7 cm en opciones.
- Ningún texto puede salirse del botón. Se permiten hasta 30 draw calls en el menú.

**2. Agarre con las manos**
- **Diagnóstico.** En cada intento (inicio de pellizco o de puño), registrar una vez: si la mano es fiable, si se encontró el `OVRSkeleton` (tipo y cantidad de huesos), si se encontraron los huesos usados y la distancia al libro.
- **Pellizco.** Índice y pulgar, con el punto de pellizco a menos de **6 cm** del colisionador del libro.
- **Mano completa.** La distancia media de las puntas de índice, medio, anular y meñique al centro de la palma baja de 6 cm, y la palma está a menos de **8 cm** del libro.
- **Respaldo sin huesos.** Si el esqueleto o sus huesos no están disponibles, usar la pose del `OVRHand` (su `Transform` o `PointerPose`) y `GetFingerIsPinching`, para que el agarre no dependa solo de los huesos.

**3. Física realista del libro**
- **Al soltarlo**, el libro pasa a Rigidbody dinámico: gravedad, unos 0,6 kg, fricción alta y detección de colisión continua. Sale con la velocidad de lanzamiento, que es el promedio de los últimos 5 cuadros de la mano o el mando.
- **Colisionadores de la casa.** La casa genera `BoxCollider` en objetos aparte sin renderer: pisos, paredes, techos, muebles, estanterías, barandas, escalera (escalones o rampa) y terreno. Las mallas combinadas no cambian.
- **En passthrough** hay un piso invisible a la altura del piso real, que es la altura del tracking space del `OVRCameraRig` con origen en el piso.
- **«Traer libro».** El clic del stick derecho y la nueva opción «Traer libro» del menú dejan el libro frente a la vista **flotando quieto** (cinemático). La gravedad vuelve recién cuando se agarra y se suelta, para que traerlo no lo haga caer.
- **Recuperación.** Si el libro cae 2 m bajo el piso o queda a más de 20 m, vuelve solo frente a la vista.
- **Ajustes del menú:** «Física: Realista / Flotante», guardado en PlayerPrefs con la clave `folioxr.bookPhysics`. Por defecto es Realista. «Flotante» es el comportamiento actual: queda donde se suelta.

**4. Tamaño con dos manos o dos mandos**
- Si el libro ya está agarrado y la otra mano o mando también lo agarra, entra en **modo dos manos**. Esto reemplaza la transferencia actual a la segunda mano.
- **Escala:** escala inicial × (distancia actual entre las manos / distancia inicial), entre **0,5× y 5×** (el máximo era 3× y el usuario lo subió a 5× el 9 de noviembre de 2026).
- **Movimiento:** la posición sigue al punto medio entre las manos, con el desplazamiento inicial. La rotación sigue la primera mano.
- Al soltar una mano, se vuelve a una mano con la que queda, sin saltos.
- El tamaño se guarda en PlayerPrefs con la clave `folioxr.bookScale`. En Ajustes hay una opción «Tamaño normal» que vuelve a 1×.
- Si la escala supera 1,5×, las páginas visibles se vuelven a renderizar con más resolución, hasta 4096 px de ancho, para que no se vea pixelado.

## Tanda B — Movimiento libre (stick y teletransporte)

Solo funciona dentro de la casa; en passthrough no cambia nada.

**Con mandos**
- **Stick izquierdo:** camina hacia donde se mira, en horizontal, a 1,4 m/s con aceleración suave.
- **Stick derecho a los lados:** gira por pasos de 30° alrededor de la cabeza.
- **Stick derecho hacia adelante (mantener):** aparece un arco desde el mando derecho con una marca en el destino. Al soltar el stick, salta.

**Con manos**
- Pellizco con el **dedo medio y el pulgar** de la mano derecha, mantenido: aparece el arco desde la mano. Al soltar, salta.
- Se usa el dedo medio para no confundirlo con el pellizco del índice, que pasa página y agarra.

**Colisiones**
- Un `CharacterController` en el rig, del alto de la cabeza, con gravedad.
- Sube escalones de hasta 30 cm y las paredes y los muebles lo detienen.
- El teletransporte solo acepta pisos (normal hacia arriba, sin paredes encima) a 10 m como máximo.

**Comodidad**
- Viñeta suave en los bordes de la vista mientras se camina.
- En Ajustes: «Viñeta: sí / no» y «Giro: por pasos / suave».

**Cambios de controles dentro de la casa**
- El stick izquierdo deja de ajustar la nitidez y de cambiar de lugar; ambas cosas quedan en el menú.
- El clic del stick izquierdo sigue alternando entre la casa y el passthrough.

## Tanda C — Reloj de herramientas y linterna triple

**El reloj**
- Es un anillo de 6 íconos alrededor de la muñeca izquierda (de la mano o del mando): lupa, libreta, lápices, tinta invisible, linterna y regla.
- Los íconos son pequeños y crecen al mirar la muñeca, cuando queda frente a la cara.

**Tomar y devolver herramientas**
- **Tomar:** con la mano derecha, pellizco o puño a menos de 3 cm del ícono; con el mando derecho, grip cerca del ícono. La herramienta aparece en esa mano.
- **Devolver:** soltar la herramienta a menos de 10 cm de la muñeca izquierda.
- Si se suelta en otro lugar, queda en el mundo y cae con física si la física está en Realista.
- En el menú hay una opción «Guardar herramientas» que las devuelve todas al reloj.

**Linterna triple**
- Tiene tres modos:
  - **Normal:** luz spot real, sin sombras, de unos 35° y 20 m.
  - **Láser:** un rayo rojo fino con un punto donde choca (raycast).
  - **Ultravioleta:** luz violeta tenue que revela la tinta invisible.
- **Con mando:** el gatillo la enciende y la apaga, y A/X cambia de modo. Mientras se sostiene una herramienta con un mando, sus botones son de la herramienta y no pasan página.
- **Con manos:** se toca con el índice de la otra mano el botón de la linterna para encenderla o apagarla, y el selector de modo para cambiarlo.
- Revisar si el asset de URP permite luces adicionales por píxel. Si no las permite, registrar un aviso y usar un cono de luz falso.

## Tanda D — Lápices de colores, tinta invisible y libreta

**Lápices**
- Hay 6 colores: negro, azul, rojo, verde, morado y resaltador amarillo semitransparente.
- Se cambia de color tocando con la otra mano el extremo del lápiz, o con A/X si se usa mando.
- La goma está en la punta contraria y borra los trazos que toca.

**Dónde se escribe**
- En cualquier superficie con colisionador: la casa, el libro, la libreta y la regla. No se escribe en el aire.
- Cuando la punta toca la superficie (a menos de 5 mm), el trazo se dibuja pegado a ella, 1 mm hacia afuera.

**Junto a la regla:** si la punta pasa a menos de 1 cm de su borde, el trazo sale recto, alineado con el borde.

**Dónde se guarda lo escrito** (JSON en `persistentDataPath`)
- En el libro: por PDF y por página, en coordenadas de la página. Así reaparece al volver a esa página.
- En la libreta: por página de la libreta.
- En la casa: por entorno, en coordenadas de la raíz de la casa.

**Tinta invisible**
- Es una pluma aparte del reloj, con trazos invisibles.
- Solo se ven dentro del cono de la linterna en modo ultravioleta, a menos de 3 m.

**Libreta**
- Tamaño A5, con 20 páginas.
- Se pasa de página tocando las esquinas con el índice.
- Se puede agarrar, dejar en cualquier parte y tiene física, igual que el libro.

## Tanda E — Lupa y regla

**Lupa**
- Lente de 8 cm con mango. Muestra ampliado 2,5× lo que hay detrás.
- Usa una cámara secundaria con RenderTexture de 512 px, alineada desde el ojo a través del centro de la lente. Solo renderiza mientras está en la mano o cerca del libro.
- El aumento va de 1,5× a 4×. Se cambia con el stick del mando que la sostiene o tocando los botones + y − de la lupa.

**Regla**
- Mide 30 cm, con marcas cada 1 mm y números cada 1 cm, en una textura generada por código.
- Tiene física, se puede apoyar sobre el libro y sirve de guía para leer línea por línea.
- Con el lápiz traza líneas rectas (ver la tanda D).

## Reglas para todas las tandas

- Todo se hace en código: sin modelos, texturas ni paquetes externos y sin `Shader.Find`. Los materiales se clonan de los del libro, igual que en la casa y el menú.
- Solo APIs que existan en Unity 6000.3, URP 17 y Meta XR Core 207. Cada script se crea solo con `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`.
- Cada tanda agrega sus filas a la tabla del Paso 10 de `docs/HITO1_GUIA.md`. `PROGRESO.md` lo actualiza Claude.
- Claude revisa cada tanda antes de integrarla en `fase-1/preparacion`, y el usuario la prueba en el Quest 3.


## Estado de la Tanda A

Implementada por Codex en `interaccion/tanda-a` e integrada en `fase-1/preparacion` tras la revisión de Claude (9 de noviembre de 2026). Falta compilar en Unity 6000.3 y probar en el Quest 3. No se inició el Hito 2 ni las tandas B–E; `PROGRESO.md` permanece intacto.

- **Menú:** un TextMesh por botón, `MiddleCenter`, altura inicial 8 mm en categorías y 7 mm en opciones. Prueba los cortes entre palabras, elige hasta dos líneas y reduce la geometría a los límites reales del rectángulo con 3 mm de margen. Ajustes conserva lo existente y añade Física, Tamaño normal y Traer libro. Máximo previsto: 21 renderizadores incluyendo aviso/reloj/rayos, pendiente de medir draw calls en visor.
- **Manos:** pellizco a 6 cm, puño a 8 cm con media de puntas a palma <6 cm. Reconoce huesos legacy y XRHand; busca también esqueletos hermanos dentro del rig. El respaldo utiliza `OVRHand.transform` y `GetFingerIsPinching(Index)` si faltan huesos válidos. Sin huesos no se estima el puño: sigue disponible el pellizco. Diagnóstico por inicio de gesto, tolerancia de seguimiento de 0,3 s y consumo del pellizco de agarre para no pasar páginas. El gesto de menú se rearma después de soltar el libro.
- **Física:** Rigidbody de 0,6 kg, fricción alta, gravedad y ContinuousDynamic al soltar; velocidad lineal y angular media de hasta cinco cuadros fiables. Suspender/recolocar limpia la historia. Los sólidos de la casa tienen BoxCollider independientes sin Renderer; no cambian sus mallas combinadas. Piso invisible en passthrough, con origen FloorLevel y superficie en `trackingSpace.position.y`. Recuperación bajo el piso en 2 m o a más de 20 m de la cabeza. Traer libro cancela ambos agarres y queda cinemático hasta el siguiente agarre/suelta. Preferencia `folioxr.bookPhysics`: entero 1 Realista (predeterminado), 0 Flotante.
- **Tamaño:** dos manos o dos mandos, razón de distancias desde la segunda toma, límites 0,5×–3×, punto medio con desplazamiento inicial y rotación de la primera mano. Al retirar cualquiera, conserva la pose y continúa con la otra. `folioxr.bookScale` se guarda al soltar, recolocar, restablecer o suspender. Tamaño normal también recalcula los desplazamientos si se sostiene el libro. Por encima de 1,5× se renderizan las páginas visibles a mayor resolución, en tramos de 256 px hasta 4096, después de 0,35 s estable y sin concurrencia con otra carga. Se elimina la copia CPU de las texturas de páginas; la vista previa del Editor sigue siendo de baja resolución.

**Validación:** revisión manual de nombres y tipos contra el código publicado de `com.meta.xr.sdk.core@207.0.0` (`OVRSkeleton`, `OVRHand`, `OVRManager`, `OVRCameraRig`) y la referencia oficial de Unity 6000.3 (`PhysicsMaterial`, `PhysicsMaterialCombine`, `Rigidbody.linearVelocity`, damping, colisión continua y `Renderer.localBounds`). Análisis sintáctico de los 13 archivos C# sin errores y `git diff --check` limpio; comprobado que no se añadió `Shader.Find` y que `PROGRESO.md` no cambió. La tabla del Paso 10 de `HITO1_GUIA.md` contiene las pruebas funcionales y de regresión. No se ha compilado Unity ni ejecutado el proyecto en Quest desde este entorno.

**Riesgos pendientes en visor:** comprobar la pose real de respaldo de cada mano y el umbral de puño; validar la geometría del texto y draw calls; ajustar si hiciera falta fricción/lanzamiento y contactos del libro fino contra escalones/barandas; verificar la calibración del piso real. A 4096 px aumenta el uso de memoria y la latencia del PDF: medir con documentos propios, especialmente páginas muy altas. El gesto bimanual exige una separación inicial mínima de 2 cm para evitar divisiones inestables. Los colisionadores de cilindros son cajas aproximadas y las vigas con escala no uniforme usan cajas envolventes.

**Revisión de Claude (9 de noviembre de 2026).** El código compila contra stubs de Unity 6000.3 y Meta XR 207. El registro de la prueba de las 04:13 mostró:

- 72 FPS estables en la casa, también con nitidez 1,8.
- La nitidez desde el menú funciona.
- El agarre con mando funciona.
- Los huesos de la mano izquierda se leyeron al principio (ángulo de la palma en el registro).

Cambios de la revisión:

- **Inicio del gesto con confianza baja.** El agarre con manos exigía que el pellizco o el puño empezaran con seguimiento de confianza alta. Al cerrar los dedos esa confianza suele bajar un instante, así que se perdía el inicio y el agarre nunca empezaba. Ahora un gesto que empieza con confianza baja vale si la confianza vuelve antes de 0,3 s con el gesto sostenido. Sigue habiendo un solo intento por gesto, y acercarse con el gesto ya hecho no agarra.
- **Respaldo para encontrar las manos.** Si las manos no cuelgan del rig, se buscan en toda la escena.
- **Resolución de páginas con techo de 3072 px**, en vez de 4096. A 4096 px cada página pesaba unos 127 MB con mipmaps y Android tardaba segundos en codificar el PNG. A 3× el libro ocupa unos 60° de la vista y el visor no muestra más de unos 1800 px en ese ángulo.
- **Origen del seguimiento.** Solo se cambia a FloorLevel si estaba en EyeLevel, para no mover un origen Stage. Después del cambio, el libro se vuelve a poner frente a la vista, porque la altura de la cabeza salta.


## Estado de la Tanda B

Implementada en `interaccion/tanda-b`, partiendo de `origin/fase-1/preparacion` (`d9fcda2`, Tanda A revisada). Pendiente de revisión e integración y de compilar/probar en Unity 6000.3 + Quest 3. No se inició el Hito 2 ni las tandas C–E. `PROGRESO.md` no se modifica.

- **Arranque y alcance:** `Locomotion/FolioLocomotion.cs`, namespace `FolioXR.Locomotion`, se crea con `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`. Su ayudante `LocomotionVisuals` crea y libera los recursos visuales por código. Solo actúa con la casa activa, seguimiento válido y `InputPaused == false`. «Casa» incluye el terreno, terraza y balcón ya generados por ese entorno; no añade locomoción a passthrough.
- **Caminar y colisionar:** CharacterController en OVRCameraRig, radio 20 cm, altura física de cabeza +10 cm (mínimo 60 cm), centro siguiendo la cabeza en horizontal, skin 2 cm, stepOffset 30 cm y gravedad 9,81 m/s². Movimiento a 1,4 m/s, aceleración/frenado 3,5 m/s² y diagonal limitada. También barre el desplazamiento físico horizontal para que actualizar el centro no atraviese los sólidos. Requiere el rig vertical a escala unitaria de la escena existente.
- **Giro:** 30° por flanco con abs(x)>0,7, rearme bajo 0,3; el giro suave alternativo es proporcional hasta 60°/s. Ambos giran alrededor de la cabeza, sin órbita, y se bloquean durante el arco. Tras pausa, cambio de modo o pérdida de seguimiento se exige centrar antes de actuar.
- **Teletransporte:** arco balístico de segmentos desde el mando derecho con Y>0,7, suelta bajo 0,3; o `OVRHand.GetFingerIsPinching(Middle)` derecho con mano, pulgar y medio de confianza alta y PointerPose válido. Una pérdida de confianza cancela y no se interpreta como suelta. Índice simultáneo cancela el arco; el pellizco de medio no inicia un agarre por puño. Cambiar de fuente, abrir el menú o suspender también cancela.
- **Validación del destino:** el generador registra explícitamente los BoxCollider de pisos, alfombras, escalones, balcón, terraza, sendero y terreno. No basta con una normal horizontal de una mesa: se exige pertenecer al registro, normal Y≥0,95, distancia 3D desde los pies ≤10 m y una cápsula libre con holgura superior. El arco para en el primer sólido y el destino vuelve a comprobarse al soltar. Consultas NonAlloc con rechazo conservador si se llenan sus buffers. Material del arco/marca clonado de la página URP Unlit, sin Shader.Find ni paquetes añadidos.
- **Recolocaciones y libro:** el entorno suspende la cápsula antes de mover el rig y esta se reactiva en otro cuadro. Las cinco llegadas ahora están en suelo libre junto a los asientos, conservando la altura física; se mantienen los nombres y orientaciones. Teletransporte y giro también deshabilitan la cápsula durante la transformación. BookGrab recibe el cambio completo del rig en LateUpdate para llevar el libro y sus poses almacenadas, incluido el modo bimanual, sin introducir velocidad de lanzamiento artificial. Solo se ignora la colisión cápsula/libro mientras está agarrado.
- **Comodidad y menú:** viñeta nativa `Vignette` de URP 17, con fundido e intensidad máxima 0,32, mediante VolumeProfile temporal; no crea un shader/material de pantalla propio. Activa postprocesado/actualización del volumen durante el efecto y restaura sus valores anteriores al parar o salir. Preferencias `folioxr.locomotionVignette` (1 por defecto) y `folioxr.smoothTurn` (0 por defecto). Ajustes añade «Viñeta: sí / no» y «Giro: por pasos / suave»; la cuadrícula adapta sus seis filas para contener las doce opciones. Menú cerrado exige también neutralizar el stick derecho y el medio derecho dentro de la casa.
- **Controles existentes:** nitidez y puntos de lectura permanecen en el menú dentro de la casa. En passthrough el izquierdo Y conserva la nitidez. Los clics izquierdo/derecho conservan alternar entorno/traer libro. No se bloquea caminar por estar agarrando el libro.

**Validación realizada:** análisis sintáctico de los 15 archivos C# sin errores, revisión manual de tipos/usings/miembros contra Meta XR Core `207.0.0` publicado (`OVRHand`, `OVRCameraRig`), UnityCsReference `6000.3` (`CharacterController`, consultas Physics) y Graphics `6000.3/staging` (URP 17: Vignette y CameraExtensions). Revisión de flancos/cancelación, orden Update/LateUpdate, filtros de destino y transferencia del libro. `git diff --check` limpio; sin llamadas nuevas a Shader.Find, paquetes, escenas ni cambios en PROGRESO.md. No se ha compilado Unity ni ejecutado física o render XR en este entorno. Las filas de Tanda B del Paso 10 de HITO1_GUIA.md son el protocolo pendiente para el visor, no pruebas ya ejecutadas.

**Riesgos pendientes en Quest:** medir coste del postprocesado, consultas del arco y actualización de la cápsula; comprobar que el renderer de la APK incluye postprocesado y no elimina variantes de Vignette creadas solo en runtime. Validar escalera, barandas, bordes de piso y cambio de altura bajo obstáculos, tanto sentado como de pie; las cajas aproximadas de la Tanda A siguen delimitando muebles y vigas. Calibrar dirección de PointerPose y estabilidad del pellizco medio. Verificar tamaño de texto de las seis filas y el libro a dos manos durante giros/saltos. El desplazamiento físico del visor y su actualización tardía requieren prueba real de contactos y comodidad.

**Revisión de Claude de la Tanda B (9 de noviembre de 2026).** El código compila contra stubs de Unity 6000.3 y Meta XR 207. Se revisaron estos puntos:

- La cápsula sigue a la cabeza y frena también el movimiento físico, para no atravesar paredes caminando en la habitación.
- Se suspende antes de cada recolocación del entorno.
- El libro agarrado acompaña el movimiento del rig (`CarryWithRig`).
- El pellizco del dedo medio está reservado para el teletransporte.
- Solo se aceptan pisos marcados con espacio libre para la cápsula.

Hubo un cambio. La viñeta usaba el posprocesado de URP, encendido en la cámara mientras se camina. En Quest eso agrega una pasada de pantalla completa justo al moverse, puede provocar tirones al reasignar texturas, y sus variantes de shader podrían faltar en el APK. Ahora es una malla pegada a la vista con un degradado radial generado por código, sobre un clon transparente del material de las páginas (cola Overlay).


## Estado de la Tanda C

Implementada en `interaccion/tanda-c`, desde `origin/fase-1/preparacion` (`46c1f19`, tandas A y B revisadas). Pendiente de revisión, compilación en Unity 6000.3 y prueba en Quest 3. No se inicia el Hito 2 ni se implementan las tandas D/E. `PROGRESO.md` permanece intacto.

- **Infraestructura:** namespace `FolioXR.Tools`, bootstrap único `ToolRegistry` con `RuntimeInitializeOnLoadMethod(AfterSceneLoad)`. El registro crea los seis componentes y sus recursos por código; `FolioTool` ofrece estados Stored/Held/World, callbacks de tomar/sostener/soltar/devolver, `ToolButtons` del mando propietario y `OtherIndexTouch` para el índice contrario. Cinco entradas usan `UnavailableTool`; D/E podrán sustituirlas al registrarlas sin duplicar agarre/física/entradas. No hay paquetes, prefabs, escenas, shaders ni materiales importados nuevos.
- **Lectura compartida:** `GrabInput` extrae de BookGrab la resolución de OVRHand/OVRSkeleton (incluidos hermanos y respaldo de escena), lectura legacy/XRHand, gesto de puño, pellizco de respaldo, ventana de inicio de 0,3 s, exclusión del medio reservado a teletransporte y muestreo de movimiento. BookGrab conserva sus distancias, seguimiento, diagnóstico, velocidades y escala; solo añade consulta de lado ocupado y supresión por lado. El helper expone muñeca e índice válidos para el reloj y los toques.
- **Reloj:** anclaje izquierdo a muñeca, respaldo OVRHand si faltan huesos, o `leftControllerAnchor` con mando real. Anillo de seis símbolos geométricos, orientados hacia la vista, con escala suave 0,65–1,2 al mirar (producto escalar >0,92 y distancia <70 cm). Radio base 5,2 cm. La toma del reloj es exclusivamente derecha, a <3 cm del centro del símbolo. Los cinco símbolos futuros muestran «Próximamente» 2,5 s por toque de índice o intento de toma, sin instanciar una herramienta funcional.
- **Propiedad y física:** un dueño por lado físico y prioridad de herramientas antes de BookGrab/FolioBook. La herramienta acompaña mano/mando y se parenta al tracking space mientras se sostiene para acompañar también locomoción y recolocaciones tardías. Al soltar a <10 cm de muñeca se devuelve y apaga; fuera queda en el mundo, con Rigidbody de 0,15 kg y ContinuousDynamic en Realista, o cinemático en Flotante. Cae desde reposo, sin lanzamiento derivado del rig. Se retoma del mundo con cualquiera de los dos lados a <3 cm del collider. Mientras se sostiene, su collider está deshabilitado para no empujar cápsula/libro. Cambiar a Flotante congela las herramientas libres; Realista vuelve a actuar tras otra toma/suelta.
- **Integración:** `FolioBook.SetToolInputSuppressed` y `BookGrab.SetToolSuppressed` reservan solo el lado necesario. FolioBook consume pellizcos hasta abrir y conserva los flancos reales de botones por mando. El menú respeta B/Y y gatillo del propietario, evita el gesto de puño izquierdo con herramienta y añade «Guardar herramientas». Las trece opciones aprovechan la cuadrícula adaptable existente, ahora de siete filas; no se cambia su geometría. Abrir menú pausa nuevas tomas y acciones, conservando seguimiento/suelta. Devolver con botones sostenidos no crea una página ni un cierre de menú accidental.
- **Linterna:** una única Light Spot sin sombras, Normal 35°/20 m, Láser rojo de 1,2 mm con LineRenderer y punto en el primer impacto sólido, UV violeta tenue con luz y cono suave. Gatillo alterna encendido; A/X cambia modo. Con manos, índice contrario contra botón verde o selector violeta (<1,2 cm); exige retirarse >2,5 cm, y perder fiabilidad desarma el toque. Los rayos se actualizan después de la locomoción. Guardar o perder seguimiento del propietario >0,3 s devuelve y apaga; perder foco/suspender guarda todas.
- **URP y materiales:** se consulta `GraphicsSettings.currentRenderPipeline` como `UniversalRenderPipelineAsset`, `additionalLightsRenderingMode == LightRenderingMode.PerPixel` y `maxAdditionalLightsCount > 0`. Si no se cumple, avisa y deshabilita la Light; Normal/UV muestran solo el cono falso. Detecta cambios de asset y de estas opciones. Materiales de geometría clonados de la página URP Unlit, transparencia como LocomotionVisuals; aviso con la misma fuente integrada que el menú. Mallas, materiales y textura degradada de 32×32 se crean una vez y se liberan al destruir el registro. No se añade `Shader.Find`.
- **API para D:** `UltravioletLight.Reveals(Vector3 punto)` requiere linterna activa, encendida en UV, distancia estrictamente <3 m y ángulo al eje ≤17,5°. Un Physics.Raycast comprueba obstáculos sólidos ignorando triggers y con tolerancia de 1 mm en el destino para la superficie del futuro trazo. Funciona también con el respaldo visual sin luces adicionales. No crea tinta, trazos ni persistencia.

**Validación realizada:** análisis sintáctico de los 22 archivos C# sin errores. Comparación por tokens de siete cuerpos de métodos extraídos contra la base: misma lógica, salvo parámetros de contexto y acceso añadido a muñeca/índice. Revisión manual de APIs contra Meta XR Core `207.0.0` publicado, UnityCsReference `6000.3`, documentación oficial de `Light.innerSpotAngle` y Graphics `6000.3/staging` (URP 17). Revisión de propiedad por lado, flancos, pausa, toma/suelta, devolución y liberación de recursos. `git diff --check` limpio; una sola creación de Light adicional, sin paquetes ni modificaciones de `PROGRESO.md`. Las filas nuevas del Paso 10 son pruebas pendientes en visor, no resultados ejecutados. No se ha compilado ni ejecutado Unity en este entorno.

**Riesgos pendientes:** calibrar orientación de linterna según muñeca/mando y distancia de toques en Quest, especialmente con oclusión de dedos; comprobar símbolos/textos y siete filas del menú. Medir coste de luz, transparencia, raycasts y draw calls junto al libro ampliado y locomoción. El material Unlit del libro no recibe iluminación real, y el cono falso es una indicación visual, no iluminación de superficies; su longitud se recorta por el rayo central y no por toda la sección del cono. La API UV sí comprueba oclusión por punto. Verificar variantes de luces adicionales/transparencia conservadas en la APK, contactos del collider aproximado y transiciones manos/mandos/casa/passthrough. «Guardar herramientas» permite recuperar una linterna perdida fuera del espacio visible.
