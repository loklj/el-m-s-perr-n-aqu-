# Reglas comunes para Codex (GPT-6 Astra) en Folio XR

Cada prompt de [PROMPTS_CODEX.md](PROMPTS_CODEX.md) pide leer este archivo. Codex debe cumplir todas estas reglas sin excepción.

## Proyecto y forma de trabajo

- Responde y comenta el código en español.
- Folio XR es un lector de libros 3D para Meta Quest 3 hecho con Unity 6000.3 LTS, URP 17 y Meta XR SDK 207 (OpenXR), en Android ARM64. Se venderá en la Meta Horizon Store a USD 4,99, con código cerrado.
- Codex programa, Claude revisa y corrige antes de integrar, y el usuario prueba en su Quest 3.
- Trabaja siempre en la rama que indique el prompt, partiendo de `origin/fase-1/preparacion`.
  - Nunca hagas push a `main` ni a `fase-1/preparacion`.
  - Si `git push` no tiene credenciales, usa el conector de GitHub.
- No edites `PROGRESO.md`: lo actualiza Claude.
- Al terminar, responde con un resumen corto, la lista de archivos y los riesgos.

## Legal (obligatorio)

- **Nunca** quites ni esquives DRM. Solo se abren archivos sin DRM. Si un archivo tiene DRM, se informa con un mensaje claro.
- No copies código, nombre, logo, textos, assets ni capturas de Livro ni de otras apps.
- **Licencias permitidas:** solo BSD, MIT, Apache-2.0 o componentes del sistema.
  - **Nada GPL, LGPL ni AGPL**: la licencia del SDK de Meta lo impide.
  - Cada dependencia nueva se anota en el inventario de `docs/LICENCIAS_Y_TIENDA.md`, con versión, licencia y origen.
- Nada de claves, tokens ni credenciales en el repositorio.
  - Los IDs de cliente OAuth van en un archivo de configuración local ignorado por git, con una plantilla `.example` en el repo.

## Unity y código

- Usa solo APIs que existan en Unity 6000.3, URP 17 y Meta XR Core SDK 207. Aquí no puedes compilar Unity, así que revisa a mano los `using`, los tipos y los nombres exactos.
- **Assets por código.** Siempre que se pueda, todo se genera en código: sin modelos ni texturas externas sin licencia documentada, y sin `Shader.Find`.
  - Los materiales se clonan de los del libro: la tapa usa URP Lit y las páginas URP Unlit.
  - Para transparencia, usa la misma configuración que `PanelMenuView.Clone` o `LocomotionVisuals.TransparentClone`.
- **Scripts nuevos.** Van en `hito1/Assets/FolioXR/Scripts/<Área>/`, con namespace `FolioXR.<Área>`.
  - Se crean solos con `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` si no existen, salvo que el prompt diga otra cosa.
- **Serialización.** No pongas `#if UNITY_EDITOR` alrededor de campos serializados y no marques clases anidadas como `[Serializable]` sin necesidad. Unity rechaza el build si el formato de las clases difiere entre el editor y el visor.
- **Código Android.** Va en `hito1/Assets/Plugins/Android/`, igual que `FolioPdfRenderer.java`.
- **Registros.** Llevan el prefijo `[FolioXR]`. Registra los eventos útiles para diagnosticar en el visor sin llenar el registro: nada por cuadro.

## Rendimiento (Quest 3)

- La app debe mantener **72 FPS**; hoy da 72 FPS estables en la casa con nitidez 1,8.
- Nada de reservar memoria ni reconstruir mallas en cada cuadro.
- Los trabajos pesados (render de páginas, lectura de archivos, red) van fuera del hilo principal o repartidos entre cuadros, y se pueden cancelar.
- **Memoria acotada:** cachés LRU con límite. Recordar que hay archivos de hasta 20 GB y PDF de más de 100.000 páginas.

## No romper lo que ya funciona

Verifica que siga funcionando todo esto:

- Pasar página con A/X y B/Y y con pellizcos lejos del libro.
- Agarre del libro (pellizco, puño y grip), física Realista/Flotante y tamaño con dos manos (0,5× a 5×).
- Menú simple (☰ o gesto de la palma), nitidez, casa con sus lugares, movimiento libre y teletransporte.
- Reloj de herramientas y las herramientas ya integradas.

## Documentación

- Agrega filas a la tabla del Paso 10 de `docs/HITO1_GUIA.md` por cada control o prueba nuevos.
- Agrega una sección «Estado» al documento del área, o crea uno nuevo en `docs/` si el prompt lo pide.
