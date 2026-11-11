# Menú esférico 3D — diseño acordado

Hay una alternativa para quien prefiera algo más directo: el [menú simple](MENU_SIMPLE.md), un panel plano. Se elige en Ajustes.

Fecha: 7 de noviembre de 2026. Basado en el boceto del usuario («Menú esférico 3D, Meta Quest 3»): una esfera azul flotante anclada al mando izquierdo, con categorías alrededor, que se gira con la mano derecha. Este documento es la especificación para que Codex (GPT-6 Astra) lo programe y Claude lo revise.

## Decisiones del usuario

| Tema | Decisión |
| --- | --- |
| Cómo aparece | Botón de menú (☰) del mando izquierdo. Con las manos: **con la palma izquierda hacia arriba, cerrar y abrir la mano** (el mismo gesto la cierra) |
| Segundo nivel | **Otro anillo en la esfera**: al elegir una categoría, la esfera gira hacia arriba y muestra un anillo con sus opciones |
| Categorías | **Biblioteca, Entornos, Ajustes, Importar y nubes, Hora** |

## Comportamiento

**Aparecer y desaparecer**
- Mando: el botón ☰ izquierdo abre y cierra. En Unity XR es `CommonUsages.menuButton` del `XRNode.LeftHand`, por flanco.
- Manos (aclaración del usuario: «tiene que cerrar y abrir la palma izquierda hacia arriba»): con la palma izquierda hacia arriba, cerrar la mano en puño y volver a abrirla abre el menú, y el mismo gesto lo cierra. Girar la mano no lo cierra: la esfera sigue a la mano hasta que se repite el gesto o se pulsa ☰.
  - Palma hacia arriba: la normal de la palma forma menos de 45° con el eje vertical del mundo. La normal se calcula con el producto cruz entre muñeca→nudillo del índice y muñeca→nudillo del meñique de `OVRSkeleton`, con el signo correcto para la mano izquierda. Escribe el ángulo en el registro para comprobarlo en el visor.
  - Puño: la distancia media de las puntas de índice, medio, anular y meñique al centro de la palma (punto medio entre la muñeca y el nudillo del dedo medio) baja de 5,5 cm. La mano está abierta cuando esa distancia supera 8 cm; la diferencia entre ambos umbrales evita parpadeos.
  - Secuencia válida: mano abierta → puño durante al menos 0,15 s → mano abierta antes de 1,2 s, con la palma hacia arriba todo el tiempo. Después de cambiar, espera 0,8 s antes de aceptar otro gesto.
  - El gesto solo cuenta si la mano está rastreada con confianza alta (`IsTracked`, `IsDataHighConfidence`) y no hay un gesto del sistema en curso (`IsSystemGestureInProgress`).
  - Mientras la palma izquierda esté hacia arriba, el pellizco izquierdo no pasa páginas. Así, al cerrar la mano no se retrocede una página por accidente.
- Animación de 0,2 s al aparecer y desaparecer (escala de 0 a 1).

**Posición**
- Unos 10 cm sobre el mando o la mano izquierda, siguiendo su posición en tiempo real.
- El eje vertical de la esfera queda vertical. La esfera gira sola en Y para que la categoría de frente mire hacia la cabeza.
- Diámetro de unos 14 cm. Los textos deben medir al menos 1 cm de alto para leerse a unos 40 cm.

**Girar** (anillo de categorías, 72° entre cada una)
- Mano derecha: con la punta del índice a menos de 1 cm de la superficie, deslizar en horizontal hace girar la esfera siguiendo el dedo. Al soltar, sigue girando con inercia, se frena y se acomoda en la categoría más cercana.
- Mando: el stick derecho, izquierda o derecha, gira una categoría por movimiento. Actúa con |x| > 0,7 y se rearma bajo 0,3.
- En cada categoría que pasa por el frente: un clic suave y, con mando, una vibración corta.

**Elegir**
- La categoría de frente se ilumina.
- Mano: tocarla con el índice derecho, como un botón.
- Mando: gatillo derecho.

**Segundo nivel: otro anillo**
- Al elegir, la esfera gira hacia arriba en 0,3 s. El anillo de categorías queda arriba, atenuado, y aparece un segundo anillo con las opciones, que se gira y se elige igual.
- Para volver: la casilla «Volver» del anillo, o B/Y con mando.

**Mientras el menú está abierto**, los controles del libro quedan en pausa (A/X, B/Y, pellizcos y stick izquierdo), salvo agarrar el libro.

## Contenido de la primera versión

| Categoría | En la casilla | Opciones del segundo anillo |
| --- | --- | --- |
| Biblioteca | Ícono de libros | Libro abierto ahora y «Próximamente: tu biblioteca» (llega en el paso 3 de la Fase 1) |
| Entornos | Ícono de casa | «Passthrough», «Casa moderna cálida» (entorno 1) y, si hay un entorno activo, sus lugares: sofá, biblioteca, dormitorio, balcón y terraza. Los otros 19 entornos se agregan como casillas nuevas |
| Ajustes | Engranaje | Nitidez 1,4 · 1,5 · 1,6 · 1,7 · 1,8, con el valor actual marcado, y «Estilo de menú: Esfera / Simple». Más adelante: tamaño del libro, mano dominante y sonido |
| Importar y nubes | Nube | «Próximamente» (Fase 3: archivos del visor, Google Drive, Dropbox y MEGA) |
| Hora | **Hora actual (HH:MM) y batería**, actualizadas cada minuto | Temporizador de lectura: 15, 30, 45 o 60 min, y «Apagar». Al terminar, un aviso suave, sin cerrar el libro |

«Hora» es una interpretación de Claude: el usuario la agregó como categoría. Confirmar con él si quiere algo distinto, como un reloj grande en el entorno o una alarma.

## Aspecto

- Esfera azul translúcida con un borde más claro, como un brillo. Se logra con una textura radial generada por código, sin shaders externos.
- Casillas como paneles curvos, cada una con un ícono dibujado por código y su nombre en `TextMesh` (fuente `LegacyRuntime.ttf`). Más adelante se puede cambiar a TextMeshPro con Noto.
- Todo hecho en código, como los entornos: sin modelos, texturas ni paquetes externos.
- Materiales clonados de los del libro (URP Unlit), igual que en los entornos, para que los shaders estén incluidos en el APK.

## Rendimiento

10 draw calls o menos, texturas de 256 px o menos y nada de reconstruir mallas en cada cuadro: solo se mueven y giran transformaciones.

## Integración técnica

- Las opciones salen de un modelo compartido con el [menú simple](MENU_SIMPLE.md) (`MenuModel`). Esta esfera es una de las dos vistas, `SphereMenuView`, y `MenuController` decide cuál se muestra.
- Código nuevo en `hito1/Assets/FolioXR/Scripts/Menu/`, con el namespace `FolioXR.Menu`. Se crea solo con `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`, sin tocar la escena.
- Interfaces públicas que hay que agregar:
  - `FolioBook`: `float RenderScale { get; }`, `void SetRenderScale(float value)` (guarda en PlayerPrefs y muestra el aviso) y `bool InputPaused { get; set; }`.
  - `FolioEnvironmentController`: entrar o salir de un entorno, ir a un lugar, lista de entornos y lugares, y pausar su lectura de controles.
- Manos: `OVRHand` y `OVRSkeleton` para la palma y la punta del índice derecho, o los huesos de `OVRSkeleton` (`Hand_IndexTip`). Mandos: `UnityEngine.XR.InputDevices`, como el resto del proyecto.
- Vibración del mando: `OVRInput.SetControllerVibration` (o `InputDevice.SendHapticImpulse`).

## Orden

1. Terminar y revisar el entorno de prueba (rama `entornos/prueba-1`, hecho por Codex). Quedó sin subir porque se acabó el cupo de Codex; se reanuda cuando se restablezca.
2. Programar el modelo compartido y el [menú simple](MENU_SIMPLE.md) (Codex), revisarlo (Claude) y probarlo (usuario).
3. Programar esta esfera sobre el mismo modelo (Codex), revisarla (Claude) y probarla en el Quest 3 (usuario).

## Estado

Escrito por Claude el 11 de noviembre de 2026, a pedido del usuario («continua tu»), porque Codex agotó su cupo antes de subir nada. Compila con Roslyn (C# 9) contra los stubs de Unity 6000.3 y Meta XR 207. Falta probarlo en el Quest 3.

**Qué hay**
- `Menu/SphereMenuView.cs` es la segunda vista del mismo `MenuModel`. `MenuController` crea las dos y usa la que diga `folioxr.menuStyle`. La esfera es el estilo por defecto.
- En Ajustes, «Menú: Esfera» y «Menú: Simple» cambian de vista al instante, incluso con el menú abierto.
- Antes de existir la esfera, la app guardaba «simple» a la fuerza. Por eso solo se respeta «simple» si el usuario lo eligió (`folioxr.menuStyleChosen`).
- La esfera muestra todas las opciones del modelo sin listas propias: lo que se agregue al modelo aparece solo.

**Cómo se usa**
- **Abrir y cerrar:** botón ☰ izquierdo, o cerrar y abrir la palma izquierda hacia arriba. La esfera aparece 13 cm sobre el mando o la palma izquierda y los sigue. Si la mano sale de vista, queda donde estaba.
- **Girar con la mano:** deslizar el índice derecho sobre la esfera. Al soltar sigue con inercia, se frena y se acomoda en la casilla más cercana.
- **Elegir con la mano:** tocar la casilla del frente (la del marco claro) y retirar el dedo sin deslizar, como en una pantalla táctil. Tocar una casilla lateral la trae al frente.
- **Con mandos:** el stick de cualquier mando sin herramienta gira una casilla por movimiento; el gatillo elige la del frente; B/Y vuelve a las categorías o, si ya está en ellas, cierra.
- **Segundo nivel:** al elegir una categoría, toda la esfera gira 52° hacia arriba en 0,3 s. Las categorías quedan arriba, atenuadas, y el anillo de opciones sube al ecuador. «Volver» queda justo a la izquierda de la primera opción. Tocar una categoría atenuada también vuelve.
- **Clic** al pasar cada casilla por el frente y al elegir; con mando, además, una vibración corta.
- Mientras el menú está abierto se oculta el reloj de herramientas de la muñeca.

**Cómo está hecho**
- **6 draw calls:** cuerpo, casillas y textos de cada anillo, y el marco del frente (más el aviso del temporizador cuando aparece). Texturas de 128 px y 8×1.
- **Textos propios:** los de cada anillo son una sola malla hecha con los glifos de la fuente integrada, curvada sobre la esfera. Se ajustan a su casilla en una o dos líneas y se achican lo necesario: siempre caben.
- **Cuerpo:** esfera azul con una textura radial que siempre mira al lector (centro translúcido, borde claro). Escribe profundidad, así las casillas de la cara de atrás no se ven a través de ella.
- **Anillos largos:** con más de cinco casillas (Ajustes tiene 16 con «Volver») solo existe la ventana de cinco alrededor del frente. Se rehace cuando cambia la casilla del frente; las que entran y salen lo hacen por detrás.
- **Mallas:** se rehacen solo cuando cambia su contenido (opciones, marcas, minuto del reloj, ventana). Al girar solo se mueven transformaciones.

**Diferencias con el diseño**
- Los textos miden entre 0,7 y 1,1 cm según su largo, no 1 cm como mínimo: se priorizó que siempre quepan.
- El centro de la esfera queda 13 cm sobre la mano, no 10, para que los dedos no la toquen al abrir la palma.
- El stick izquierdo también gira la esfera, además del derecho.

**Riesgos**
- Hay que calibrar en el visor las distancias de toque: zona de 8 mm fuera a 3,5 cm dentro de la superficie, y 7° de deslizamiento para empezar a girar.
- Al escribir profundidad, la esfera tapa los objetos transparentes que queden justo detrás (el aviso del libro, el resaltador).
- El texto usa el atlas dinámico de la fuente. Si Unity lo reconstruye, los textos se rehacen en el cuadro siguiente.

