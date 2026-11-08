# Menú simple — alternativa al menú esférico

Fecha: 7 de noviembre de 2026. El usuario lo pidió así: «crea un menú más simple para las personas que no les guste el menú esfera». Este documento es la especificación para que Codex (GPT-6 Astra) lo programe y Claude lo revise.

## Idea

Es un panel plano, como una ventana flotante, con las mismas opciones que la esfera. No hay que girar nada ni hay anillos: todo queda a la vista y basta con tocar.

## Abrir y cerrar

- Se abre y se cierra igual que la esfera. Con mando, se usa el botón ☰ izquierdo. Con las manos, se cierra y se abre la mano izquierda con la palma hacia arriba (detalles en [MENU_ESFERICO.md](MENU_ESFERICO.md)).
- También se cierra con la ✕ del panel o con B/Y.
- Aparece frente a la persona, donde está mirando, y queda fijo en el aire; no sigue a la mano.
  - Distancia: 45 cm de la cabeza, para alcanzarlo con el dedo.
  - Altura: 10 cm bajo la línea de los ojos.
  - Inclinación: 15° hacia la persona.
- Si la persona se aleja más de 1,5 m o deja de verlo, al abrirlo de nuevo vuelve a aparecer frente a ella.
- Al aparecer y desaparecer, el panel se funde en 0,15 s.

## Distribución

El panel mide 40 × 28 cm.

- **Barra superior:** hora (HH:MM) y batería a la izquierda, ✕ a la derecha.
- **Columna izquierda:** las 5 categorías como botones grandes (Biblioteca, Entornos, Ajustes, Importar y nubes, Hora). La categoría elegida va resaltada.
- **Zona derecha:** las opciones de la categoría elegida, con el mismo contenido que el segundo anillo de la esfera. La nitidez se muestra como 5 botones (1,4 · 1,5 · 1,6 · 1,7 · 1,8), con el valor actual marcado.
- Al abrirse, el panel muestra la última categoría usada.
- Tamaños mínimos:
  - Botones: 4 cm de alto, para el dedo.
  - Textos de categoría: 1,2 cm.
  - Textos de opciones: 1 cm.

## Uso

- **Con las manos:** se toca con la punta de cualquier índice, como una pantalla táctil.
- **Con mandos:** sale un rayo del mando que apunta al panel; se elige con el gatillo de ese mismo mando.
- **Respuesta al tocar:** el botón se ilumina al pasar sobre él y se hunde un poco al tocarlo. Suena un clic y, con mando, hay una vibración corta.
- Mientras el panel está abierto, los controles del libro quedan en pausa, igual que con la esfera.

## Elegir entre esfera y simple

- Las opciones de Ajustes, en los dos menús, incluyen **«Estilo de menú: Esfera / Simple»**.
- El cambio es inmediato y se guarda en PlayerPrefs con la clave `folioxr.menuStyle`.
- Por defecto se usa la esfera.

## Aspecto

- Panel gris oscuro translúcido (alfa 0,9) con esquinas redondeadas y texto blanco.
- La selección usa el mismo azul de la esfera.
- Todo se hace en código, sin modelos, texturas ni paquetes externos. Los materiales se clonan de los del libro (URP Unlit).

## Rendimiento

- Máximo 8 draw calls.
- Los textos se rehacen solo al cambiar de categoría, nunca en cada cuadro.

## Integración técnica

- **Modelo compartido:** `FolioXR.Menu.MenuModel` describe categorías, opciones y acciones (por ejemplo, `FolioBook.SetRenderScale` o entrar a un entorno). La esfera (`SphereMenuView`) y el panel (`PanelMenuView`) leen el mismo modelo, así que una opción nueva aparece en los dos.
- **Control:** `MenuController` se encarga de abrir y cerrar (☰ y gesto), del estilo elegido, de pausar los controles del libro y de mostrar la vista activa.
- **Ubicación:** el código va en `hito1/Assets/FolioXR/Scripts/Menu/` y se crea solo con `RuntimeInitializeOnLoadMethod`, sin tocar la escena.

## Orden

1. Entorno de prueba: espera que se restablezca el cupo de Codex.
2. Modelo compartido y menú simple (Codex). Es la parte más fácil y sirve para probar en el visor las acciones de nitidez y entornos.
3. Menú esférico sobre el mismo modelo (Codex).
4. Después de cada paso, Claude revisa y el usuario prueba en el Quest 3.

## Estado

8 de noviembre de 2026 — implementado en código en `menus/simple-1`, pendiente de compilar y probar en Quest 3. No se inicia el Hito 2 ni se implementa la esfera.

- `MenuModel` contiene las cinco categorías y opciones con texto, disponibilidad, marca y acción. `IMenuView` separa la presentación del modelo. `MenuController` se crea después de cargar la escena, espera al libro y al controlador de entornos y conecta `PanelMenuView` sin editar la escena.
- En esta entrega se usa **siempre Simple** y se guarda `folioxr.menuStyle = "simple"`, incluso si había otra preferencia. «Esfera (próximamente)» está deshabilitado. Esto sustituye provisionalmente el valor por defecto de la sección «Elegir entre esfera y simple».
- Panel de 40 × 28 cm, a 45 cm hacia delante y 10 cm bajo los ojos, inclinado 15°, fijo en el mundo. Al volver a abrirlo se recoloca si está a más de 1,5 m, detrás de la persona o fuera de la vista. Fundido de 0,15 s. La última categoría se conserva durante la sesión.
- Cinco botones de categoría de 4 cm de alto. Las opciones se distribuyen en dos columnas de hasta cuatro filas para que también midan 4 cm; Biblioteca e Importar usan todo el ancho disponible. Las alturas tipográficas se calculan con las métricas de `LegacyRuntime.ttf`: 1,2 cm para categorías, 1 cm para opciones y 1,8 cm para el reloj grande.
- Toque con cualquiera de los índices: cruce del plano desde delante, con rearme al retirar el dedo 18 mm. Mandos: intersección analítica desde `leftControllerAnchor`/`rightControllerAnchor` y flanco del gatillo de ese mismo mando. Un clic por cuadro como máximo, hover azul, hundimiento del fondo del botón, clic sintetizado y vibración de 35 ms cuando el dispositivo admite impulsos. No se crean colliders ni se usa `Physics.Raycast`.
- Gesto izquierdo: normal `índice × meñique`, ángulo menor de 45°, abierta (>8 cm) → puño (<5,5 cm durante 0,15 s continuos) → abierta antes de 1,2 s desde el inicio del puño; espera de 0,8 s. Solo acepta seguimiento fiable sin gesto del sistema. Core 207 admite esqueletos `Hand_*` y `XRHand_*`: se resuelven ambos por sus identificadores, sin asumir índices en la lista de huesos. El log muestra el ángulo una vez por segundo y cada apertura/cierre.
- La pausa consume A/X, B/Y, pellizcos, stick izquierdo y clic del stick derecho; el agarre sigue disponible. También pausa los atajos del entorno. Al cerrar, mantiene la pausa hasta observar dos cuadros con botones sueltos, stick izquierdo centrado y sin pellizcos. Mientras la palma izquierda está arriba, el pellizco izquierdo queda suprimido también con el menú cerrado.
- `SetRenderScale` reutiliza el camino del stick: clamp/redondeo 1,4–1,8, PlayerPrefs, aplicación de calidad y aviso. Las acciones de entorno se procesan en `LateUpdate`, como los controles existentes; conservan la restauración de passthrough, rig, Guardian y luz ambiente. Tras un salto de entorno/lugar el panel sigue en su posición mundial: cerrar y volver a abrir lo trae al nuevo punto.
- Hora y batería se actualizan por minuto; batería desconocida aparece como «—». El temporizador es único, de 15/30/45/60 minutos; elegir otra duración lo reinicia y «Apagar» lo cancela. Usa tiempo real monotónico, independiente de `timeScale`; no se guarda al cerrar la app. Al vencer suena un tono suave y aparece un aviso durante cinco segundos, aunque el menú esté cerrado, sin alterar el libro.
- Presupuesto propio del menú: **ocho renderizadores de un pase como máximo**, incluidos dos rayos en una sola malla y el aviso. Panel/botones comparten una malla y una paleta generada de 8 × 1 píxeles; cada columna usa un `TextMesh`. Todos los materiales se clonan de la página URP Unlit; los de texto usan el atlas de la fuente y atienden `Font.textureRebuilt`. No se usa `Shader.Find`, paquetes adicionales ni variantes nuevas del shader. Se deshabilitan pases de profundidad, sombras y vectores de movimiento. El número real de draw calls debe medirse en el Frame Debugger/visor; no incluye libro, casa ni manos.
- No se reconstruyen etiquetas al hacer hover, marcar una opción, abrir/cerrar o en cada cuadro. Las opciones se rehacen al cambiar de categoría o al aparecer/desaparecer los lugares de la casa; reloj/batería solo al cambiar de minuto y el aviso al vencer el temporizador.

### Revisión y prueba pendiente

Revisión manual contra las fuentes oficiales de **Meta XR Core 207.0.0** (registro de paquetes de Meta) y **URP de Unity 6000.3** (`Unlit.shader`, `UnlitForwardPass.hlsl` y `ShaderVariablesFunctions.hlsl`): nombres de huesos, confianza/seguimiento, anchors, materiales y alfa. En URP 17 `OutputAlpha` consulta `_Surface` en tiempo de ejecución; se configura el blend sin solicitar una variante transparente ausente en los materiales opacos originales. Revisar en el APK también la transparencia si se añaden SSAO o decals al renderer.

Aquí no hay compilación de Unity ni prueba en Quest. Antes de darlo por validado, ejecutar las filas de menú del Paso 10 de [HITO1_GUIA.md](HITO1_GUIA.md), comprobar el signo del ángulo de la palma y las tolerancias con ambas manos, tamaño/alineación de textos, recaptura tras perder seguimiento, oclusión con el libro, sonido/hápticos, pausa al cerrar con B/Y y presupuesto de renderizado. Probar también entrar/salir de la casa y sus cinco lugares, persistencia de nitidez/estilo y un temporizador completo con el menú cerrado.
