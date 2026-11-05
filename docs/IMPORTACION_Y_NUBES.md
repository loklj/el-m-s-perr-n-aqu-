# Importación, nubes y catálogos OPDS

Estado: diseño de la Fase 0, 5 de noviembre de 2026. No se han creado clientes OAuth, servicios, credenciales ni integraciones. Las instrucciones de las consolas son preparación para la Fase 3, no tareas que debas ejecutar ahora.

## Decisión general

La biblioteca usa copias locales duraderas. Los proveedores solo ayudan a elegir y descargar archivos; perder Internet, revocar una cuenta o cambiar una URL no debe hacer desaparecer un libro ya importado. Todos los orígenes terminan en el mismo proceso de validación e importación.

| Origen | Fase prevista | Decisión |
| --- | --- | --- |
| Download del Quest | 1 | Selector Android mediante Storage Access Framework cuando esté disponible; copiar al almacenamiento interno de la app. |
| PC por Wi-Fi | 3; adelantar a 1 si SAF no funciona en Quest | Página de carga local activada por el usuario, como alternativa de importación local si Horizon OS no expone un selector compatible. |
| Google Drive | 3 | API de Drive, OAuth en navegador del sistema; resolver y probar tipo de cliente y redirección antes de comprometer una integración. |
| Dropbox | 3 | SDK oficial Android/Kotlin o núcleo Java, código de autorización con PKCE y acceso de solo lectura. |
| MEGA | 3 | SDK oficial C++ con sus enlaces Android; autenticación propia de MEGA, no un OAuth inventado. |
| Calibre-Web, Kavita y Komga | 3 | Cliente OPDS 1.2 y 2.0, descarga de adquisiciones sin DRM y copia offline. |
| Kindle para la web y otros lectores externos | 5, baja prioridad | Ficha de estantería que abre el navegador; sin importar ni extraer contenido del lector externo. |

## Contrato de importación

1. Crear un trabajo persistente con origen, identificador remoto, estado, bytes descargados y, cuando existan, tamaño, versión, ETag y checksum del proveedor. No registrar tokens ni contraseñas en los logs.
2. Escribir en un archivo parcial dentro del almacenamiento de la app, con buffers acotados y espacio libre comprobado. Un libro de cientos de MB no pasa completo por un `byte[]`, una cadena ni un puente JNI.
3. Mostrar progreso y permitir cancelar. Si el tamaño remoto no se conoce, mostrar bytes y estado sin inventar un porcentaje.
4. Para reanudar HTTP, usar los mecanismos documentados del proveedor: `Range`/`If-Range` cuando los admita; comprobar respuesta `206` y rango real. Si responde `200`, no concatenar dos archivos: reiniciar la descarga. Una URL firmada caducada se renueva desde el proveedor.
5. Comprobar tamaño esperado cuando esté disponible, integridad del contenedor y checksum remoto cuando exista. Un SHA-256 calculado solo localmente sirve para identificar y deduplicar, pero no prueba por sí solo que el servidor entregó el archivo completo. Dropbox utiliza su algoritmo de `content_hash`, no el SHA-256 directo de todo el archivo; MEGA utiliza la validación del SDK.
6. Detectar formato y protección por el contenido, con lectura acotada y parser específico. Una extensión, un MIME remoto o un error genérico no demuestran DRM.
7. Tras validar, renombrar el parcial en el mismo volumen y registrar la importación mediante transacción de la base de datos. Como archivo y SQLite no comparten una transacción, usar un estado de importación recuperable: al reiniciar se reconcilian archivos finales, registros pendientes y parciales. No publicar una ficha que apunta a un temporal.
8. Mantener el libro disponible offline y guardar procedencia para un eventual reintento. Una copia fallida ofrece reintentar; no destruye una copia local anterior válida.

Pruebas futuras obligatorias: quitar Wi-Fi a mitad de descarga, cancelar, reiniciar la app, caducidad de URL/token, tamaño incorrecto, disco lleno, archivo reemplazado en el servidor y cierre entre renombrado y transacción. Distinguir descarga incompleta, archivo dañado, DRM detectado, contraseña requerida, formato aún no compatible y causa desconocida.

## Archivos locales y PC por Wi-Fi

### Download del Quest

El ejemplo oficial de Android confirma el uso de `ACTION_OPEN_DOCUMENT` sin depender de permisos amplios de almacenamiento [1]. No prueba que la versión concreta de Horizon OS incluya un selector funcional. En Fase 1 se comprobará en el Quest 3 real y se conservará la alternativa de carga desde PC.

La elección por defecto es copiar el contenido al almacenamiento interno. Un URI de documento puede no permitir acceso aleatorio; primero se copia a disco. Si se ofrece vincular un archivo externo como opción avanzada, se deben conservar permisos persistentes cuando el proveedor los otorgue y resolver su revocación. No convertir un URI en una supuesta ruta física ni depender de rutas temporales de otras apps.

### Página de carga local

Flujo previsto para el usuario:

1. Conectar PC y visor a la misma red de confianza.
2. En el visor, activar «Importar desde el PC». La app muestra la dirección LAN y un código de emparejamiento de un solo uso.
3. En el navegador del PC, introducir esa dirección, emparejar y elegir archivos. La página transmite datos por bloques a disco; no carga el libro completo en memoria.
4. Consultar el resultado de cada importación en el visor y cerrar el modo de recepción al terminar.

El servidor no estará siempre activo ni expondrá la biblioteca completa. Se limitarán duración, tamaño, nombres de archivo y rutas; las cargas necesitarán la sesión emparejada. La versión inicial en HTTP local no protege los bytes contra alguien que controle la red: deberá indicarse claramente y usarse solo en una LAN de confianza. No se configura apertura de puertos en el router ni acceso público. Antes de distribución se evaluará HTTPS con emparejamiento de confianza; no se desactivará la validación TLS de las nubes para resolver la carga local.

## Google Drive: el principal riesgo de autenticación

Requisitos firmes: Horizon OS sin Google Mobile Services, login en navegador del sistema, sin Google Sign-In de Play Services/Firebase y sin login incrustado en WebView. El flujo solicitado debe incorporar PKCE S256 y validar `state`.

**No hay todavía una combinación cliente/redirect validada en Quest 3.** Los documentos actuales de Google no se pudieron abrir desde este entorno: el proxy respondió `403` al establecer la conexión. Las restricciones siguientes deben contrastarse con [2–4] y probarse en hardware antes de implementar Drive:

- No adoptar un cliente Android con esquema URI personalizado suponiendo que Google lo acepta. Las restricciones de Google sobre esquemas personalizados en Android y el flujo para clientes Android requieren una revisión específica.
- No registrar un cliente «Desktop app» para simular que el APK es una aplicación Linux, ni extrapolar al Quest un ejemplo de Windows. El ejemplo oficial accesible utiliza un loopback en escritorio y no constituye soporte para Android [5].
- El flujo para TV/dispositivos no cubre la navegación de toda la unidad con los scopes de Drive indicados por el usuario (`drive.file` y `drive.appdata`). No se utilizará como atajo para obtener acceso completo.
- `drive.file` permite archivos que la app crea o que el usuario concede a esa app; no equivale a listar toda la biblioteca de Drive. Para explorar carpetas existentes se evaluará `drive.readonly`, un alcance restringido, con los requisitos de verificación y tratamiento de datos que Google exija. El documento oficial de descubrimiento de Drive confirma estos scopes y sus descripciones [6]; no resuelve el flujo de autenticación de Quest.

### Ruta candidata y alternativa

La ruta candidata a probar en Fase 3 es un cliente **Web application** con callback **HTTPS** en un servicio propio mínimo. El navegador del visor inicia la autorización; Google devuelve el código al callback registrado. Un identificador de sesión de un solo uso enlaza esa operación con la app. El secreto de cliente se queda en el servidor. La vuelta al visor se resuelve con un enlace de aplicación verificado o consulta autenticada de la sesión; no con tokens en URLs.

Esto introduce un backend, dominio, mantenimiento y responsabilidad sobre credenciales. Es una propuesta condicionada: antes de adoptarla se debe confirmar con la documentación vigente que la combinación concreta acepta PKCE S256, y diseñar dónde quedan los tokens y qué acceso puede tener el backend. Si Google no soporta esa combinación, no se presenta como compatible ni se omite PKCE silenciosamente. Un cliente completamente autónomo, sin GMS y sin backend, sigue siendo una cuestión abierta.

Mientras se resuelve, el usuario puede descargar legalmente los archivos desde Drive en su PC e importarlos por Wi-Fi. Esa alternativa mantiene lectura offline, pero no sustituye la integración de Drive prevista en Fase 3.

### Preparación futura en Google Cloud Console

1. Crear o seleccionar un proyecto propio en Google Cloud Console.
2. Activar Google Drive API.
3. Configurar la pantalla de consentimiento/Google Auth Platform: nombre propio, contacto, audiencia y usuarios de prueba. Los nombres de las secciones pueden cambiar.
4. Declarar el alcance mínimo que cubra el producto. Para navegar todo el Drive de lectura, evaluar `drive.readonly`; documentar por qué `drive.file` no alcanza. Revisar verificación de scopes restringidos y si corresponde evaluación de seguridad, especialmente si un backend maneja datos.
5. Solo tras validar la ruta anterior, crear el tipo de cliente correcto. Si es Web application, registrar exactamente el callback HTTPS propio y proteger el secreto en el backend. No ponerlo en Unity, Git, el APK ni un archivo distribuido.
6. Probar autorización con una cuenta de prueba en el navegador real del visor, retorno a la app, listados paginados, Shared Drives si se incluyen, descarga, cancelación, revocación, cierre del visor y renovación de sesión.
7. Comprobar las limitaciones de cuentas en modo de prueba y la duración de los refresh tokens. Pasar a producción únicamente después de cumplir los requisitos vigentes de Google.

No se necesitan credenciales de Google durante la Fase 0. La disponibilidad de Google Drive para este producto no se declara validada.

## Dropbox

La fuente oficial del SDK confirma Android, autenticación en navegador si no está instalada la app de Dropbox y una entrada PKCE [7]. El núcleo Java documenta PKCE sin `client_secret` y el flujo sin redirect que muestra un código para copiar y pegar [8].

Se propone OAuth con código de autorización, PKCE S256, `state` y acceso offline para refresco. El cliente público conoce la app key/client ID; no incluye el app secret. Los tokens y el material de sesión se protegen con claves de Android Keystore y nunca se exportan junto con la biblioteca.

El fallback manual es un flujo de autorización **nuevo sin redirect**, conservando su propio verificador PKCE hasta el intercambio. No se toma un código de una redirección fallida y se canjea con parámetros diferentes. La instancia o estado de la operación debe ser el mismo que generó la autorización, como exige el SDK.

### Preparación futura en Dropbox App Console

1. Entrar en [Dropbox App Console](https://www.dropbox.com/developers/apps) con tu cuenta y crear una app de acceso con scopes.
2. Para explorar libros existentes en cualquier carpeta, seleccionar acceso a **Full Dropbox**. **App folder** solo cubre la carpeta de esa app.
3. Darle el nombre propio del proyecto y habilitar únicamente los permisos de lectura necesarios, inicialmente `files.metadata.read` y `files.content.read`. Agregar otros solo si una función verificada lo requiere.
4. Guardar la app key como configuración pública. No distribuir el app secret ni un token personal generado en la consola.
5. Configurar el retorno según la versión elegida del SDK. El README actual muestra el esquema `db-<app-key>` y la actividad de autenticación; se comprobará en Quest que el navegador devuelve el control correctamente. Si se usa otro callback, registrarlo exactamente en la consola cuando corresponda.
6. Probar PKCE y renovación offline. Si el retorno automático no funciona, iniciar el flujo documentado sin redirect y permitir pegar el código.
7. Antes de publicar para terceros, revisar límites del modo desarrollo y los requisitos de producción de Dropbox.

Las fuentes de código confirman los mecanismos; el flujo completo en Quest queda pendiente de Fase 3. No hace falta instalar la aplicación móvil de Dropbox en el visor.

## MEGA

Se elige el SDK oficial frente a una implementación no oficial del protocolo. Su repositorio documenta C++, enlaces Java/Android, compilación con CMake y Android como plataforma soportada [9]. La licencia principal es **BSD-2-Clause** [10]; se deben inventariar también las dependencias incluidas en la compilación. La versión consultada declara Android 9 como mínimo, pendiente de cruzar con el sistema real del visor y las versiones fijadas del proyecto.

La API exige una application key y ofrece login, MFA, sesiones y transferencias [11]. No se inventa un flujo OAuth de MEGA: las credenciales se introducen en una pantalla propia y se entregan únicamente al SDK oficial. No se conserva la contraseña ni se imprime en logs; se guarda el material de sesión protegido. Cerrar sesión borra ese material, sin eliminar las copias offline salvo que el usuario lo solicite.

El cifrado de almacenamiento de MEGA es parte de su servicio y no implica que el EPUB/PDF contenga DRM editorial. Descargar con el SDK y las credenciales legítimas del usuario no elimina un DRM del libro. Después de la descarga se aplica la detección del archivo igual que con los demás proveedores.

### Preparación futura de MEGA

1. Crear o utilizar tu cuenta MEGA.
2. Abrir el portal oficial de desarrolladores, [mega.io/developers](https://mega.io/developers), y acceder a la gestión de aplicaciones/API que ofrezca la cuenta.
3. Registrar una aplicación con nombre propio y los datos que solicite el portal; obtener una application key. Las pantallas exactas del portal no se pudieron verificar desde este entorno.
4. Incorporar esa clave de aplicación al adaptador del SDK según su documentación. No confundirla con la contraseña, la clave de recuperación o la sesión de la cuenta: esos datos no se incorporan al proyecto.
5. Compilar el SDK para `arm64-v8a`, inventariar dependencias y licencias, y probar login con MFA cuando esté activado, listado paginado, descarga, cancelación, reanudación, cuota agotada, cierre y reapertura de sesión.

El soporte anunciado por upstream no equivale a una compilación o una prueba realizada aquí. No se solicitarán credenciales en el chat.

## Por qué no usar rclone embebido como backend único

| Aspecto | Evaluación |
| --- | --- |
| Licencia | MIT para rclone; revisar también dependencias [12]. |
| Integración | `librclone` expone un puente C/RPC con runtime Go; upstream etiqueta sus interfaces como experimentales [13]. |
| Android | Requiere validar compilación cruzada NDK/arm64, cgo, ciclo de vida, memoria y cancelación; no basta ejecutar rclone en Linux. |
| OAuth | No hace desaparecer las restricciones de Google, los tipos de cliente, las redirecciones ni la verificación de scopes. |
| MEGA | El backend consultado importa `go-mega`, no el SDK oficial de MEGA [14]. |
| Persistencia | Su configuración de credenciales necesitaría integrarse cuidadosamente con Keystore y el ciclo de vida de la app. |

Decisión: adaptadores nativos bajo una interfaz de aplicación común para listar, descargar, cancelar y reanudar. Rclone queda como herramienta opcional en el PC para quien ya lo use; no será una dependencia del MVP ni la base de las tres integraciones.

## OPDS en la Fase 3

El usuario introduce la URL de catálogo que entrega su servidor; no se adivinan rutas ni se exige un servicio público. Se implementarán navegación, búsqueda si el catálogo la anuncia, paginación y enlaces de adquisición. OPDS 1.2 utiliza Atom/XML y OPDS 2.0 utiliza JSON [15,16]. Se detectará por contenido y tipo de respuesta, no por que la URL termine de cierta forma.

| Servidor objetivo | Evidencia y prueba necesaria |
| --- | --- |
| Calibre-Web | Su README oficial anuncia OPDS y muestra una ruta de catálogo [17]. Probar la versión concreta, biblioteca, búsqueda y autorización configuradas por el usuario. |
| Kavita | Objetivo solicitado. La documentación operativa actual del catálogo no pudo verificarse aquí; copiar la URL que proporciona la instancia y comprobar versión OPDS y esquema de autenticación en Fase 3 [18]. |
| Komga | El README oficial anuncia OPDS v1 y v2 [19]. Probar ambos contra una instancia real, incluidos permisos por biblioteca. |

La primera matriz incluirá catálogos anónimos, HTTP Basic sobre TLS y tokens/URLs de acceso cuando el servidor los utilice. No se tratará la compatibilidad OPDS como promesa de cualquier login web, SSO o proxy de autenticación. Las URL que llevan tokens son credenciales: no entran en logs ni respaldos sin protección. No se reenvía automáticamente un encabezado de autenticación a otro dominio al seguir un enlace de descarga.

Solo se importarán adquisiciones sin DRM. Un archivo ACSM o una licencia LCP no es el EPUB/PDF descargable; se identifica y se explica la limitación, sin intentar convertirlo ni quitar su protección. Readium LCP queda fuera. OPDS no garantiza que el catálogo sirva libros sin DRM ni que el formato ya esté implementado.

Se limitarán profundidad de navegación, cantidad y tamaño de respuestas; el parser XML no resolverá entidades externas. Las descargas grandes siguen el contrato común y no necesitan cargar un feed entero ni todos los libros en memoria. El formato de imagen/cómic recibido se valida después de adquirirlo.

Criterios de aceptación de Fase 3: importar y abrir offline un libro de cada servidor configurado; navegar varias páginas de catálogo; manejar un `401`, un enlace caducado, un formato no compatible y una adquisición protegida; cancelar y reanudar una descarga sin duplicar ni perder la ficha local.

## Guía Kindle que incluirá la app

Texto previsto para la ayuda de importación; aún no hay una interfaz donde mostrarlo:

> **Importar un libro Kindle sin DRM**
>
> 1. Entra en Amazon desde tu navegador con la cuenta que compró el libro.
> 2. Abre **Contenido y dispositivos**, localiza el título y entra en **Más acciones**.
> 3. Si ese libro permite **descargar EPUB/PDF**, elige uno de esos formatos y guarda el archivo. La opción solo aparece cuando Amazon y el título lo permiten; comprar el libro no garantiza que esté disponible.
> 4. Transfiere el archivo a Download del Quest o impórtalo desde tu PC por Wi-Fi. En la app, selecciona el archivo para copiarlo a tu biblioteca offline.
> 5. Si no aparece la opción, busca una edición que el editor ofrezca sin DRM. Un archivo protegido no podrá abrirse en esta app.

Esta guía recoge la decisión del usuario. La disponibilidad, las etiquetas exactas y los requisitos de la opción de Amazon en cada región/cuenta deben comprobarse antes de incorporarla a una versión pública. No se pudo abrir la ayuda vigente de KDP desde este entorno [20], por lo que no se presenta esa navegación como comprobada aquí. No se sustituirá por el antiguo flujo de transferencia USB ni por extracción de claves o de contenido de Kindle.

En Fase 5, una ficha opcional podrá abrir Kindle para la web mediante el navegador del sistema. Se verificará entonces disponibilidad en la región, compatibilidad del navegador y necesidad de conexión. Esa ficha no será un libro 3D importado ni ofrecerá progreso sincronizado sin una API pública autorizada.

## Fuentes y nivel de verificación

Consulta realizada el **5 de noviembre de 2026**. «Leída» significa respuesta HTTPS satisfactoria y revisión del contenido; no significa compilación, autenticación ni ejecución en Quest. Las ramas upstream pueden cambiar: al integrar se fijará versión/commit y se revisará de nuevo.

1. **Leída** — [Android: ejemplo oficial ACTION_OPEN_DOCUMENT](https://github.com/android/storage-samples/tree/main/ActionOpenDocument).
2. **Acceso bloqueado por proxy; revisión vigente pendiente** — [Google: OAuth para apps nativas](https://developers.google.com/identity/protocols/oauth2/native-app).
3. **Acceso bloqueado por proxy; revisión vigente pendiente** — [Google: OAuth para dispositivos con entrada limitada](https://developers.google.com/identity/protocols/oauth2/limited-input-device).
4. **Acceso bloqueado por proxy; revisión vigente pendiente** — [Google Drive: alcances de autorización](https://developers.google.com/workspace/drive/api/guides/api-specific-auth).
5. **Leída; ejemplo de Windows, no prueba de soporte Quest** — [Google: muestras OAuth de escritorio](https://github.com/googlesamples/oauth-apps-for-windows).
6. **Leída** — [Google: documento oficial de descubrimiento Drive v3](https://github.com/googleapis/google-api-python-client/blob/main/googleapiclient/discovery_cache/documents/drive.v3.json).
7. **Leída** — [Dropbox SDK Java/Android](https://github.com/dropbox/dropbox-sdk-java), sección de configuración Android.
8. **Leída** — [Dropbox: DbxPKCEWebAuth](https://github.com/dropbox/dropbox-sdk-java/blob/main/core/src/main/java/com/dropbox/core/DbxPKCEWebAuth.java), documentación de `withNoRedirect`/`finishFromCode`.
9. **Leída** — [MEGA SDK: README](https://github.com/meganz/sdk).
10. **Leída** — [MEGA SDK: licencia](https://github.com/meganz/sdk/blob/master/LICENSE).
11. **Leída** — [MEGA SDK: API pública y requisitos de application key](https://github.com/meganz/sdk/blob/master/include/megaapi.h).
12. **Leída** — [rclone: licencia](https://github.com/rclone/rclone/blob/master/COPYING).
13. **Leída** — [librclone: interfaces experimentales](https://github.com/rclone/rclone/tree/master/librclone).
14. **Leída** — [rclone: backend MEGA](https://github.com/rclone/rclone/blob/master/backend/mega/mega.go).
15. **Leída desde el repositorio oficial** — [OPDS Catalog 1.2](https://github.com/opds-community/specs/blob/master/opds-1.2.md).
16. **Leída desde el repositorio oficial** — [OPDS Catalog 2.0](https://github.com/opds-community/specs/blob/master/opds-2.0.md).
17. **Leída** — [Calibre-Web: README](https://github.com/janeczku/calibre-web).
18. **README leído; catálogo operativo pendiente** — [Kavita](https://github.com/Kareadita/Kavita) y [documentación oficial](https://wiki.kavitareader.com/).
19. **Leída** — [Komga: README y soporte OPDS v1/v2](https://github.com/gotson/komga).
20. **Acceso bloqueado por proxy; revisión vigente pendiente** — [Amazon KDP: ayuda sobre DRM](https://kdp.amazon.com/en_US/help/topic/G200644210).

La guía OAuth de Dropbox, la ayuda de Android y los sitios web de OPDS también quedaron bloqueados por la política de red; se utilizaron las fuentes oficiales accesibles en GitHub indicadas arriba. No se modificó la política de red ni se eludió el proxy.
