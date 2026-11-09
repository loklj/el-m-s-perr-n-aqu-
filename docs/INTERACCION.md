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
- **Escala:** escala inicial × (distancia actual entre las manos / distancia inicial), entre **0,5× y 3×**.
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
