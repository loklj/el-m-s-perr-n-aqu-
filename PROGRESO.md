# Folio XR — progreso y plan

Última actualización: 5 de noviembre de 2026. Nombre de trabajo propio y provisional; no se ha comprobado su disponibilidad comercial. Repositorio: `loklj/el-m-s-perr-n-aqu-`.

## Punto de reanudación

**Fase 0 completada como entrega documental: arquitectura y planificación.** No hay aplicación, proyecto Unity, APK ni implementación de lectores. No comenzar la Fase 1 sin el OK explícito del usuario.

El repositorio partía de un README. Esta fase añade documentación de diseño, evaluación de dependencias, riesgos, estructura propuesta y procedimientos de validación. Las instrucciones de compilación e instalación describen trabajo futuro en el PC; no acreditan pruebas realizadas.

## Documentos

- [Arquitectura y estructura propuesta](docs/ARQUITECTURA.md).
- [Formatos, bibliotecas y licencias](docs/FORMATOS_Y_LICENCIAS.md).
- [Importación local, nubes, OPDS y guía Kindle](docs/IMPORTACION_Y_NUBES.md).
- [Preparación del PC, compilación, instalación y pruebas](docs/PC_Y_VALIDACION.md).

## Objetivo del producto

Lector personal para Meta Quest 3: libros, cómics y manga como objetos 3D agarrables, movibles y con páginas que se pueden pasar. Tres libros abiertos simultáneamente como objetivo base; ampliar esa cantidad solo después de medir. Lectura sentado, de pie o acostado, controles Touch y manos, VR inmersiva y passthrough. La biblioteca y el progreso deben sobrevivir a cierres, reinicios y actualizaciones compatibles.

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

### Fase 1 — MVP local y biblioteca persistente (pendiente de OK)

1. Fijar versiones compatibles de Unity LTS, Meta XR, paquetes XR, JDK/SDK/NDK/Gradle y bibliotecas. Guardar versiones y lockfiles; compilar un proyecto mínimo Android ARM64 con IL2CPP en Pop!_OS y probar VR, passthrough, Touch y manos en Quest 3.
2. Resolver el prototipo de página EPUB en una textura 3D legible. Evaluar Readium en Android y su integración gráfica antes de construir el lector completo; una vista Android plana no cumple el requisito del libro 3D.
3. Crear biblioteca Room, importación local con SAF, copia privada, estados de descarga/importación, recuperación, progreso, marcadores y respaldo versionado básico. Comprobar reinicio y actualización de APK conservando datos. Si SAF no funciona en Horizon OS, adelantar la carga por Wi-Fi desde el PC como alternativa local; de lo contrario, queda en Fase 3.
4. PDF con PDFium, CBZ/ZIP con imágenes y EPUB sin DRM; portada, apertura cancelable, errores específicos, navegación básica y cachés acotadas. No dejar las pruebas de memoria para el final.
5. Un libro manipulable con geometría propia y animación de páginas; después tres libros simultáneos con un presupuesto compartido. Añadir manga derecha a izquierda y doble página en el lector de cómics básico.
6. Validar las posturas de lectura y la estabilidad del libro al recentrar, reanudar o perder seguimiento.

Salida: APK reproducible y una sesión real con los tres formatos, tres libros, biblioteca duradera, recuperación de un fallo e instrucciones actualizadas con las versiones efectivamente usadas. Sin esta evidencia no marcar el MVP como completado.

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

## Riesgos que guían el orden

| Riesgo | Cómo se resuelve o contiene | Momento |
| --- | --- | --- |
| EPUB Android no se transforma directamente en páginas Unity | Prueba de render, texto nítido, selección de resolución y transferencia GPU/CPU; decidir backend tras medir | Inicio Fase 1 |
| Editor/Meta XR/NDK incompatibles o problemas Linux | Matriz oficial, proyecto mínimo ARM64 y versiones fijadas; alternativa Ubuntu LTS soportada | Inicio Fase 1 |
| Acceso aleatorio imposible en RAR sólido o parser que carga todo | Detectar capacidades, extraer a disco con cuotas/progreso o limitar/convertir explícitamente | Fases 1–2; estrés Fase 4 |
| LGPL y componentes con licencias distintas | Inventario por versión, avisos y obligaciones de redistribución antes de integrar/publicar | Fases 1–3 |
| OAuth Drive no funciona en navegador Quest con cliente elegido | Prototipo en visor con la configuración oficial vigente; no presentar un workaround no autorizado como solución | Inicio del conector, Fase 3 |
| Pérdida de archivo o progreso por suspensión/cierre | Copias privadas, commit transaccional, guardado por página y recuperación; pruebas de interrupción | Fase 1 |
| Presión de memoria con tres libros/texturas | Presupuesto global, LRU, resolución adaptable, límites de trabajos y cierre ordenado | Desde Fase 1 |
| Libro hostil, comprimido desmesurado o parser nativo fallando | Cuotas, validación, rutas seguras, aislamiento cuando sea viable y diagnóstico sin falsos positivos | Desde Fase 1 |

## Evidencia y límites de esta entrega

- Se ha inspeccionado la copia local, inicialmente limpia y con solo `README.md`.
- El entorno cloud dispone de Linux y Java; no se detectaron Unity, `adb` ni Gradle en el PATH. No hay un Quest conectado ni un proyecto compilable.
- Se consultaron repositorios oficiales accesibles para contrastar bibliotecas y ejemplos. Algunos portales de documentación oficiales devolvieron HTTP 403 por la política de red; se documenta esa limitación junto a las fuentes.
- La revisión realizada aquí es documental. No se ejecutaron pruebas de renderizado, OAuth, persistencia de la aplicación ni rendimiento; no existe aún esa aplicación.
- Validación documental: seis archivos Markdown legibles en UTF-8, nueve enlaces locales válidos, bloques de código cerrados y ausencia de espacios finales; `git diff --check` sin errores. Revisión cruzada de fases y requisitos completada.
- No se solicitaron ni guardaron credenciales. No se modificó la política de red ni se instalaron toolchains para fases futuras.

## Próxima conversación

Leer este archivo y los documentos enlazados, revisar `git status` y respetar el trabajo existente. Usar la copia de este repositorio en `/workspace`; la tarea cloud ya está aislada y no necesita un worktree nuevo.

**Esperar el OK del usuario para la Fase 1.** Después, empezar por versiones y los prototipos de Android/EPUB 3D. Obtener antes de instalar en el PC su versión de Pop!_OS, CPU/GPU/RAM y espacio libre; no son datos necesarios para cerrar esta Fase 0. Actualizar aquí resultados, decisiones, limitaciones, instrucciones y checklist al cerrar cada fase.
