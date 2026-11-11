# Folio XR — progreso y plan

Última actualización: 7 de noviembre de 2026 (Hito 1.2: nitidez ajustable). Nombre de trabajo propio y provisional; no se ha comprobado su disponibilidad comercial. Repositorio: `loklj/el-m-s-perr-n-aqu-`.

## Punto de reanudación

**Fase 0 completada como entrega documental: arquitectura y planificación.**

**Fase 1 en curso — Hito 1 terminado y probado en Quest 3 por el usuario.** La base confirmada está en `fase-1/preparacion`; el usuario también confirmó que la nitidez del Hito 1.1 mejoró con renderScale 1,4 y MSAA 4×. El Hito 1.2 (escrito por Codex/GPT-6 Astra, revisado y unido por Claude a `fase-1/preparacion`) añade el ajuste de nitidez dentro de la app y queda pendiente de su prueba específica en el visor. Aquí solo se revisa el código a mano: no hay Unity ni visor. Ver [la guía del Hito 1](docs/HITO1_GUIA.md). **El Hito 2 no está iniciado ni autorizado.**

El repositorio partía de un README. Esta fase añade documentación de diseño, evaluación de dependencias, riesgos, estructura propuesta y procedimientos de validación. Las instrucciones de compilación e instalación describen trabajo futuro en el PC; no acreditan pruebas realizadas.

## Documentos

- [Arquitectura y estructura propuesta](docs/ARQUITECTURA.md).
- [Formatos, bibliotecas y licencias](docs/FORMATOS_Y_LICENCIAS.md).
- [Menú esférico 3D: diseño acordado](docs/MENU_ESFERICO.md).
- [Menú simple (alternativa a la esfera)](docs/MENU_SIMPLE.md).
- [Entorno de prueba 1: casa moderna cálida](docs/ENTORNOS.md).
- [Interacción: física del libro, movimiento libre y herramientas de muñeca](docs/INTERACCION.md).
- [Prompts para terminar la app con Codex](docs/PROMPTS_CODEX.md) y [reglas comunes para Codex](docs/REGLAS_CODEX.md).
- [Licencias para vender la app en la Meta Horizon Store](docs/LICENCIAS_Y_TIENDA.md): Unity, SDK de Meta, comisión e inventario de componentes (7 de noviembre de 2026).
- [Importación local, nubes, OPDS y guía Kindle](docs/IMPORTACION_Y_NUBES.md).
- [Preparación del PC, compilación, instalación y pruebas](docs/PC_Y_VALIDACION.md).
- [Hito 1: guía paso a paso para Pop!_OS y Quest 3](docs/HITO1_GUIA.md).
- [Hito 2: enfoque propuesto para páginas EPUB en 3D](docs/HITO2_ENFOQUE.md) — pendiente de aprobación (incluye una decisión de licencia).
- [Muestras pequeñas](tests/fixtures/LEEME.md) y [generadores de archivos gigantes](tools/test-data/LEEME.md).

## Objetivo del producto

Lector personal para Meta Quest 3: libros, cómics y manga como objetos 3D agarrables, movibles y con páginas que se pueden pasar. Tres libros abiertos simultáneamente como objetivo base; ampliar esa cantidad solo después de medir. Lectura sentado, de pie o acostado, controles Touch y manos, VR inmersiva y passthrough. La biblioteca y el progreso deben sobrevivir a cierres, reinicios y actualizaciones compatibles.

**Tamaño de archivo (requisito añadido por el usuario el 5 de noviembre de 2026):** debe abrir archivos de **hasta 5 GB**, además de documentos de más de 100.000 páginas. El usuario tiene en el visor **PDF reales de hasta 20 GB** para las pruebas, así que el diseño apunta a 20 GB o más. Ver la sección [Archivos de varios GB](#archivos-de-varios-gb-5-gb-requeridos-pruebas-hasta-20-gb).

Se toman requisitos funcionales como referencia, sin copiar código, marca, logo, interfaz gráfica ni assets de Livro. Todos los recursos deberán ser originales o tener una licencia documentada.

## Decisiones registradas

| Tema | Decisión | Estado |
| --- | --- | --- |
| Experiencia 3D | Unity LTS + Meta XR Interaction/Passthrough; comparar con Meta Spatial SDK en la arquitectura | Selección de arquitectura; versiones exactas pendientes de compatibilidad |
| Motor de lectura | Módulo Android Kotlin, Room/SQLite y C/C++ vía NDK cuando corresponda; interfaz asíncrona con Unity | Diseño |
| Persistencia | Copia privada duradera del archivo, importaciones transaccionales y recuperación tras interrupciones | Obligatorio desde Fase 1 |
| Formatos | Identificar contenido y estructura; la extensión solo orienta la exploración | Obligatorio desde Fase 1 |
| DRM | Nunca quitar ni eludir protección; no incorporar descifradores de DRM | Decisión del usuario |
| Diagnóstico | Distinguir protección confirmada, contraseña PDF, formato pendiente, daño, descarga incompleta y causa desconocida | Decisión del usuario |
| Adobe/Kindle | Detectar evidencias de DRM de Adobe en EPUB/PDF y de Kindle en contenedores KFX/AZW; no atribuir DRM solo al vendedor o sufijo | Diseño con pruebas por proveedor pendientes |
| Marcas de agua | No bloquean la apertura por sí solas; no intentar borrarlas | Decisión del usuario |
| PDF protegido con contraseña | Solicitar la contraseña; utilizar el mecanismo normal del lector; no intentar adivinarla ni guardarla por defecto | Diseño |
| Readium LCP | Fuera del alcance actual | Decisión del usuario |
| Kindle sin DRM | Guía breve en la app para descargar EPUB/PDF desde Amazon cuando la opción esté disponible para ese título | Contenido planificado; acceso a documentación Amazon pendiente |
| KFX sin DRM | No prometer renderizado; no hay backend seleccionado y validado. Ofrecer EPUB/PDF legítimo cuando exista | Limitación declarada |
| Nubes | Google Drive, Dropbox, MEGA y OPDS en Fase 3; clientes independientes detrás de contratos propios | Diseño; OAuth Drive en Quest es una investigación bloqueante para ese conector |
| OPDS | Importar publicaciones sin DRM de servidores como Calibre-Web, Kavita y Komga; validar cada versión y autenticación | Decisión del usuario para Fase 3 |
| Lectores externos | Fichas que abran, por ejemplo, Kindle para la web en el navegador | Fase 5, baja prioridad; compatibilidad Quest por verificar |
| Linux | Desarrollo principal en Pop!_OS; distinguir compatibilidad práctica de soporte oficial de distribución | Preparación y alternativas documentadas |
| Pruebas gigantes | Generadores reproducibles, corpus legal y mediciones reales; no generar cientos de MB durante Fase 0 | Fase 4, con límites de memoria desde Fase 1 |

## Mensajes de error requeridos

El detalle técnico exportable acompaña al mensaje, sin incluir contraseñas, tokens, contenido del libro ni datos personales por defecto. “Reintentar” se ofrece cuando puede volver a intentar la operación, sin bucles automáticos infinitos.

| Evidencia | Mensaje y acción |
| --- | --- |
| Marcadores de un DRM no compatible confirmados por el detector | «Este libro tiene una protección DRM que esta app no admite. Consigue una edición sin DRM del editor o de una tienda que la ofrezca». |
| PDF con contraseña de apertura requerida | «Este PDF necesita una contraseña para abrirse». Pedirla; si no es correcta, permitir corregirla o cancelar. |
| Formato reconocido sin lector implementado | «Formato aún no compatible». Identificarlo solo cuando se conozca y ofrecer una alternativa legítima. |
| Daño estructural confirmado | «El archivo parece estar dañado. Prueba a volver a descargarlo». |
| Longitud, hash publicado o transferencia que acredita archivo incompleto | «La descarga está incompleta. Reanuda o vuelve a descargar el archivo». |
| No hay evidencia suficiente | «No se pudo abrir el archivo y no conocemos la causa». Reintentar y exportar diagnóstico. |
| Solo marca de agua | Abrir normalmente si el formato es compatible. La marca no se clasifica como bloqueo DRM. |

Un EPUB con `encryption.xml` puede contener ofuscación de fuentes legítima; ese archivo por sí solo no prueba DRM. Un PDF cifrado puede tener contraseña vacía de apertura y restricciones de uso; no pedir una contraseña innecesaria ni eliminar restricciones. Un archivo ACSM es un comprobante de adquisición, no el libro EPUB/PDF. Ver los matices en el documento de formatos.

## Fases y criterios de salida

### Fase 0 — arquitectura, bibliotecas, estructura, riesgos y plan

Entregables de documentación en este repositorio. Evaluación con fuentes primarias accesibles; lo bloqueado o no comprobado se señala. No se necesitan credenciales, un APK ni cambios de configuración cloud para esta fase.

- [x] Revisar el repositorio y los requisitos completos del adjunto y sus ampliaciones.
- [x] Proponer arquitectura Unity/Android y compararla con Meta Spatial SDK.
- [x] Definir importación duradera, modelo de biblioteca y contratos de trabajo asíncrono.
- [x] Evaluar todos los formatos solicitados, dependencias, licencias y límites Android.
- [x] Registrar DRM, OPDS, guía Kindle y lector externo en las fases correspondientes.
- [x] Diseñar memoria limitada, paginación persistente y plan de pruebas gigantes.
- [x] Documentar preparación, compilación, instalación y validación en PC/Quest.
- [x] Documentar riesgos y comprobaciones pendientes sin presentarlas como resultados.

### Fase 1 — MVP local y biblioteca persistente (en curso, por hitos)

1. Fijar versiones compatibles de Unity LTS, Meta XR, paquetes XR, JDK/SDK/NDK/Gradle y bibliotecas. Guardar versiones y lockfiles; compilar un proyecto mínimo Android ARM64 con IL2CPP en Pop!_OS y probar VR, passthrough, Touch y manos en Quest 3.
2. Resolver el prototipo de página EPUB en una textura 3D legible. Evaluar Readium en Android y su integración gráfica antes de construir el lector completo; una vista Android plana no cumple el requisito del libro 3D.
3. Crear biblioteca Room, importación local con SAF, copia privada, estados de descarga/importación, recuperación, progreso, marcadores y respaldo versionado básico. Comprobar reinicio y actualización de APK conservando datos. Si SAF no funciona en Horizon OS, adelantar la carga por Wi-Fi desde el PC como alternativa local; de lo contrario, queda en Fase 3.
4. PDF con PDFium, CBZ/ZIP con imágenes y EPUB sin DRM; portada, apertura cancelable, errores específicos, navegación básica y cachés acotadas. No dejar las pruebas de memoria para el final.
5. Un libro manipulable con geometría propia y animación de páginas; después tres libros simultáneos con un presupuesto compartido. Añadir manga derecha a izquierda y doble página en el lector de cómics básico.
6. Validar las posturas de lectura y la estabilidad del libro al recentrar, reanudar o perder seguimiento.

Salida: APK reproducible y una sesión real con los tres formatos, tres libros, biblioteca duradera, recuperación de un fallo e instrucciones actualizadas con las versiones efectivamente usadas. Sin esta evidencia no marcar el MVP como completado.

#### Hito 1 — libro 3D con un PDF (terminado y probado por el usuario)

Alcance acordado con el usuario: proyecto mínimo Unity + Android que muestre páginas de un PDF de prueba propio en un libro 3D simple, con VR/passthrough, Touch y manos, más instrucciones exactas para compilar en Pop!_OS e instalar con adb.

Decisiones tomadas para este hito (revisables):

| Tema | Decisión del Hito 1 | Motivo |
| --- | --- | --- |
| Editor | Unity 6.3 LTS (soporte hasta diciembre de 2027), plantilla Universal 3D | LTS vigente; Meta exige 6000.0.66f2 o superior y recomienda 6.1+ |
| XR | Meta XR All-in-One SDK + Unity OpenXR Plugin + Building Blocks (Camera Rig, Passthrough, Controller/Hand Tracking) + Grab Quick Action | Camino oficial de Meta; un principiante puede configurarlo sin escribir YAML de escenas |
| PDF | `android.graphics.pdf.PdfRenderer` en un hilo propio, página a PNG en caché y `Texture2D.LoadImage` | Evita compilar PDFium/NDK solo para validar la cadena; PDFium sigue planificado para el lector completo |
| Módulo Android | Clase Java en `Assets/Plugins/Android` (Unity la compila) | Más simple que un AAR Kotlin para el primer APK; migrar a `android/reader` cuando se integre Room |
| Proyecto Unity | Lo crea el usuario con Unity Hub en `unity/`; los archivos del hito están en `hito1/Assets` y se copian | Unity Hub no crea proyectos en carpetas que ya tienen contenido |
| Paquete | `com.loklj.folioxr`, nombre «Folio XR» | Lo fija el menú del Editor |
| Controles | A/X siguiente doble página, B/Y anterior, clic del stick derecho recoloca el libro; agarre con manos y mandos vía Interaction SDK; stick izquierdo arriba/abajo ajusta la nitidez (Hito 1.2) | Hito 1.1 añade pellizcos para pasar página; su prueba específica con manos sigue pendiente |
| PDF propio | `adb push` a `/sdcard/Android/data/com.loklj.folioxr/files/libro.pdf` | Evita permisos de almacenamiento antes de tener SAF |
| Persistencia | Solo la nitidez con PlayerPrefs (`folioxr.renderScale`, Hito 1.2) | Room, biblioteca y progreso de lectura llegan en el paso 3 de esta fase |

Limitaciones conocidas: sin PDF con contraseña (PdfRenderer no los abre en esta versión), un solo libro, PNG intermedio que añade latencia al pasar página (aceptable para validar; se optimiza en Fase 4), sin medición de memoria todavía.

Implementación y verificaciones del usuario (las pruebas detalladas sin confirmación específica siguen pendientes):

- [x] Unity 6.3 LTS instala y abre en Pop!_OS (7 de noviembre de 2026): **Unity Hub 3.22.5, Editor 6000.3.28f1, Meta XR All-in-One SDK 207, NDK r27c, OpenJDK 17.0.18, SDK build-tools 36.0.0**. Editor, proyecto y cachés en el disco `/media/dimitrov/almacenamiento` (el disco del sistema tiene 75 GB y estaba casi lleno).
- [x] El menú Folio XR aparece y «Comprobar Hito 1» da todo OK (Meta Quest activo, PDF, plugin Java, libro, cámara del Camera Rig, paquete `com.loklj.folioxr`).
- [x] El APK compila (7 de noviembre de 2026). Primer intento fallido: el NDK de Unity Hub quedó sin `clang++`; se reemplazó por el NDK r27c oficial de Google.
- [x] El APK se instala con adb y abre en el Quest 3.
- [x] Se ve el passthrough y el libro con páginas reales del PDF (captura del usuario: páginas 3-4, viñetas y letra grande).
- [x] A/X y B/Y cambian de doble página; el texto inferior coincide.
- [x] Calidad de imagen del Hito 1.1: el usuario confirmó que mejoró en Quest 3. RenderScale 1,4 y MSAA 4× al iniciar, páginas a 1800 px, filtrado anisotrópico 8× y sesgo de mipmap −0,5; esto no acredita todavía la prueba de letra de 10–12 puntos a ~45 cm.
- [x] Hito 1.2 implementado: nitidez ajustable de 1,4 a 1,8 con el stick izquierdo, pasos de 0,1 por movimiento, guardado en PlayerPrefs y aviso temporal. **Pendiente de prueba específica en Quest 3.**
- [ ] Manos: el seguimiento funciona, pero «no se puede hacer nada con ellas». Hito 1.1 añade pellizco derecho/izquierdo lejos del libro para pasar página y registra en el log cuántos interactores de agarre hay (para diagnosticar el agarre con la mano).
- [ ] Se agarra el libro con manos y con mandos; el clic del stick lo recoloca.
- [ ] El texto de 10–12 puntos se lee a ~45 cm.
- [ ] Un PDF propio copiado con adb se abre.

### Fase 2 — ampliar formatos

TXT con detección de codificación y corrección manual, CBR RAR4/RAR5, MOBI/AZW/AZW3 sin DRM, DOCX/ODT/RTF. DOC binario requiere evaluación independiente; no prometer soporte fiel mediante un parser de texto. KFX conserva diagnóstico explícito hasta que exista un lector sin DRM técnicamente viable y revisado. Registrar por separado soportado, experimental y no compatible; no afirmar que todos los formatos ya funcionan.

Salida por formato: corpus válido y erróneo, lectura fiel al alcance declarado, importación persistente, cancelación, límites de recursos y obligaciones de licencia resueltas. Conversión explícita en el PC es una alternativa si el lector nativo no es viable; no cuenta como soporte nativo en Quest.

### Fase 3 — importación remota y OPDS

Google Drive mediante navegador del sistema y OAuth/PKCE únicamente con un tipo de cliente y redirect admitidos y probados; Dropbox PKCE y alternativa manual documentada; SDK oficial MEGA sujeto a licencia. Añadir OPDS con catálogo, paginación, credenciales y enlaces de adquisición compatibles por servidor. Incluir subida PC por Wi-Fi si no se adelantó como importador auxiliar.

Salida: explorar carpetas/catálogos, descargar con progreso, cancelar/reanudar de forma segura, detectar transferencias incompletas y leer offline; probar expiración/revocación de credenciales. No dar el conector Drive por resuelto usando un flujo de TV que no autoriza explorar el Drive completo.

### Fase 4 — archivos gigantes y rendimiento

Implementar un generador con semillas y tamaños configurables para PDF de al menos 100.001 páginas, TXT de cientos de MB y CBZ con miles de imágenes. Guardar manifiesto, tamaños y hashes; generarlos en disco en streaming, con límite de espacio y sin llenar el repositorio. Incluir RAR sólido y EPUB con capítulo enorme como casos de estrés adicionales.

Medir en Quest 3 real: apertura fría/caliente, primera página, cambios consecutivos y saltos aleatorios, caché fría/caliente, búsqueda y paginación cancelables, tres libros, PSS y memoria gráfica, FPS/tiempos de cuadro, energía y temperatura en sesiones prolongadas. Medianas y percentiles p95/p99 con condiciones reproducibles.

Metas iniciales: primera página <3 s desde archivo local disponible y cambio de página precargada <100 ms, sin perder la frecuencia elegida del visor. Medir también fallos de caché sin ocultarlos. Una descarga, una extracción RAR sólida o una paginación inicial no tienen esa latencia garantizada. La cifra de ~5,75 GiB PSS se trata como referencia del requisito, no como límite universal comprobado del sistema.

Salida: generadores, informes reales, límites de memoria aplicados y degradación controlada documentada. No hay mediciones de esta fase todavía.

### Fase 5 — mejoras y pulido

Estantería VR/passthrough, carpetas, renombrar, búsqueda e índice enriquecidos, noche/sepia, fuentes y tamaño, panel 2D clásico, más de tres libros solo si las mediciones lo permiten, accesibilidad y pulido. Fichas para abrir un lector web externo como Kindle para la web: baja prioridad, mediante navegador y sin extraer contenido protegido. Los básicos de búsqueda, índice y marcadores pueden adelantarse cuando un formato los necesite.

## Publicación comercial (decisión del usuario, 5 de noviembre de 2026)

La app se venderá en la **Meta Horizon Store a USD 4,99**, con **código cerrado**. Consecuencias para todo el proyecto:

| Tema | Regla |
| --- | --- |
| Licencias | Solo bibliotecas permisivas (BSD, MIT, Apache-2.0) o del sistema. **Nada AGPL, GPL ni LGPL**: la licencia del SDK de Meta prohíbe usarlo de forma que quede sujeto a una licencia abierta. MuPDF queda fuera y libmobi también (7 de noviembre de 2026): MOBI/AZW/AZW3 se leerán con un parser propio. Unity Personal sirve (menos de USD 200.000 al año; sin pantalla obligatoria en Unity 6). Inventario completo y pantalla de licencias de terceros: ver [docs/LICENCIAS_Y_TIENDA.md](docs/LICENCIAS_Y_TIENDA.md). |
| EPUB | WebView del sistema + Readium (BSD-3), ver `docs/HITO2_ENFOQUE.md`. |
| Google Drive | **Decidido por el usuario (9 de noviembre de 2026): `drive.file` con Google Picker.** Leer los archivos que el usuario ya tiene (`drive.readonly`) es un permiso restringido: exige verificación de Google y una evaluación de seguridad anual (CASA) de terceros. Alternativa sin esa evaluación: permiso `drive.file` con el selector de archivos de Google (*Google Picker*), que solo da acceso a los archivos que el usuario elige. Decidir antes de la Fase 3. |
| Dropbox | Las apps en modo desarrollo tienen un límite de usuarios; pedir aprobación de producción antes de publicar. |
| Tienda | Cuenta de organización de desarrollador en Meta, requisitos técnicos de la tienda (VRC), política de privacidad, clasificación por edades (IARC) e imágenes y textos de la ficha. Meta retiene el 30 % de cada venta. |
| Marca | No usar el nombre, logo, textos ni capturas de Livro, tampoco en la ficha de la tienda. Verificar que el nombre final (hoy «Folio XR», provisional) no choque con marcas existentes. |
| DRM | Igual que antes: solo archivos sin DRM; nunca eludir protecciones. |

## Entornos virtuales (decisión del usuario, 7 de noviembre de 2026)

- **20 entornos**, cada uno una **casa de 2 pisos** con **terraza, biblioteca, balcón, sala de estar y dormitorio**, en estilos distintos.
- **Modelos 3D hechos en código** (por Codex/GPT-6 Astra, revisados por Claude): sin paquetes comprados ni recursos de terceros, así no hay licencias que cumplir.
- **Ahora solo uno de prueba** para medir rendimiento en el Quest 3; los otros 19 después de que el lector de EPUB funcione.
- Forma de trabajo pedida por el usuario: **Codex/GPT-6 Astra escribe el código y Claude lo revisa y corrige** antes de que el usuario compile.
- Estado (8 de noviembre de 2026): **entorno de prueba 1 integrado en `fase-1/preparacion`**. Es una casa «moderna cálida» de dos pisos, hecha por Codex con esfuerzo Alto y revisada por Claude. Claude agregó la actualización de la luz ambiente y el manejo del límite del Guardian. Controles: el clic del stick izquierdo alterna entre passthrough y la casa, y el stick izquierdo a los lados cambia entre 5 lugares para leer. Falta probarlo en el Quest 3. Detalle: [docs/ENTORNOS.md](docs/ENTORNOS.md).
- **Prueba del 9 de noviembre de 2026 (03:25), registro leído por Claude:** los pellizcos funcionan. La nitidez no registró ningún movimiento del stick (falta confirmar si se usaron los mandos). El agarre no puede funcionar porque la escena no tiene interactores del Interaction SDK (`HandGrabInteractor=0, GrabInteractor=0`). Además, el APK era la versión 1.2: la casa y el menú no se habían copiado al proyecto.
- **Tandas D y E escritas por Claude (11 de noviembre de 2026), sin probar todavía:** lápices de 6 colores con goma, tinta invisible revelada por la luz UV, libreta A5 de 20 páginas, lupa con cámara secundaria y regla de 30 cm que guía el lápiz. Los trazos se guardan por página del libro, de la libreta y de la casa. Detalle en [docs/INTERACCION.md](docs/INTERACCION.md) («Estado de la Tanda D/E»). El usuario las prueba junto con A, B y C en el visor.
- **Revisión tras la prueba del 11 de noviembre de 2026 (Claude):** el usuario confirmó que casi todo funciona; fallaban moverse y abrir el menú con las manos. Se relajaron el teletransporte de manos (fuerza de pellizco) y el gesto del menú, y se agregó caminar con el pellizco pulgar + medio izquierdo. El reloj ahora muestra la hora y tiene un botón «Menú». Las herramientas se sacan tocando el ícono con el índice derecho y quedan pegadas a la mano. Hay guantes de gravedad para atraer herramientas y el libro, como en Half-Life: Alyx, y la opción «Velocidad: trotar» (3 m/s).
- **Encargo 5 (biblioteca) escrito por Claude, sin probar:** plugin Java `FolioLibrary` con SQLite del sistema (sin Room, para no agregar dependencias de Gradle), importación con el selector del sistema (SAF), copia privada atómica con diario de recuperación, detección de formato por firma, portada de PDF, progreso, marcadores y respaldo con `VACUUM INTO`. En Unity, `Books/BookLibrary.cs` y la categoría Biblioteca del menú. Al arrancar se reabre el último libro en su página. Si SAF no existe en Horizon OS, se avisa: entonces se adelanta la carga por Wi-Fi.
- **Menú esférico escrito por Claude (11 de noviembre de 2026), sin probar todavía:** esfera azul sobre la mano o el mando izquierdo, con dos anillos (categorías y opciones) sobre el mismo modelo del panel. Es el estilo por defecto; «Menú: Simple» en Ajustes vuelve al panel. Detalle en [docs/MENU_ESFERICO.md](docs/MENU_ESFERICO.md) («Estado»).
- **Codex:** GPT-6 Astra Ultra agotó el cupo en 6 minutos sin subir nada del encargo 4, así que lo escribió Claude. Desde el 10 de noviembre se usa un solo chat de Codex; cuando se renueve el cupo sigue con el siguiente encargo pendiente.
- **Tanda C integrada (9 de noviembre de 2026), sin probar todavía:**
  - Reloj de 6 herramientas en la muñeca izquierda; por ahora solo la linterna funciona.
  - Se toman con la mano derecha (pellizco o puño) o con el grip del mando y se devuelven soltándolas cerca de la muñeca.
  - Linterna triple: normal (luz spot), láser y ultravioleta, con una API para la tinta invisible.
  - «Guardar herramientas» en Ajustes.
  - Mientras se sostiene una herramienta, esa mano no pasa página.
  - Claude agregó el soporte para revelar la tinta sobre superficies con grosor.
  - Desde esta tanda, la compilación de revisión usa Roslyn (.NET 8 SDK, C# 9) en vez de mcs, porque mcs no entiende funciones locales.
- **Tanda B integrada (9 de noviembre de 2026), sin probar todavía:** movimiento libre dentro de la casa.
  - Stick izquierdo para caminar y stick derecho para girar 30° (o giro suave, en Ajustes).
  - Teletransporte con el stick derecho hacia adelante, o con pellizco del dedo medio de la mano derecha.
  - CharacterController con gravedad, escalones y paredes.
  - Viñeta configurable en Ajustes.
  - Dentro de la casa, el stick izquierdo ya no ajusta la nitidez.
  - La hizo Codex; Claude cambió la viñeta de posprocesado por una malla, que es más liviana en Quest.
  - El usuario quiere probar todas las tandas juntas al final.
- **Tanda A integrada (9 de noviembre de 2026):** letras del menú que caben en los botones, agarre con manos (pellizco o puño) con diagnóstico, física realista (BoxCollider en la casa y piso invisible en passthrough), tamaño con dos manos (0,5× a 5×; el usuario subió el máximo de 3× a 5× después de probarlo) y en Ajustes «Física», «Tamaño normal» y «Traer libro». La hizo Codex (esfuerzo Alto) y la revisó Claude: inicio del gesto con confianza baja, techo de 3072 px y origen del seguimiento. Probada en el Quest 3 el 9 de noviembre de 2026: el agarre, la física y el tamaño funcionan bien.
- **Prueba del 9 de noviembre de 2026 (04:15)**, con la casa, el menú y el agarre. La casa se ve bien. El agarre funciona con el mando, pero no con las manos (el usuario probó pellizco y mano completa). Las letras del menú no caben en los botones. El usuario pidió movimiento libre, física realista, cambiar el tamaño del libro con dos manos y herramientas de muñeca. Plan por tandas A a E en [docs/INTERACCION.md](docs/INTERACCION.md).
- **Agarre propio (`BookGrab`)**, hecho por Codex y revisado por Claude, sin Interaction SDK. Con la mano: pellizco a menos de 4 cm del libro. Con el mando: botón de agarre (grip) a menos de 8 cm. Mientras el libro está tomado, los pellizcos no pasan página.
- `FolioBook` desactiva la Dynamic Resolution de Meta al arrancar, porque el Project Setup Tool la activó y pasaría por encima de la nitidez elegida.

## Menú esférico 3D (decisión del usuario, 7 de noviembre de 2026)

Esfera azul anclada al mando o la mano izquierda, que se abre con ☰ o, con las manos, cerrando y abriendo la mano izquierda con la palma hacia arriba y se gira con la mano derecha. Las opciones aparecen en un segundo anillo. Categorías: Biblioteca, Entornos, Ajustes, Importar y nubes, Hora. Especificación completa: [docs/MENU_ESFERICO.md](docs/MENU_ESFERICO.md).

**Estado (8 de noviembre de 2026):** el menú simple está programado por Codex (GPT-6 Astra, esfuerzo Alto), revisado por Claude e integrado en `fase-1/preparacion`; falta probarlo en el Quest 3. Se abre con ☰ o con el gesto de la palma, y hay detalles en [docs/MENU_SIMPLE.md](docs/MENU_SIMPLE.md#estado). La esfera sigue pendiente.

También habrá un **menú simple** para quien no quiera la esfera: un panel plano con las mismas opciones, que se toca con el dedo o se apunta con el rayo del mando. Se elige en Ajustes, y la esfera queda por defecto. Especificación: [docs/MENU_SIMPLE.md](docs/MENU_SIMPLE.md). Orden: entorno de prueba, luego el menú simple con el modelo compartido y después la esfera.

## Archivos de varios GB (5 GB requeridos, pruebas hasta 20 GB)

Reglas de diseño, válidas para todos los formatos:

1. **Desplazamientos de 64 bits en toda la cadena** (Java/Kotlin `long`, C/C++ `int64_t`/`off64_t`, C# `long`). Ningún tamaño ni posición de archivo en `int` de 32 bits.
2. **Nunca leer el archivo completo** a memoria ni recorrerlo entero para mostrar la primera página. PDF y ZIP permiten ir directo al índice del final del archivo.
3. **No obligar a copiar 5 GB.** Importar debe ofrecer «abrir en su ubicación» (permiso persistente o ruta propia de la app) y «copiar a la biblioteca», en este caso con comprobación de espacio libre antes de empezar, progreso, cancelación y archivo temporal que solo se renombra al terminar.
4. **Descargas de la nube reanudables** (peticiones por rangos), con comprobación de espacio y de tamaño final; el hash completo es opcional y en segundo plano (calcular SHA-256 de 5 GB toma tiempo en el visor).
5. Los índices propios (páginas de un TXT, entradas de un ZIP, posiciones de EPUB) se guardan en disco (SQLite), no en memoria.

Puntos a verificar por formato:

| Formato | Riesgo con más de 4 GB | Qué hacer |
| --- | --- | --- |
| PDF (PdfRenderer/PDFium) | La estructura `FPDF_FILEACCESS` de PDFium declara el tamaño en un `unsigned long`: 64 bits en Android ARM64 (Quest 3), pero 32 bits en Windows | Probar en el visor `pdf-5gb-imagenes.pdf`; si falla, abrir con `FPDF_LoadDocument` o con MuPDF |
| PDF (MuPDF) | — | **Verificado en PC (Linux 64 bits):** abre el PDF de 5 GiB y dibuja su última página en 0,2 s con 62 MB; con el de 20 GiB, 1,4 s y 82 MB, sin reparar el archivo |
| PDF de más de ~9,3 GiB | El índice clásico (`xref`) escribe cada posición con 10 dígitos y no llega más allá de 9.999.999.999 bytes. Estos PDF usan un índice comprimido en *xref stream* (PDF 1.5) | El lector debe aceptar *xref streams*: PDFium, MuPDF y poppler lo hacen. Si un PDF enorme viene con un índice clásico roto, el lector lo reconstruye recorriendo todo el archivo: lento, mostrar progreso |
| PDF escaneados enormes | Una página puede traer una imagen gigante (por ejemplo, 10.000×14.000 px son ~560 MB sin comprimir) y agotar la memoria al decodificarla | El Hito 1 ya captura la falta de memoria y muestra «La página es demasiado grande…». Registrar qué archivo y página fallan para decidir el motor y los límites |
| CBZ/EPUB (ZIP) | Pasado 4 GiB el ZIP necesita la extensión ZIP64 | `java.util.zip` de Android incluye ZIP64 desde hace años y libarchive también; probar en el visor `cbz-5gb.cbz` |
| CBR (RAR sólido) | Para leer la página N hay que descomprimir todo lo anterior | Extraer a caché en disco con cuota y progreso, o avisar; no prometer páginas instantáneas |
| TXT | Decodificar 5 GB tarda | Índice de páginas por bloques en segundo plano; mostrar la primera página antes de terminar |
| EPUB (texto) | Un capítulo gigante usa mucha memoria al maquetar (~600 MB por un capítulo de 30 MB con MuPDF en PC) | Ver `docs/HITO2_ENFOQUE.md`; los EPUB de 5 GB suelen ser imágenes, no texto |

Archivos de prueba (5 de noviembre de 2026): `tools/test-data/gigantes.py --solo pdf-gb --solo cbz-gb --gb 5` genera en 13 s, con 53 MB de memoria, un PDF de 5.659 páginas (5,37 GB) y un CBZ ZIP64 de 5.662 imágenes (5,37 GB). Verificación: `pdfinfo` cuenta las páginas, `qpdf --check` no encuentra errores y poppler dibuja las páginas 1, 2.829 y 5.659. En el CBZ, `unzip -t` no da errores y la última imagen, que empieza en el byte 5.367.717.314 (más allá de 4 GiB), se lee y es válida.

PDF de 20 GiB (mismo día): `--solo pdf-gb --gb 20` genera en 27 s, con 56 MB de memoria, un PDF 1.5 de 22.635 páginas (21,47 GB) con índice *xref stream*. `pdfinfo` y `qpdf --show-npages` leen el índice al instante y sin reparaciones; poppler dibuja las páginas 1, 11.317 y 22.635, y MuPDF la última en 1,4 s con 82 MB. `qpdf --check` completo (lee los 20 GB) no terminó en 10 minutos; no indica un error.

Pendiente en el visor: abrir los **PDF reales del usuario (hasta 20 GB)** y los generados en el Quest 3 (prueba opcional del Hito 1 en `docs/HITO1_GUIA.md`).

## Riesgos que guían el orden

| Riesgo | Cómo se resuelve o contiene | Momento |
| --- | --- | --- |
| EPUB Android no se transforma directamente en páginas Unity | Prueba de render, texto nítido, selección de resolución y transferencia GPU/CPU; decidir backend tras medir | Inicio Fase 1 |
| Editor/Meta XR/NDK incompatibles o problemas Linux | Matriz oficial, proyecto mínimo ARM64 y versiones fijadas; alternativa Ubuntu LTS soportada | Inicio Fase 1 |
| Acceso aleatorio imposible en RAR sólido o parser que carga todo | Detectar capacidades, extraer a disco con cuotas/progreso o limitar/convertir explícitamente | Fases 1–2; estrés Fase 4 |
| LGPL y componentes con licencias distintas | Inventario por versión, avisos y obligaciones de redistribución antes de integrar/publicar | Fases 1–3 |
| OAuth Drive no funciona en navegador Quest con cliente elegido | Prototipo en visor con la configuración oficial vigente; no presentar un workaround no autorizado como solución | Inicio del conector, Fase 3 |
| Pérdida de archivo o progreso por suspensión/cierre | Copias privadas, commit transaccional, guardado por página y recuperación; pruebas de interrupción | Fase 1 |
| Archivos de 5 GB: límites de 32 bits en alguna librería, copias completas o falta de espacio | Pruebas con `pdf-5gb-imagenes.pdf` y `cbz-5gb.cbz` en el visor; abrir sin copiar; comprobar espacio antes de copiar o descargar | Desde Fase 1 (PDF); Fases 2–4 |
| Presión de memoria con tres libros/texturas | Presupuesto global, LRU, resolución adaptable, límites de trabajos y cierre ordenado | Desde Fase 1 |
| Libro hostil, comprimido desmesurado o parser nativo fallando | Cuotas, validación, rutas seguras, aislamiento cuando sea viable y diagnóstico sin falsos positivos | Desde Fase 1 |

## Evidencia y límites de esta entrega

- Se ha inspeccionado la copia local, inicialmente limpia y con solo `README.md`.
- El entorno cloud dispone de Linux y Java; no se detectaron Unity, `adb` ni Gradle en el PATH. No hay un Quest conectado ni un proyecto compilable.
- Se consultaron repositorios oficiales accesibles para contrastar bibliotecas y ejemplos. Algunos portales de documentación oficiales devolvieron HTTP 403 por la política de red; se documenta esa limitación junto a las fuentes.
- La revisión inicial de Fase 0 fue documental. El Hito 1 se compiló y probó después en el Quest 3 del usuario; el Hito 1.2 se revisa manualmente aquí, sin compilar Unity ni ejecutar pruebas en el visor. No se han verificado OAuth, persistencia de biblioteca ni rendimiento.
- Validación documental: seis archivos Markdown legibles en UTF-8, nueve enlaces locales válidos, bloques de código cerrados y ausencia de espacios finales; `git diff --check` sin errores. Revisión cruzada de fases y requisitos completada.
- No se solicitaron ni guardaron credenciales. No se modificó la política de red ni se instalaron toolchains para fases futuras.

## Próxima conversación

Leer este archivo y los documentos enlazados, revisar `git status` y respetar el trabajo existente. Usar la copia de este repositorio en `/workspace`; la tarea cloud ya está aislada y no necesita un worktree nuevo.

**Pendientes del usuario:** probar el ajuste de nitidez del Hito 1.2 en el visor y completar las verificaciones detalladas aún abiertas del checklist. La licencia ya está decidida: venta en la Meta Horizon Store con código cerrado; el enfoque propuesto del Hito 2 usa WebView (ver [Publicación comercial](#publicación-comercial-decisión-del-usuario-5-de-noviembre-de-2026)), pero su implementación no está autorizada.

Avance sin depender del visor (5 de noviembre de 2026, rama `fase-1/preparacion`): muestras en `tests/fixtures/` (EPUB válido según epubcheck 5.3.0, CBZ, manga RTL, TXT en 4 codificaciones) y `tools/test-data/gigantes.py` (PDF de 100.001 páginas verificado con poppler y qpdf, TXT, CBZ y EPUB de estrés). Esto adelanta parte del generador previsto para la Fase 4.

**El Hito 1 ya está confirmado; no empezar el Hito 2.** El Hito 1.2 ya está unido a `fase-1/preparacion`; falta la prueba del ajuste en el Quest 3. Si falla, corregir a partir de su registro (`adb logcat`) y del paso de `docs/HITO1_GUIA.md` donde se detuvo. Actualizar aquí resultados, decisiones, limitaciones, instrucciones y checklist al cerrar cada hito.
