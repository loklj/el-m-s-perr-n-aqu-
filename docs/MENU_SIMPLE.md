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
