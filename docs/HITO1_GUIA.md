# Hito 1 — Guía paso a paso (Pop!_OS + Quest 3)

Objetivo: instalar en el Quest 3 una app mínima que muestre un **libro 3D con páginas de un PDF**, con passthrough, mandos Touch y manos, y que se pueda **agarrar y mover**.

> **Estado:** el Hito 1 está terminado y probado en Quest 3 por el usuario; la base está en `fase-1/preparacion`. La nitidez del Hito 1.1 mejoró en el visor. El Hito 1.2 (ya unido a `fase-1/preparacion`) añade el ajuste con el stick izquierdo, pendiente de prueba específica en Quest 3; en la nube solo se revisa el código a mano, sin compilar Unity. Si algo falla, anota en qué paso y qué mensaje apareció.

Tiempo estimado: 1–2 horas la primera vez (casi todo es descarga e instalación). Espacio en disco: unos 15–20 GB.

---

## Qué trae esta rama

| Archivo | Para qué sirve |
| --- | --- |
| `hito1/Assets/FolioXR/Scripts/Reading/FolioBook.cs` | El libro: carga el PDF, muestra dos páginas, botones para avanzar y retroceder |
| `hito1/Assets/FolioXR/Scripts/Reading/PdfPageRenderer.cs` | Puente entre Unity y el plugin de Android |
| `hito1/Assets/FolioXR/Scripts/Environments/` | Primera casa procedural de prueba, temas y controles VR/passthrough; ver [Entornos](ENTORNOS.md) |
| `hito1/Assets/Plugins/Android/FolioPdfRenderer.java` | Dibuja las páginas con el lector de PDF de Android, en un hilo aparte |
| `hito1/Assets/FolioXR/Editor/FolioXRHito1Setup.cs` | Agrega el menú **Folio XR** al Editor de Unity |
| `hito1/Assets/StreamingAssets/folio-prueba.pdf` | PDF de prueba de 8 páginas, con contenido propio |
| `tools/generar_pdf_prueba.py` | Script que generó ese PDF |

El menú del Editor no depende del SDK de Meta. Los scripts de ejecución del libro y del entorno sí requieren Meta XR Core SDK (versión de esta prueba: 207), además de Unity 6000.3, URP 17 y OpenXR.

---

## Paso 1 — Bajar el repositorio

En una terminal:

```bash
sudo apt install git curl
git clone https://github.com/loklj/el-m-s-perr-n-aqu-.git ~/folio-xr
cd ~/folio-xr
git checkout fase-1/preparacion   # incluye el Hito 1, el 1.1, el 1.2 y el entorno de prueba
```

## Paso 2 — Instalar Unity Hub (comandos oficiales para Ubuntu)

Unity solo da soporte oficial a Ubuntu 22.04/24.04. Pop!_OS se basa en Ubuntu, así que normalmente funciona, pero no está garantizado.

```bash
sudo install -d /etc/apt/keyrings
curl -fsSL https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor -o /etc/apt/keyrings/unityhub.gpg
echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/unityhub.gpg] https://hub.unity3d.com/linux/repos/deb stable main" | sudo tee /etc/apt/sources.list.d/unityhub.list
sudo apt update
sudo apt install unityhub
```

Abre Unity Hub e inicia sesión con una cuenta de Unity (la licencia Personal es gratis si cumples sus condiciones).

> **Si usas Pop!_OS 24.04 con el escritorio COSMIC:** Unity está pensado para GNOME sobre X11. Si el Editor se ve mal o se cierra, anótalo; la alternativa documentada es usar Ubuntu 24.04 LTS.

## Paso 3 — Instalar el Editor

En Unity Hub: **Installs → Install Editor →** la versión más reciente de **Unity 6.3 LTS**. En módulos, marca:

- **Android Build Support**
  - **OpenJDK**
  - **Android SDK & NDK Tools**

## Paso 4 — Crear el proyecto dentro del repositorio

En Unity Hub: **Projects → New project**

- Plantilla: **Universal 3D**
- Project name: `unity`
- Location: la carpeta del repositorio (`~/folio-xr`)

Así el proyecto queda en `~/folio-xr/unity`. Cierra Unity cuando termine de abrirse por primera vez.

## Paso 5 — Copiar los archivos del Hito 1 al proyecto

```bash
cd ~/folio-xr
cp -r hito1/Assets/. unity/Assets/
```

Vuelve a abrir el proyecto en Unity Hub. En la barra de menú debe aparecer **Folio XR**.

## Paso 6 — Configurar Unity para el Quest

Son los pasos de la guía oficial de Meta (*Manual Project Setup*):

1. **File → Build Profiles** → elige **Meta Quest** → **Enable Platform** (si pide instalar `com.unity.xr.openxr`, **Install**) → luego **Switch Platform**. En versiones anteriores a Unity 6.1, si no aparece Meta Quest, elige **Android**.
2. **Edit → Project Settings → XR Plug-in Management** → si lo pide, **Install XR Plugin Management**. En la pestaña de **Meta Quest/Android**, marca **OpenXR**.
3. En Unity: **Window → Package Management → Package Manager** → botón **+** → **Add package by name** → escribe `com.meta.xr.sdk.all` → **Add**. No hace falta pasar por la Asset Store.
4. Si pide activar el conjunto de funciones Meta XR, **Enable**. Si pide reiniciar el Editor, acepta. Si pregunta por *OpenXR Hand Skeleton*, elige **Use OpenXR Hand**.
5. **Project Settings → XR Plug-in Management → OpenXR**, pestaña Android: activa el grupo de funciones **Meta XR**.
6. Abre el **Project Setup Tool** (menú **Meta XR Tools** o **Window → Meta → Meta XR SDK**) y toca **Fix All** y luego **Apply All**.

## Paso 7 — Armar la escena

1. Abre la escena de la plantilla (`Assets/Scenes/SampleScene`).
2. Borra **Main Camera** de la jerarquía.
3. Abre **Building Blocks** (desde la ventana Meta XR SDK) y agrega, en este orden: **Camera Rig**, **Passthrough**, **Controller Tracking** y **Hand Tracking**.
4. Menú **Folio XR → Crear libro de prueba (Hito 1)**. Aparece el libro y se fija el identificador del paquete en `com.loklj.folioxr`.
5. El agarre propio `BookGrab` se crea automáticamente al iniciar y encuentra el libro y el `OVRCameraRig`: no hay que agregar interactores del Interaction SDK. Si la escena ya tiene `HandGrabInteractable`/`GrabInteractable`, no son necesarios para este agarre.
6. Guarda la escena (**Ctrl+S**).
7. Menú **Folio XR → Comprobar Hito 1**. Todo debe salir **OK**.

> Si al agregar un bloque aparece un segundo **Camera Rig**, borra el duplicado y deja solo uno.

## Paso 8 — Compilar el APK

1. **File → Build Profiles**: revisa que la escena esté en la lista (**Open Scene List → Add Open Scenes**).
2. Marca **Development Build** (en este hito es más fácil de diagnosticar).
3. **Build** → guarda como `~/folio-xr/Builds/FolioXR-hito1.apk`.

## Paso 9 — Instalar en el Quest 3

Con el modo desarrollador activo (ver `docs/PC_Y_VALIDACION.md`), conecta el visor por USB y acepta la depuración USB dentro del visor:

```bash
sudo apt install adb                          # si no tienes adb
adb devices -l                                # debe decir "device"
adb install -r ~/folio-xr/Builds/FolioXR-hito1.apk
```

En el visor: **Biblioteca → Orígenes desconocidos → Folio XR**.

## Paso 10 — Qué probar

| Prueba | Resultado esperado |
| --- | --- |
| EPUB · Importación y apertura | Importar libro-prueba.epub desde Biblioteca; aparece como abrible. Muestra capítulos en el orden del spine, primera y última página, tildes, listas, cursivas e imágenes en ambas páginas 3D |
| EPUB · Nitidez y captura | Con renderScale 1,4 y letra normal, leer 12 pt a 45 cm; comprobar que no falte texto en bordes ni se repita una columna. Revisar tiempos `[FolioXR] EPUB captura` y capturas de capítulos de distinta longitud |
| EPUB · Letra y progreso | Ajustes → Letra pequeña/normal/grande repagina. Conserva fracción aproximada de lectura, alineada a doble página; reiniciar conserva letra y página. Probar abrir otro EPUB guardado con otro conteo y volver |
| EPUB · Cancelación y caché | Cambiar de documento durante carga, cerrar/suspender y alternar PDF/CBZ/EPUB. No aparecen páginas del libro anterior. Recorrer más de 24 MiB de PNG y verificar LRU, memoria estable y precarga de la siguiente doble página; medir latencia real |
| EPUB · DRM y fuentes | rights.xml o AES en encryption.xml: «Este libro tiene DRM y no se puede abrir». Solo ofuscación IDPF/Adobe de fuentes: abre con fuente del sistema, sin descifrar. XML corrupto da error de lectura |
| EPUB · Contenido complejo y límites | Probar nav EPUB3, NCX EPUB2, imágenes y CSS relativos, caracteres escapados en rutas, scripts/URL externos bloqueados; capítulo >4 MiB debe fallar de forma controlada. Revisar SVG, tablas, RTL y maquetación fija por separado, sin darlos por soportados |
| EPUB · Regresiones | PDF y cómics mantienen su lector, páginas, agarre, escala y progreso; ambos menús muestran ajustes de letra; herramientas y esfera conservan su código |
| Menús movibles | Panel: barra azul clara bajo el panel. Esfera: barra bajo la esfera. Pellizcar (mano) o apretar el grip (mando) a menos de 6/10 cm de la barra y mover: el menú sigue y mira a la cabeza; al soltar queda ahí. Mientras arrastras no se pulsan botones |
| Entornos · once estilos | Menú → Entornos: elegir cada estilo. Se construye en ~1 s, mismo plano de casa con otros colores, paisaje, cielo y sol. Revisar que no haya huecos en el horizonte ni árboles dentro de la casa o el camino. Anotar las líneas `[FolioXR] Entorno: triángulos=` y el FPS medio |
| Entornos · lago y ciudad | Casa del lago: el muelle es caminable y el agua refleja el sol. Loft nocturno: ventanas y farolas encendidas, cielo con pocas estrellas |
| Entornos · salir y recordar | Passthrough restaura la luz de la escena (color, fuerza y dirección). Al reiniciar la app se recuerda el último estilo |

| Abrir la app | Ves tu habitación (passthrough) y un libro frente a ti |
| Portada | La página izquierda muestra la portada azul «Folio XR» |
| A o X | Avanza dos páginas; el texto de abajo dice «Páginas 3-4 de 8» |
| B o Y | Retrocede dos páginas |
| Página 2 y 4 | Se distinguen los tamaños de letra (8 a 18 puntos) |
| Agarrar con la mano izquierda/derecha | Con seguimiento fiable, empieza un pellizco índice-pulgar a menos de **6 cm** del colisionador del libro (medido desde el punto medio de ambas puntas). Sigue ese punto y la rotación de la muñeca conservando la pose relativa inicial, sin saltar al tomarlo. Al abrir los dedos cae en Realista o queda flotando en Flotante, sin pasar página. Log: `[FolioXR] Agarre: tomado con mano izquierda` / `mano derecha` y `[FolioXR] Agarre: soltado` |
| Agarrar con el mando izquierdo/derecho | Acerca el ancla del mando a menos de **8 cm** del colisionador y pulsa el botón de agarre (grip). Vibra brevemente al tomarlo y conserva la pose relativa mientras lo mantienes. Al soltar cae con inercia en Realista; queda inmóvil en Flotante. Log: `[FolioXR] Agarre: tomado con mando izquierdo` / `mando derecho` y `[FolioXR] Agarre: soltado` |
| Acercarse con pellizco/grip ya sostenido | No toma el libro: soltar y volver a pulsar dentro de la distancia para generar un flanco nuevo |
| Añadir la segunda mano o el segundo mando | Un nuevo gesto cercano entra en modo dos manos/mandos. Sigue el punto medio y permite cambiar el tamaño; soltar una conserva la pose y deja el control en la otra |
| Pérdida de confianza durante un agarre con mano | Conserva la última pose durante la pérdida; si vuelve antes de 0,3 s con el pellizco sostenido, continúa. Si la pérdida supera 0,3 s, suelta. Recuperar el seguimiento todavía pellizcando no vuelve a agarrar ni pasa página: abrir y hacer un gesto nuevo |
| Pellizcar lejos mientras el libro está agarrado | Ninguna mano pasa página. Tras soltar el libro y abrir los dedos con seguimiento fiable, un pellizco nuevo lejos vuelve a pasar página como antes |
| Agarrar con el menú abierto y recolocar después | Funciona con manos y mandos aunque el menú pause los otros controles. Tras soltar y cerrar el menú, el clic del stick derecho vuelve a poner el libro frente a la vista |
| Clic en el stick derecho | El libro vuelve frente a tu vista |
| Clic en el stick izquierdo (entorno de prueba) | Alterna passthrough/casa. Al entrar, aparece el sofá de la sala y el libro se recoloca; al salir, vuelve el passthrough y se restauran la pose original del rig y el fondo de la cámara. Mantener pulsado no repite la acción |
| Stick izquierdo hacia la izquierda (dentro de la casa) | Camina lateralmente a la izquierda respecto a la mirada. Los puntos de lectura se eligen en Entornos del menú |
| Stick izquierdo hacia la derecha (dentro de la casa) | Camina lateralmente a la derecha respecto a la mirada. En passthrough el eje horizontal no desplaza el rig |
| Stick izquierdo arriba/abajo en passthrough (Hito 1.2) | Sube/baja la nitidez entre 1,4 y 1,8, de 0,1 en 0,1: un paso por movimiento, sin repetición al mantenerlo inclinado; vuelve al centro para rearmarlo. El valor inicial es 1,4 y el elegido se conserva al cerrar y abrir la app. Muestra «Nitidez: 1,6x (stick izquierdo arriba/abajo). Más nitidez usa más batería.» durante 2,5 s y vuelve al estado actual de páginas; un error visible tiene prioridad |
| Pellizco (índice + pulgar) con la mano **derecha**, lejos del libro (Hito 1.1) | Avanza dos páginas |
| Pellizco con la mano **izquierda**, lejos del libro (Hito 1.1) | Retrocede dos páginas |
| Nitidez (Hito 1.1) | Texto de 14 puntos legible a ~45 cm; comparar con la primera versión |
| ☰ del mando izquierdo (menú simple) | Abre/cierra una vez por pulsación. Panel de 40 × 28 cm, a 45 cm y 10 cm bajo los ojos, inclinado 15°, con fundido de 0,15 s. Conserva la última categoría; al reabrir tras alejarte más de 1,5 m o mirar a otro lado, reaparece delante |
| Palma izquierda arriba, cerrar en puño y abrir | Abierta → puño mantenido ≥0,15 s → abierta antes de 1,2 s alterna el menú; espera 0,8 s entre gestos. Con palma inclinada ≥45°, baja confianza o gesto del sistema no actúa. El log muestra ángulo y aperturas/cierres. El pellizco izquierdo no retrocede páginas mientras la palma está arriba |
| X del panel | Cierra el menú. La pausa continúa hasta soltar botones/pellizcos y centrar el stick izquierdo; en la casa también el derecho y el pellizco medio derecho |
| B o Y con el menú abierto | Cierra sin retroceder página, incluso manteniéndolo pulsado. Tras soltar, una pulsación nueva vuelve a servir para pasar página |
| Índice izquierdo contra un botón | Hover y un clic al cruzar el plano desde delante; retirar el dedo antes de volver a tocar. Perder seguimiento o aparecer ya dentro del panel no pulsa un botón |
| Índice derecho contra un botón | Mismo toque, con clic suave y fondo que se hunde brevemente; mantener el dedo dentro no repite |
| Apuntar con el mando izquierdo y pulsar su gatillo | El rayo ilumina el botón y el flanco del gatillo izquierdo lo activa; clic y vibración breve. Mantener el gatillo al abrir no selecciona nada |
| Apuntar con el mando derecho y pulsar su gatillo | Mismo comportamiento con rayo y gatillo derechos; el gatillo del otro mando no activa este rayo |
| Botones Biblioteca / Entornos / Ajustes / Importar y nubes / Hora | Cambian la categoría resaltada. Biblioteca identifica el PDF actual; biblioteca futura e importación muestran «Próximamente» sin ejecutar acciones |
| Entornos → Passthrough | Restaura el passthrough y la pose original, como el clic del stick izquierdo. Cerrar y reabrir el panel lo recoloca en la nueva posición |
| Entornos → Casa moderna cálida | Entra una sola vez, marca la casa y muestra sus cinco lugares. Volver a pulsarla no reemplaza la pose guardada para salir |
| Entornos → Sofá / Biblioteca / Dormitorio / Balcón / Terraza | Cada botón lleva a su punto de lectura y recoloca el libro. No exige mover el stick; los atajos del entorno quedan en pausa mientras está abierto el menú |
| Ajustes → Nitidez 1,4 / 1,5 / 1,6 / 1,7 / 1,8 | Marca el valor actual, muestra el aviso habitual y lo conserva al reiniciar. Al cerrar, el stick sigue ajustando por pasos sin saltos por una inclinación sostenida |
| Ajustes → Estilo de menú: Simple | Guarda `folioxr.menuStyle = simple`. «Esfera (próximamente)» está deshabilitado; cualquier preferencia anterior sigue usando el panel |
| Hora → 15 / 30 / 45 / 60 min | Inicia/reinicia un solo temporizador y marca su duración. Al vencer, tono suave y aviso de cinco segundos, también con el menú cerrado, sin cerrar el libro. Hora HH:mm y batería visibles, actualizadas por minuto |
| Hora → Apagar | Cancela el temporizador; no apaga el visor ni cierra el libro |
| A/X, pellizcos y sticks mientras el menú está abierto | No cambian páginas, nitidez, lugar ni posición del libro. Agarrar el libro sigue funcionando. Cerrar con botones sostenidos no los filtra al libro; solo se reanudan tras soltarlos |
| Tanda A · Letras de todos los botones | Un TextMesh centrado por botón (incluida la X), máximo dos líneas y margen mínimo de 3 mm. Revisar «Casa moderna cálida», «Sofá de la sala», «Sillón de la biblioteca» y las doce opciones actuales de Ajustes. Nada sale de sus rectángulos, también con el panel girado. Verificar ≤30 draw calls del menú en Quest |
| Tanda A · Diagnóstico por intento | Cada inicio de pellizco/puño genera una línea `[FolioXR] Agarre intento:` con mano, gesto, fiabilidad, tipo/cantidad de huesos, presencia de los huesos usados, respaldo y distancia al colisionador. Mantener el gesto no repite líneas |
| Tanda A · Puño izquierdo y derecho | Con la mano abierta cerca del libro, cerrar: media de las cuatro puntas a la palma <6 cm y palma a <8 cm del colisionador. Conserva desplazamiento/rotación al tomar; abrir suelta. El puño usado para sostener el libro no alterna el menú |
| Tanda A · Respaldo sin huesos | Desactivar OVRSkeleton o retirar los huesos en una escena de prueba, conservando OVRHand fiable. Abrir y pellizcar con la pose del OVRHand a <6 cm toma el libro; log `respaldoOVRHand=True`. Probar ambas manos, huesos legacy y XRHand. Sin huesos el respaldo es por pellizco, no por estimación geométrica de puño |
| Tanda A · Lanzar y apoyar en la casa | En Realista, soltar desde ambas manos/mandos: masa 0,6 kg, gravedad, fricción alta, velocidad media de los últimos cinco cuadros y colisión continua. Se apoya en suelo, mesa, sofá, cama, estanterías y escalones; choca con paredes, techo, barandas y terreno. En la jerarquía los BoxCollider están en objetos sin Renderer; las siete mallas combinadas se conservan |
| Tanda A · Piso passthrough | Soltar junto a los pies estando de pie y sentado: cae hasta el piso real (`trackingSpace.position.y`, origen FloorLevel). Recentrar el visor y alternar casa/passthrough: el piso invisible solo está activo en passthrough y no queda a la altura del dormitorio/balcón |
| Tanda A · Traer libro | Clic del stick derecho con menú cerrado o Ajustes → Traer libro: cancela incluso un agarre bimanual, borra velocidades y queda flotando quieto frente a la vista. Mantener el gesto anterior no lo recupera; abrir y agarrar de nuevo. Solo agarrar y soltar reactiva gravedad en Realista |
| Tanda A · Recuperación automática | En una escena de prueba, colocar el libro libre a >20 m de la cabeza o a >2 m bajo el piso: vuelve quieto frente a la vista y registra recuperación. En la casa se usa el suelo de la casa, no la altura del rig sentado |
| Tanda A · Física guardada | Ajustes → Física alterna Realista/Flotante y actualiza el texto. Reiniciar conserva `folioxr.bookPhysics` (1/0); sin preferencia empieza en Realista. Cambiar a Flotante detiene una caída; volver a Realista espera al siguiente agarre/suelta |
| Tanda A · Escala con dos manos | Tomar primero con una y luego con la contraria cerca. Duplicar/reducir la separación duplica/reduce el tamaño inicial, limitado a 0,5×–5×. Sigue el punto medio con desplazamiento inicial y la rotación de la primera mano |
| Tanda A · Escala con dos mandos | Repetir con ambos grips, en ambos órdenes. Un lado físico no cuenta dos veces como mano y mando. Soltar primero cualquiera de los dos vuelve a una mano sin salto; soltar ambos activa la física elegida |
| Tanda A · Seguimiento durante escala | Ocultar una mano <0,3 s congela la pose hasta recuperarla. Si supera 0,3 s, continúa con la mano restante; si se pierden ambas, suelta sin lanzamiento artificial. Cambiar entre huesos/respaldo no debe producir salto ni velocidad falsa |
| Tanda A · Tamaño normal y persistencia | Ajustes → Tamaño normal restablece 1×, incluso mientras se sostiene; la próxima muestra no restaura la escala anterior. Soltar/recolocar/suspender guarda `folioxr.bookScale`; reiniciar conserva el tamaño |
| Tanda A · Resolución de páginas ampliadas | Superar 1,5× y estabilizar el tamaño ≥0,35 s vuelve a renderizar la doble página visible. Log de ancho en tramos de 256 px, máximo 4096. Reducir a ≤1,5× recupera la resolución base (mínimo 1800). Pasar páginas y cambiar tamaño durante una carga no mezcla solicitudes; verificar nitidez, memoria y latencia en Quest |
| Tanda A · Regresión de controles | Repetir páginas por A/X, B/Y y pellizcos lejanos; menú por botón y gesto; cinco lugares de lectura desde el menú; nitidez con stick en passthrough; retorno de passthrough. Dynamic Resolution sigue desactivada. Validar la locomoción con las filas de Tanda B; no hay herramientas nuevas |
| Tanda B · Caminar con stick izquierdo | Solo en la casa: avance/retroceso y desplazamiento lateral relativos a la mirada horizontal, máximo 1,4 m/s también en diagonal; aceleración/frenado de 3,5 m/s². Mirar arriba/abajo no hace volar. Entrar con el stick inclinado exige centrarlo antes de caminar |
| Tanda B · Colisiones y escalera | Ir contra paredes, mesa, cama, sofá, estanterías y barandas; la cápsula no atraviesa los sólidos. Subir/bajar los 18 escalones con `stepOffset = 0,3`, gravedad 9,81 m/s² y sin quedarse flotando al salir de un borde. Probar también movimientos físicos horizontales del visor |
| Tanda B · Altura de cabeza | De pie, sentado y agachado: la cápsula sigue X/Z de la cabeza, su altura hasta el tracking space y 10 cm de margen superior. Revisar contactos al incorporarse cerca de un techo y límites de seguimiento; no simular atravesar objetos reales |
| Tanda B · Cinco puntos desde Entornos | Llegada sobre suelo libre junto a cada asiento conservando altura física. No aparece dentro del sofá/cama. El CharacterController está deshabilitado durante el cambio; tras cerrar el menú no hay rebote y se puede caminar. El libro se recoloca como antes |
| Tanda B · Giro por pasos | Stick derecho X >0,7 o <−0,7: un giro de 30° alrededor de la cabeza; sostenerlo no repite, cruzar directamente al otro extremo tampoco. Rearmar solo con abs(x)<0,3. Probar desplazado físicamente del centro del rig: la cabeza no describe una órbita |
| Tanda B · Giro suave | Ajustes → Giro: suave; tras centrar, giro continuo proporcional al eje, máximo 60°/s, alrededor de la cabeza. Volver a por pasos restaura el enclavamiento. No gira mientras se apunta con el arco |
| Tanda B · Arco con mando | Mantener stick derecho Y>0,7, inicialmente centrado en X: arco desde el mando derecho. Verde y círculo de destino si es válido, naranja sin círculo si no lo es. Soltar hasta Y<0,3 salta al último destino mostrado, comprobado de nuevo; desviarlo lateralmente por encima de 0,7 cancela |
| Tanda B · Destinos permitidos | Suelos, alfombras, escalones, balcón, terraza y terreno del entorno, normal Y≥0,95, distancia 3D desde los pies ≤10 m y cápsula completa libre con margen para la cabeza. Rechazar mesas, asientos, estantes, techos, paredes, huecos y puntos bajo muebles/techos bajos o a >10 m. Una mano atravesando una pared no permite apuntar a través de ella |
| Tanda B · Teletransporte con manos | Sin mando derecho activo, abrir con confianza alta y pellizcar MEDIO+pulgar derecho: mantener muestra el arco con PointerPose válido; una suelta fiable salta. Índice+pulgar sigue pasando página lejos y agarrando cerca; por sí solo nunca inicia teletransporte. Añadir índice durante el arco lo cancela |
| Tanda B · Pérdida de seguimiento | Perder confianza de mano/dedos, PointerPose, seguimiento del mando/visor, foco o suspender cancela el arco sin salto. Recuperar con gesto sostenido no actúa: abrir/centrar y repetir. Cambiar de mano a mando durante el arco tampoco salta |
| Tanda B · Menú y rearme | Abrir menú mientras se camina, gira o apunta: detención inmediata y arco oculto. Soltar detrás del menú no teletransporta. Al cerrar, esperar dos cuadros neutros con ambos sticks centrados y pellizcos índice/medio derecho abiertos; el libro puede seguir agarrándose |
| Tanda B · Libro acompañado | Caminar, girar y teletransportarse sosteniendo el libro con una o dos manos/mandos: acompaña en el mismo cuadro, conserva escala y agarre; no bloquea la cápsula. Al soltar, no sale disparado por la distancia del salto. Repetir Realista/Flotante y cambio de mano |
| Tanda B · Viñeta | Viñeta suave por ojo solo mientras hay desplazamiento por stick, intensidad creciente con velocidad y desaparición gradual al frenar. No al empujar una pared sin avanzar. Ajustes → Viñeta: no la desactiva; el menú y passthrough restauran inmediatamente la cámara. Verificar en la APK que el renderer conserva postprocesado y variantes de Vignette |
| Tanda B · Preferencias y panel | Reiniciar conserva `folioxr.locomotionVignette` (1 sí/0 no, predeterminado 1) y `folioxr.smoothTurn` (1 suave/0 pasos, predeterminado 0). Las doce opciones de Ajustes quedan dentro del panel, textos centrados y ≤30 draw calls de menú |
| Tanda B · Passthrough y clics | Clic izquierdo sigue alternando modos y restaura la pose guardada; CharacterController deshabilitado en passthrough. Stick izquierdo Y sigue ajustando nitidez allí; ningún stick mueve/gira/salta. Clic derecho sigue trayendo el libro. En la casa el izquierdo nunca cambia nitidez ni punto de lectura |
| Tanda B · Rendimiento y ciclo de vida | Medir FPS en Quest caminando con viñeta y mostrando arco, incluyendo libro a 3×. Repetir casa/passthrough y suspensión: sin duplicar cápsulas, materiales, volúmenes ni rayos. Al salir no quedan viñeta, colisiones ni caída aplicadas al rig |
| Tanda C · Reloj izquierdo | Con manos fiables, anclado al hueso de muñeca izquierdo (legacy/XRHand); sin huesos, respaldo OVRHand. Con mando, `leftControllerAnchor`. Seis símbolos dibujados por código: lupa, libreta, lápices, tinta, linterna y regla. Crecen suavemente al mirar a <70 cm y se ocultan al perder el anclaje. Comprobar posiciones y legibilidad sentado/de pie |
| Tanda C · Tomar del reloj | Abrir primero y empezar pellizco o puño derecho a <3 cm del centro del ícono de linterna; con mando derecho, flanco de grip a <3 cm. Un inicio con confianza baja vale si vuelve en ≤0,3 s; acercarse con el gesto antiguo sostenido no toma. Un solo dueño por lado físico, sin toma simultánea del libro |
| Tanda C · Cinco herramientas futuras | Tocar con el índice derecho cada uno de los otros cinco símbolos, retirándolo >2,5 cm entre toques; también probar pellizco/puño/grip cerca. Muestra «Próximamente» durante 2,5 s. No aparece otra herramienta, escritura, cámara de lupa ni libreta funcional |
| Tanda C · Soltar y devolver | Soltar a <10 cm de la muñeca izquierda: vuelve el ícono, linterna apagada y log de devolución. Fuera de esa distancia: permanece en el mundo, collider activo y caída desde reposo en Realista; en Flotante queda quieta. No hereda velocidad artificial del teletransporte |
| Tanda C · Recuperar del mundo | Tras abrir, tomar a <3 cm del collider con pellizco, puño o grip de cualquiera de los dos lados; comprobar A en derecho y X en izquierdo. Cambiar Física a Flotante detiene una caída; volver a Realista espera la siguiente toma/suelta |
| Tanda C · Propiedad de entradas | Con herramienta en derecha, su pellizco/grip no agarra libro ni pasa páginas; A/B/gatillo quedan reservados. Repetir en izquierda con X/Y. La otra mano/mando conserva sus controles. Devolver con A/B/X/Y sostenido y abrir/cerrar el menú: ninguna página ni cierre por flanco falso |
| Tanda C · Normal | Gatillo del propietario alterna una vez por pulsación; A/X recorre Normal → Láser → Ultravioleta. En Normal: una luz Spot de 35°, alcance 20 m, sin sombras. Probar sobre superficies Lit de la casa; las páginas Unlit no reciben iluminación real |
| Tanda C · Láser | Rayo rojo de 1,2 mm hasta el primer sólido o 20 m, punto rojo solo con impacto. Apuntar a suelo, pared, muebles y libro; actualizar después de caminar/girar/saltar. Apagar y cambiar de modo elimina rayo y punto |
| Tanda C · Ultravioleta y API | Luz violeta tenue y cono suave, alcance <3 m para `UltravioletLight.Reveals(punto)`. En una prueba de Unity: aceptar punto frontal a 1 m, rechazar apagada/Normal/Láser, detrás, fuera de 17,5° y a ≥3 m; poner una pared entre emisor y punto y rechazar. El soporte en el punto admite 1 mm de tolerancia; no se implementa aún tinta |
| Tanda C · Botones con índice contrario | Con linterna en mano derecha, retirar índice izquierdo >2,5 cm y tocar a <1,2 cm el botón verde (encendido) o violeta (modo). Repetir con manos intercambiadas después de dejarla en el mundo. Mantener contacto, perder confianza o reaparecer con dedo dentro no repite ni activa; retirar para rearmar |
| Tanda C · Respaldo URP | En asset activo, comparar Additional Lights = Per Pixel con máximo >0 frente a Disabled/Per Vertex/máximo 0. Sin luces por píxel: aviso en log y cono falso para Normal/UV, Light deshabilitada; láser y API UV siguen operativos. El chequeo también detecta cambiar de asset/calidad o de esa opción en ejecución |
| Tanda C · Guardar herramientas y menú | Ajustes contiene trece opciones en siete filas, incluidos «Guardar herramientas», Física, Tamaño normal y Traer libro. Guardar recupera desde el mundo o cualquier mano, apaga la linterna y exige soltar antes de otra toma. El lado ocupado no selecciona el panel con gatillo/índice ni cierra con B/Y; usar el lado libre o devolver. Menú abierto pausa nuevas tomas y acciones de herramienta |
| Tanda C · Seguimiento y suspensión | Ocultar la mano/mando propietario: conserva pose hasta 0,3 s; después devuelve sin caída. Suspender/quitar foco guarda todas y apaga luz/rayos. Recuperar tracking con gesto sostenido no vuelve a tomar; abrir primero. Ocultar solo muñeca izquierda no pierde la herramienta sostenida |
| Tanda C · Regresión y rendimiento | Repetir agarre/escala del libro con dos manos y dos mandos, páginas, menú, casa, movimiento, giro y teletransporte con linterna sostenida. Al moverse acompaña al rig en el mismo cuadro. Solo una Light adicional creada, cero sombras y mallas/materiales/textura de cono creados una vez; perfilar FPS, GC, draw calls, overdraw del cono y variantes URP en la APK de Quest 3. Ver logs de tomar/soltar/devolver/cambiar modo |
| Tanda D · Lápiz: tomar y escribir | Tomar el ícono de lápices del reloj. Apoyar la punta (mina de color, hacia delante) en el libro, la libreta, una pared o un mueble de la casa: aparece una línea pegada a la superficie. En el aire no escribe. Al despegar más de 5 mm la línea termina |
| Tanda D · Colores | Con mando: A/X avanza y B/Y retrocede por negro, azul, rojo, verde, morado y resaltador amarillo transparente (el log dice `[FolioXR] Lápiz: color …`). Con manos: tocar con el índice contrario el extremo de color del lápiz (la banda junto a la goma roja) |
| Tanda D · Goma | Pasar la goma roja (punta trasera) por encima de un trazo: se borra completo. También borra tinta invisible. El log dice `goma borró N trazo(s)` |
| Tanda D · Libro | Escribir en una página, pasar de página y volver: el trazo reaparece en su página. Cambiar el tamaño del libro: el trazo crece con él. Cerrar y abrir la app: sigue ahí (log `Trazos: cargados …`) |
| Tanda D · Casa | Entrar a la casa, escribir en una pared. Salir a passthrough y volver, o reiniciar la app: el trazo sigue en la pared |
| Tanda D · Tinta invisible | Tomar la pluma oscura con anillo violeta y escribir: no se ve nada. Encender la linterna en modo ultravioleta y apuntar a menos de 3 m: el texto aparece en violeta claro, solo dentro del cono |
| Tanda D · Libreta | Tomar la libreta (A5, 20 páginas rayadas). Escribir en ella mientras se sostiene con la otra mano o apoyada en una mesa. Tocar la esquina inferior derecha (marca cian) con el índice: página siguiente; la izquierda: anterior. Con mando, A/X y B/Y. Al soltarla cae y se apoya como el libro (física Realista) |
| Tanda D · Borrar desde el menú | Ajustes → «Borrar trazos de esta página»: el botón pasa a «¿Borrar? Toca otra vez»; un segundo toque antes de 4 s borra las dos páginas visibles. Igual con «Borrar trazos de la casa» (solo dentro de la casa) |
| Tanda D · Rendimiento | Con muchos trazos (cientos en la casa) mantener 72 FPS. El registro anota trazos y vértices de cada lienzo al cargarlo |
| Tanda E · Regla | Tomar la regla (30 cm, transparente, mm y números cada cm). Dejarla sobre el libro: sirve de guía para leer. Con el lápiz a menos de 1 cm de su borde largo, la línea sale recta, paralela a la regla |
| Tanda E · Lupa | Tomar la lupa: mirando a través del lente se ve el libro ampliado 2,5×. Con mando: stick arriba/abajo cambia el aumento (1,5× a 4×) y A/X, B/Y lo suben o bajan de a 0,5; mientras tanto ese stick no mueve ni gira. Con manos: tocar los botones cian (+) y violeta (−) junto al lente. El aumento se ve bajo el lente |
| Tanda E · Lupa en pausa | Dejar la lupa a más de 1 m del libro (o mirar el lente por detrás): deja de renderizar (log `Lupa: en pausa`) y el lente queda como vidrio. En passthrough, lo que no es virtual se ve negro dentro del lente |
| Esfera · Abrir | Botón ☰ izquierdo, o cerrar y abrir la palma izquierda hacia arriba: aparece una esfera azul de 14 cm sobre el mando o la palma izquierda, con cinco casillas (Biblioteca, Entornos, Ajustes, Importar y nubes, y la hora con batería). El registro dice `Menú abierto: Esfera.` El reloj de herramientas se oculta mientras tanto |
| Esfera · Girar | Con la mano: deslizar el índice derecho sobre la esfera; al soltar sigue girando, se frena y queda centrada en una casilla. Con mando: stick a la izquierda o derecha, una casilla por movimiento. Suena un clic por casilla (y vibra el mando) |
| Esfera · Elegir | Tocar con el índice la casilla del frente (marco claro) y retirar el dedo, o gatillo. La esfera gira hacia arriba: la categoría queda arriba, atenuada, y aparecen sus opciones. Tocar una casilla lateral solo la trae al frente |
| Esfera · Opciones y volver | En Ajustes, girar por todas las opciones (nitidez, menú, física, viñeta, giro, tamaño, guardar herramientas, borrar trazos, traer libro): los textos caben en sus casillas y la opción actual se ve azul claro. «Volver» está a la izquierda de la primera; B/Y también vuelve y, desde las categorías, cierra |
| Esfera · Cambiar de estilo | Ajustes → «Menú: Simple»: la esfera se cierra y aparece el panel. En el panel, «Menú: Esfera» vuelve a la esfera. La elección se conserva al reiniciar la app |
| Esfera · Pausa de controles | Con la esfera abierta, A/X, B/Y, pellizcos y sticks no pasan página ni mueven al usuario. Al cerrarla vuelven a funcionar tras soltar los controles |
| 11 nov · Reloj de pulsera | Con manos: un reloj (caja gris, esfera azul oscuro con la hora y correa) aparece en el dorso de la muñeca izquierda. Tocar su esfera con el índice derecho abre encima la bandeja con las 6 herramientas y el botón «Menú»; se cierra sola a los 8 s, al elegir algo o al tocar otra vez el reloj. Con mandos, los íconos siguen alrededor del mando izquierdo |
| 11 nov · Sacar herramientas tocando | Con manos: tocar con el índice derecho un ícono del reloj, sin pellizcar: la herramienta aparece en la mano derecha y queda pegada. Para soltarla: pellizcar (o cerrar la mano) y abrir. Para guardarla: alejarla y volver a acercarla al reloj |
| 11 nov · Guantes de gravedad | Apuntar con la mano abierta (o el mando) a una herramienta en el suelo o al libro (a más de 1,5 m): aparece un círculo cian. Pellizcar (o grip) y tirar la mano hacia atrás: el objeto vuela en arco. Una herramienta llega a la mano; el libro queda frente a la vista |
| 11 nov · Caminar con manos | En la casa: pellizco pulgar + dedo medio IZQUIERDO sostenido = caminar hacia donde apunta esa mano; soltar = detenerse. El teletransporte con pulgar + medio DERECHO ahora usa fuerza de pellizco (antes exigía confianza alta y casi nunca funcionaba) |
| 11 nov · Gesto del menú | Palma izquierda hacia arriba, abrir, cerrar el puño y abrir: más tolerante (no exige confianza alta). El registro anota «palma abierta hacia arriba» y «puño cerrado» para diagnosticar |
| 11 nov · Trotar | Ajustes → «Velocidad: caminar/trotar»: trotar va a 3 m/s (caminar 1,4 m/s). Se conserva al reiniciar |
| Encargo 5 · Biblioteca | Menú → Biblioteca → «Importar libros»: se abre el selector de archivos de Horizon OS; elegir uno o varios PDF. Se ve «Importando N de M · %» y luego el aviso «Se importaron …». El libro aparece en la lista con su progreso; tocarlo lo abre. Si el visor no tiene selector, aparece un aviso (y el registro lo anota) |
| Encargo 5 · Progreso y marcadores | Leer unas páginas, cerrar la app y volver: abre el último libro en su página. «Marcar pág. N» guarda un marcador; «Ir al marcador» vuelve a él. «Quitar de la biblioteca» → tocar un libro → «¿Quitar? Toca otra vez» |
| 11 nov · Contorno de gravedad | Al apuntar a una herramienta o al libro lejano aparece un contorno blanco que late; al pellizcar (fijar) brilla fijo y sale un haz blanco hasta la mano |
| 11 nov · Cómics CBZ | Importar un .cbz desde la biblioteca y tocarlo: se abre como libro, una imagen por página, en orden natural de nombre |
| 11 nov · Esfera con manos | Al abrirla con la mano queda fija donde apareció (con mando sigue al mando). Tocar con el índice cualquier casilla visible la elige al retirar el dedo, aunque el dedo se hunda en la esfera. El registro anota «dedo sobre la esfera» y «toque con el índice» |
| Encargo 8 · Hoja que pasa | Pasar página: una hoja curvada cruza de un lado al otro en 0,35 s con un roce de papel. Al cambiar de nitidez o de libro no hay animación |
| Encargo 8 · Manga | Biblioteca → «Lectura: normal/manga»: en manga la primera página queda a la derecha y la hoja pasa hacia la derecha. Se recuerda por libro. Los trazos siguen en su página |
| Encargo 8 · Cerrar libro | Biblioteca → «Cerrar libro»: guarda la página y vuelve al libro de muestra |
| Encargo 9 · Acostado | Ajustes → «Acostado: sí»: el libro se coloca hacia donde mira la cabeza (también hacia el techo) y el movimiento libre se desactiva |
| Encargo 9 · Recentrar | Mantener el botón Meta para recentrar: el libro vuelve frente a la vista (si no está en la mano) |
| Encargo 9 · Suspender | Quitarse el visor con el libro o una herramienta apoyados y volver: nada sale lanzado ni atraviesa el suelo; el progreso quedó guardado |
| Encargo 9 · Interrupciones | Cerrar la app a mitad de una importación y volver a abrir: no queda un libro a medias (el registro dice «parciales borrados»). Importar sin espacio: aviso «No hay espacio suficiente…» |
| Encargo 12 · Wi-Fi desde el PC | Menú → Importar y nubes → «Desde el PC por Wi-Fi»: aparece una dirección (http://192.168.x.x:8080) y un código de 6 dígitos. En el navegador del PC (misma red Wi-Fi) abrir esa dirección, escribir el código, elegir archivos y «Enviar al visor»: barra de progreso por archivo y el libro aparece en la Biblioteca. Con un código errado responde «Código incorrecto». «Cerrar la carga por Wi-Fi» o quitarse el visor cierra el servidor |
| Encargo 14 · Agregar catálogo OPDS | Con la carga por Wi-Fi abierta, en la página del PC, sección «Agregar un catálogo (OPDS)»: nombre, dirección (p. ej. `http://192.168.1.10:8083/opds` en Calibre-Web, `/api/opds/<clave>` en Kavita, `/opds/v1.2/catalog` o `/opds/v2/catalog` en Komga), usuario, contraseña y el código del visor → «Agregar al visor». En unos segundos aparece «Catálogo: nombre» en Importar y nubes |
| Encargo 14 · Navegar y descargar | Tocar el catálogo: se ven sus carpetas (▸) y libros, de 6 en 6 («Más»), «Siguiente página del catálogo» y «Volver». Tocar un libro descarga el mejor formato (EPUB > PDF > CBZ > MOBI > TXT) con porcentaje y aviso al terminar; aparece en la Biblioteca. Un libro con DRM (Adobe o LCP) se ve como «con DRM: no se puede». Contraseña errada: «Usuario o contraseña incorrectos» |
| Encargo 14 · Quitar catálogo | «Quitar un catálogo» → tocar el catálogo → «¿Quitar? Toca otra vez» |
| Encargo 6 · PDF rápido | Pasar páginas de un PDF: el registro dice `PDF: página N lista en X ms` y, al avanzar, «(precargada)». Ya no se codifica PNG: las páginas llegan como píxeles crudos |
| Encargo 6 · Páginas enormes | Un PDF con páginas gigantes (carteles, planos) se ve en menor resolución en vez de fallar (registro: «sin memoria… se reduce a») |
| 11 nov · Ajustes compactos | En Ajustes, «Nitidez: 1,4», «Letra: normal» y «Menú: esfera/simple» son una sola casilla cada una: cada toque pasa al valor siguiente |
| 11 nov · + Agregar libro | Biblioteca → «+ Agregar libro»: «Desde el visor», «Desde el PC por Wi-Fi» (muestra dirección y código), «Desde un catálogo (OPDS)», «Volver» |
| 11 nov · Libro nuevo | Al terminar de agregar un libro, Biblioteca muestra «Libro nuevo»: «Nombre» (abre el teclado del sistema; por defecto el nombre del archivo), «Lectura: normal/manga», «Tapas: las del archivo / de cuero con el título», «Cuero: color» (marrón, negro, rojo vino, verde bosque, azul marino, morado, miel), «Listo» y «Listo y abrir». «Editar libro» vuelve a esta pantalla para el libro abierto |
| 11 nov · Tapas | Girar el libro abierto y mirarlo por fuera: tapa de cuero del color elegido con el título dorado en la portada y en el lomo. Con «las del archivo», la portada y la contraportada son la primera y la última página. En manga la portada queda del otro lado |

El entorno de prueba ya está en `fase-1/preparacion`; consulta [instalación, presupuesto y comprobaciones pendientes](ENTORNOS.md). El eje vertical del stick izquierdo conserva el ajuste de nitidez del Hito 1.2 también dentro de la casa.

### Actualizar a una versión nueva del código (por ejemplo, el Hito 1.2)

Con Unity cerrado: `git pull`, volver a copiar `hito1/Assets` al proyecto (paso 5), abrir Unity, **Build** y reinstalar (paso 9). La escena y los Building Blocks se conservan.

## Paso 11 (opcional) — Probar con tu propio PDF

```bash
adb push mi-libro.pdf /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
```

Cierra la app y ábrela de nuevo. Si existe `libro.pdf`, se abre ese en vez del de prueba. Para volver al de prueba:

```bash
adb shell rm /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
```

> PDF con contraseña: el lector de PDF de Android no los abre, y la app lo indica con un mensaje.

## Paso 12 (opcional) — Probar con tus PDF grandes (hasta 20 GB)

El lector debe abrir archivos de varios GB. Esta prueba usa los PDF grandes que ya tienes en el visor.

> **Importante:** mientras el PDF esté dentro de la carpeta de la app, **desinstalar Folio XR lo borra**. Actualizar con `adb install -r` no lo borra. Al terminar, devuélvelo a su lugar (punto 5).

1. Abre Folio XR una vez para que exista su carpeta de datos y ciérrala.

2. Busca tus PDF de más de 1 GB y anota la ruta exacta:

   ```bash
   adb shell "find /sdcard -iname '*.pdf' -size +2000000 2>/dev/null"
   ```

3. **Mueve** (no copies) uno a la carpeta de la app con el nombre `libro.pdf`. Usa comillas si el nombre tiene espacios:

   ```bash
   adb shell mv "/sdcard/Download/Mi PDF grande.pdf" /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
   ```

   Dentro del mismo almacenamiento debería ser instantáneo. Si demora, se está copiando y necesita espacio libre igual al tamaño del PDF. Si `mv` da error, avísame: hay otra forma.

4. Abre la app con el registro activo y prueba:

   ```bash
   adb logcat -s Unity FolioXR AndroidRuntime > folio-pdf-grande.log
   ```

   Anota los resultados en esta tabla:

   | Archivo | Tamaño | Páginas | Segundos hasta ver la 1.ª página | ¿Pasan las páginas con A/X? | Errores |
   | --- | --- | --- | --- | --- | --- |
   | | | | | | |

5. **Devuélvelo a su lugar** al terminar:

   ```bash
   adb shell mv /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf "/sdcard/Download/Mi PDF grande.pdf"
   ```

Qué esperar y qué significa cada caso:

- **Abre y pasan las páginas:** el lector de PDF de Android del Quest soporta ese tamaño.
- **«La página es demasiado grande para la memoria disponible»:** el archivo abrió, pero esa página trae una imagen gigante (típico de escaneos). Anota el número de página.
- **«No se pudo abrir el archivo…» u otro error:** guarda `folio-pdf-grande.log`. Dice si el límite está en el lector de PDF de Android; en ese caso se cambia de motor (MuPDF ya abrió un PDF de 20 GB en las pruebas en PC).

**Alternativa sin tus archivos:** genera un PDF de prueba en tu PC y cópialo al visor como `libro.pdf` (necesita espacio libre en el visor igual a su tamaño):

```bash
cd ~/folio-xr
sudo apt install python3-pil
python3 tools/test-data/gigantes.py --solo pdf-gb --gb 5                    # 5 GB, ~1 minuto
python3 tools/test-data/gigantes.py --solo pdf-gb --gb 20 --limite-gb 25    # 20 GB
adb push tools/test-data/salida/pdf-5gb-imagenes.pdf /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
```

Sus páginas son ruido de colores con «Página N de M» encima: es a propósito, para que el archivo realmente pese lo indicado. Bórralo al terminar con `adb shell rm /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf`.

## Si algo falla

Guarda el registro mientras reproduces el problema y termínalo con Ctrl+C:

```bash
adb logcat -s Unity FolioXR AndroidRuntime > folio-hito1.log
```

Anota qué paso falló y qué mensaje viste (en Unity, en la terminal o dentro del visor).

| Síntoma | Qué revisar |
| --- | --- |
| No aparece el menú Folio XR | Que `unity/Assets/FolioXR` exista y la consola de Unity no tenga errores en rojo |
| El libro sale en blanco con «No se pudo preparar el PDF» | Que exista `unity/Assets/StreamingAssets/folio-prueba.pdf` |
| Los botones A/B/X/Y no hacen nada | En OpenXR → Android, que esté activo un perfil de interacción de los mandos Touch (el Project Setup Tool suele activarlo) |
| No hay passthrough (fondo negro) | Que esté el bloque **Passthrough** y que el Project Setup Tool no muestre pendientes |
| `adb devices` dice `no permissions` o no muestra el visor | Desconecta y conecta el cable, acepta el aviso dentro del visor y revisa los permisos USB de Linux: <https://developer.android.com/studio/run/device> |
| Error de clase Java no encontrada en el log | Si activaste Minify, marca **Custom Proguard File** en Publishing Settings |
| El build falla con `.../NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/clang++: not found` | El módulo **Android SDK & NDK Tools** quedó a medias (pasó el 7 de noviembre de 2026 en el PC de pruebas). Con Unity cerrado, reemplazar la carpeta `.../PlaybackEngines/AndroidPlayer/NDK` por el NDK **r27c** oficial: `https://dl.google.com/android/repository/android-ndk-r27c-linux.zip`, descomprimido con `unzip` (conserva los enlaces simbólicos) |
| La consola muestra en rojo `[Meta XR Simulator Installer] Meta XR Simulator is not supported on this platform` | No es un error del código ni impide compilar: el simulador de Meta solo existe para Windows y Mac. Se puede ignorar |
| El Project Setup Tool activó **Dynamic Resolution** («Using Dynamic Resolution can help improve quality…») | Esa función cambia la resolución sola y pasaría por encima de la nitidez elegida con el stick. Desde el 8 de noviembre de 2026 `FolioBook` la desactiva al arrancar y lo anota en el registro (`[FolioXR] Dynamic Resolution desactivada…`). Con versiones anteriores, desmarcar **Enable Dynamic Resolution** en el OVR Manager del Camera Rig |
| El disco del sistema se llena al instalar paquetes o compilar | En Unity 6 la caché de paquetes está en `~/.cache/Unity` (no en `~/.config/unity3d/cache`); moverla al disco grande y dejar un enlace simbólico. Unity Hub descarga en su «Ubicación de descargas» (Ajustes → Instalaciones), no en `TMPDIR` |

## Después

El Hito 1 ya está confirmado. Prueba el ajuste de nitidez del Hito 1.2 y registra el resultado en `PROGRESO.md`. El **Hito 2: prototipo de página EPUB en el libro 3D** no está iniciado ni autorizado.
