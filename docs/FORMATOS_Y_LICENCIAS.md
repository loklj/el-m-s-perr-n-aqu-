# Formatos, protección y licencias — Fase 0

Fecha de revisión: 5 de noviembre de 2026. Este documento describe decisiones y pruebas pendientes; **ningún formato está implementado ni validado en Quest 3 todavía**. No se han incorporado bibliotecas ni fijado versiones de producción.

## 1. Decisión de implementación

Separar tres trabajos: identificar el contenedor y su protección, obtener contenido de lectura y producir una página visible. La extensión solo ayuda a presentar el archivo: no decide su formato ni si tiene DRM.

- PDF: candidato principal **PDFium**, detrás de una interfaz nativa propia.
- CBZ/ZIP: índice de entradas y lectura individual; **libarchive** como biblioteca de archivos, sujeto a demostrar acceso eficiente a entradas ZIP. No se presupone que su interfaz secuencial proporcione búsqueda aleatoria constante.
- EPUB: **Readium Kotlin Toolkit** para publicación, recursos, navegación y localizadores. La integración visual con el libro 3D es una prueba técnica obligatoria antes de comprometer el MVP completo.
- CBR: **libarchive**, con casos separados de RAR4 y RAR5 y preparación en disco cuando la compresión sea sólida.
- MOBI/AZW/AZW3: **libmobi es un candidato condicionado**, no una solución aprobada de streaming. Su API habitual carga registros y genera estructuras en memoria. No cumple automáticamente la regla de nunca cargar el archivo completo; limitar el tamaño del archivo no convierte esa ruta en streaming. Si no se obtiene una ruta de lectura incremental compatible con la licencia, se ofrecerá conversión local en el PC a EPUB sin DRM.
- TXT, DOCX, ODT y RTF: conversores incrementales a un modelo común de contenido. DOC binario y KFX requieren decisiones adicionales detalladas abajo.

El modelo interno propuesto conserva capítulos, párrafos, imágenes, estilos admitidos, idioma, dirección de lectura, índice y localizadores estables. Los recursos y el índice se guardan en disco; no se genera un único HTML gigante ni se concatena el libro completo en una cadena.

## 2. Matriz de formatos y viabilidad

| Formato solicitado | Identificación por contenido | Ruta prevista y fase | Límites que hay que comunicar |
| --- | --- | --- | --- |
| PDF | Cabecera PDF y estructura válida; examinar el manejador de seguridad al abrir | PDFium; Fase 1 | Contraseña estándar admitida cuando la proporciona el usuario. DRM Adobe o mecanismo no compatible se clasifica por evidencia, no por extensión. XFA y ejecución de JavaScript no forman parte del lector. |
| CBZ | ZIP válido con entradas de imágenes, tras descartar EPUB/DOCX/ODT | Índice de imágenes y render bajo demanda; Fase 1 | Un ZIP arbitrario no es un cómic. Orden natural y, si existe, metadatos compatibles; imágenes demasiado grandes se reducen antes de crear texturas. |
| ZIP | Directorio ZIP y contenido interno | La misma ruta de cómics, cuando contiene imágenes; Fase 1 | No se promete abrir cualquier contenido comprimido. ZIP64 y archivos truncados deben tener pruebas específicas. |
| EPUB | ZIP con `mimetype` EPUB, `META-INF/container.xml` y paquete válido | Readium; Fase 1 | EPUB 2/3 sin DRM al que el usuario tenga acceso legítimo; no requiere permiso de redistribución para lectura personal. EPUB de diseño fijo debe tener casos propios. Scripts, multimedia y funciones interactivas fuera del MVP. |
| CBR | Firma RAR4/RAR5 y entradas de imágenes | libarchive; Fase 2 | RAR4 y RAR5 no son intercambiables en las pruebas. RAR sólido puede exigir procesar entradas anteriores; variantes no admitidas y multivolumen se informarán expresamente. |
| TXT | BOM, validez UTF y análisis acotado; no existe firma universal | Decodificación y paginación incremental; Fase 2 | UTF-8, UTF-16 LE/BE y Windows-1252/Latin-1. Estas dos últimas no siempre pueden distinguirse automáticamente. Mostrar selección manual de codificación. |
| DOCX | ZIP con `[Content_Types].xml` y partes WordprocessingML | Lectura XML incremental hacia el modelo común; Fase 2 | Admitir primero texto, imágenes, listas, tablas sencillas y estilos definidos. No prometer maquetación idéntica a Word ni ejecutar macros. |
| ODT | ZIP con `mimetype` OpenDocument y `content.xml` | Lectura XML incremental hacia el modelo común; Fase 2 | La fidelidad de tablas complejas, objetos incrustados y estilos avanzados necesita corpus y pruebas. |
| RTF | Cabecera RTF y análisis estructural de grupos/control words | Parser incremental de un subconjunto documentado; Fase 2 | Escapes Unicode, páginas de códigos, tablas e imágenes requieren pruebas. OLE y contenido activo no se ejecutan. Lo no admitido debe avisarse. |
| DOC | Contenedor OLE/CFB y estructuras Word, sin confundirlo con otros OLE ni con Office cifrado | Investigación de conversor; Fase 2 | No hay una biblioteca Android seleccionada. Alternativa utilizable: abrir en LibreOffice en el PC y exportar PDF o DOCX. No anunciar soporte nativo antes de validarlo. |
| MOBI | Estructura PalmDB/Mobipocket, cabeceras y registros coherentes | Evaluar libmobi sin funciones DRM; Fase 2 | La importación debe demostrar memoria acotada. Si no lo consigue, conversión previa en PC a EPUB sin DRM. |
| AZW | Inspeccionar el contenedor real; el nombre AZW no identifica por sí solo una versión | Si es Mobipocket admitido y sin DRM, misma ruta; Fase 2 | Comprobar campos de protección; no declarar DRM por compra, tienda o extensión. Otras variantes: formato aún no compatible. |
| AZW3 | Mobipocket/KF8 y estructuras internas válidas | Evaluar libmobi/KF8 bajo las mismas condiciones; Fase 2 | Reconstrucción de HTML/CSS e imágenes, fuentes y diseño fijo requieren validación. No confundir KF8 con KFX. |
| KFX | Inspección estructural de contenedores y metadatos, no solo del sufijo | Reconocimiento y diagnóstico; Fase 2. Renderizador aplazado | libmobi no declara soporte KFX. Es un formato complejo, con variantes y recursos que pueden estar repartidos. No hay un motor Android seleccionado ni se promete convertir KFX sin DRM. Sugerir EPUB/PDF autorizado cuando exista. |

No encontrar un indicador de cifrado **no demuestra** que el archivo esté libre de DRM. Si la variante no se entiende, se reconoce el límite y se evita inventar un diagnóstico.

## 3. Bibliotecas principales

### Readium Kotlin Toolkit

**Licencia:** BSD-3-Clause en el repositorio upstream. Conservar licencia, atribuciones y avisos; revisar por separado dependencias transitivas, fuentes y contenidos distribuidos. [R1]

**Ventajas:** biblioteca Android en Kotlin, EPUB 2/3, publicaciones, recursos, TOC, búsqueda de texto, dirección RTL y localizadores. El repositorio declara lectura OPDS 1.2 y 2.0; esto es útil en Fase 3. El soporte del catálogo no equivale a tener resuelta la autenticación de cada servidor. [R2]

**Riesgos de integración:**

- Su navegador visual se integra como `Fragment` Android; también hay navegadores Compose documentados como alfa. No es una API que entregue automáticamente una textura Unity ni una imagen arbitraria de una página. Se necesita demostrar un puente de renderizado Android → textura y su ciclo de vida en Quest 3. [R3]
- Las vistas Android/WebView tienen afinidad con el hilo UI. Las descargas, análisis, índices y conversiones se harán fuera de él; no se prometerá trasladar toda la UI de Readium a un hilo de trabajo.
- Readium usa **localizadores y posiciones**, no páginas universales de un EPUB. El número de página de la app dependerá de un índice propio asociado a contenido, tamaño de página, fuente, cuerpo, márgenes y versión del motor. Cambiar esas opciones invalida ese índice, pero no debe perder el localizador de lectura. [R3]
- Las versiones recientes cambian requisitos de SDK, Gradle y Kotlin. Fijar una versión compatible con el Gradle que admita la versión Unity elegida; no copiar las dependencias de la rama `develop` sin comprobar esa matriz. Habilitar el desugaring exigido por la versión seleccionada. [R2]
- La disponibilidad y versión de Android System WebView en Horizon OS deben comprobarse en el dispositivo real.

**Decisión:** adoptar el modelo de publicación y evaluar su navegador. Readium LCP queda fuera: no incluir `readium-lcp`, `liblcp` ni provisión de licencias. No integrar protección Adobe como mecanismo de apertura.

### PDFium

**Licencia:** núcleo bajo licencia estilo BSD de 3 cláusulas; el archivo `LICENSE` actual también incorpora texto Apache-2.0. El binario contiene componentes de terceros con sus propios avisos. No presentar un APK completo como si tuviera una única licencia BSD. [P1]

**Ventajas:** API C pública para abrir documentos, cargar una página, renderizar a un bitmap y obtener errores. `FPDF_LoadCustomDocument` admite callbacks de lectura mediante `FPDF_FILEACCESS`, lo que permite servir rangos de un archivo persistente sin cargarlo entero en RAM. Existe render progresivo con pausas cooperativas. [P2, P3]

**Riesgos de integración:**

- **Ninguna API PDFium es thread-safe.** Upstream exige un solo hilo o exclusión mutua global para impedir llamadas concurrentes. Usar un trabajador PDF serial, compartido por todos los libros, con prioridades; no un renderizador concurrente por documento sin aislamiento. [P2]
- La compilación usa herramientas de Chromium, GN y Ninja, no únicamente un proyecto CMake pequeño. Linux es un host documentado y existe configuración Android; todavía debe probarse ARM64 con el NDK y la cadena de herramientas seleccionados. [P4]
- Un wrapper Android de terceros añade su propia licencia, mantenimiento y posible retraso en parches. Preferir API C estable y un wrapper JNI pequeño propio; si se adopta uno existente, auditarlo por separado.
- La lectura aleatoria no garantiza la latencia de un PDF dañado, no linealizado, con tablas de referencias enormes o páginas muy complejas. Deben existir límites de trabajo, cancelación cooperativa y errores explicables.
- Desactivar V8 y XFA al construir para un lector sin scripts ni formularios dinámicos, usando las opciones oficiales `pdf_enable_v8` y `pdf_enable_xfa`; no es una instrucción para modificar documentos. [P4]
- Liberar página, bitmap y documento por el mismo mecanismo de serialización; no reutilizar buffers mientras Unity los está consumiendo.

**Decisión:** candidato principal de Fase 1. Permitir contraseña introducida por el usuario y respetar permisos del documento. El código de error de contraseña no debe convertirse en un falso diagnóstico de archivo corrupto.

### libarchive

**Licencia:** principalmente BSD-2-Clause. `COPYING` enumera archivos BSD-3-Clause, dominio público y código con elección CC0/OpenSSL/Apache-2.0; los scripts de compilación y bibliotecas enlazadas tienen condiciones propias. Conservar los avisos del conjunto distribuido. [A1]

**Ventajas:** C portable, lectura por bloques, detección de formatos; upstream declara ZIP/ZIPX, RAR y RAR5. El propio README advierte de limitaciones de RAR. La existencia de un lector RAR5 se confirmó en el código oficial. [A2, A3]

**Riesgos de integración:**

- Compilar para Android ARM64 con NDK y únicamente los códecs necesarios. No se ha compilado aquí ni hay garantía de que una configuración de escritorio funcione sin ajustes.
- Su diseño principal es secuencial. Un índice de nombres no garantiza que leer la entrada número 80.000 sea inmediato. Validar el coste real de saltar entradas ZIP; si no satisface el objetivo, separar un backend ZIP con acceso indexado manteniendo libarchive para RAR.
- En RAR sólido, preparar las imágenes por streaming en caché persistente puede ser imprescindible. Se informa el progreso, se admite cancelar y se exige espacio libre suficiente. Nunca descomprimir todo en RAM. La primera visita a una página lejana puede esperar a su preparación.
- No extraer rutas absolutas, `..`, enlaces simbólicos ni enlaces duros. Limitar entradas, profundidad, dimensiones de imágenes, bytes descomprimidos y cuotas de disco; no confundir estos rechazos con DRM.
- Un archivo comprimido con contraseña no es necesariamente DRM editorial. El MVP no promete abrirlo; indicar «Archivo comprimido con contraseña aún no compatible». No intentar claves ni quitar protección.

**Decisión:** candidato para cómics, condicionado a pruebas ZIP y RAR4/RAR5. No usar una biblioteca que únicamente lea RAR4 anunciándola como soporte general CBR.

### libmobi

**Licencia:** **LGPL-3.0-or-later**, según README y COPYING. Es distinta de una licencia permisiva. La distribución exige cumplir las condiciones LGPL/GPL aplicables, proporcionar los avisos y el código correspondiente de la biblioteca y sus cambios, y permitir las operaciones de sustitución/reenlace exigidas por la licencia. El enlazado dinámico por sí solo no resuelve todas las obligaciones. Antes de distribuir, verificar cómo permitir sustituir la biblioteca en un APK firmado e incluir la información de instalación que corresponda. [M1, M2]

**Ventajas:** C99, MOBI y KF8/AZW/AZW3 declarados, reconstrucción de HTML/CSS/recursos y Android entre las plataformas listadas. [M1]

**Riesgos de integración:**

- La ruta documentada `mobi_load_file` seguida de `mobi_parse_rawml` mantiene datos y contenido reconstruido en memoria. No sirve como prueba de soporte de archivos gigantes ni de lectura incremental. Se exige investigar un backend acotado antes de integrarlo en la app. [M1]
- Upstream recomienda Autotools y avisa que sus sistemas alternativos, incluido CMake, tienen cobertura y mantenimiento menores. Preparar la compilación cruzada NDK y sus dependencias con ese hecho en cuenta. [M1]
- La biblioteca contiene funcionalidades criptográficas que este proyecto no utilizará. Su configuración oficial admite deshabilitarlas mediante `--enable-encryption=no`; no distribuir sus herramientas DRM ni exponer operaciones de descifrado DRM desde JNI. La detección previa debe seguir siendo posible y probarse en la compilación elegida. [M3]
- No declara soporte de renderizado KFX. Los libros de diccionario, diseño fijo y algunas estructuras KF8 necesitarán corpus específico.

**Decisión:** evaluación de Fase 2, bloqueada para producción hasta demostrar memoria acotada y una forma viable de cumplir LGPL. Alternativa: conversión local en el PC de archivos sin DRM a EPUB, sin enviar libros a servidores públicos.

## 4. Documentos de oficina y texto

DOCX y ODT son contenedores ZIP/XML: reconocer el ZIP no basta. Analizar las partes necesarias por streaming con entidades externas desactivadas y recursos con límites. Definir explícitamente el subconjunto de estilos y tablas admitido y avisar cuando la conversión pierda información relevante. No ejecutar macros, vínculos remotos, objetos OLE ni scripts del documento.

**Apache POI**, con licencia Apache-2.0, es una alternativa a evaluar para documentos Microsoft, no una dependencia seleccionada. Su ecosistema Java y algunas dependencias de escritorio, tamaño y consumo de memoria deben validarse en Android antes de escogerlo; no se ha comprobado su compatibilidad en Quest. No asumir que «es Java» significa «funciona en Android». [O1, O2]

Para DOC binario no se promete un parser casero completo. El camino de reserva será LibreOffice en Pop!_OS: abrir el archivo, comprobarlo visualmente y exportarlo a PDF para preservar maquetación o guardarlo como DOCX para una conversión de texto. Esta alternativa no cumple por sí sola soporte DOC nativo; se registrará como limitación visible de Fase 2. No se empaquetará LibreOffice dentro de la app por esta decisión.

TXT necesita un lector de bloques que preserve caracteres multibyte partidos entre bloques, detecte saltos de línea y guarde offsets de bytes junto con puntos de reinicio de decodificación. Prioridad: BOM → UTF-8 válido → heurística UTF-16 → codificación heredada propuesta. Si el resultado es ambiguo, permitir elegir la codificación. La elección se guarda por libro y se usa al regenerar su índice. Pruebas obligatorias: `áéíóúüñ¿¡`, UTF-16 con y sin BOM, Windows-1252, Latin-1 y secuencias inválidas.

RTF necesita respetar anidación y escapado, además de ignorar destinos no soportados de forma controlada. Una expresión regular que elimine llaves no es un conversor aceptable.

## 5. Clasificación de protección y errores

El detector devuelve formato, variante, evidencias, protección conocida y motivo de fallo por separado. La interfaz muestra solo lo que se haya podido establecer. Los nombres siguientes describen estados de diseño; no son APIs existentes.

| Evidencia o situación | Conducta y texto de la app |
| --- | --- |
| Indicadores coherentes de DRM Kindle en un contenedor reconocido | «Este libro tiene DRM de Kindle y no puede abrirse en esta app. Consigue una edición sin DRM en un formato compatible». Ofrecer la guía de importación autorizada; nunca retirar ni eludir DRM. |
| Evidencia de DRM Adobe en EPUB/PDF | «Este libro tiene DRM de Adobe y no puede abrirse en esta app. Consigue una edición sin DRM o ábrelo en un lector autorizado». No deducirlo por proceder de Kobo, Google Play Libros o una biblioteca. |
| Archivo ACSM reconocido | Explicar que es un archivo de licencia/descarga, no el libro EPUB/PDF. No intentar convertirlo en contenido ni anunciar que la app maneja su DRM. |
| PDF con contraseña estándar requerida o incorrecta | «Este PDF necesita una contraseña». Pedirla al usuario; si falla, «La contraseña no es correcta». No registrar la contraseña ni guardarla por defecto. |
| Manejador de seguridad no compatible, sin poder identificar el proveedor | «El mecanismo de protección de este archivo no es compatible». No atribuirlo a Adobe o Kindle sin evidencia suficiente. |
| Marca de agua social o visible | Permitir abrir el libro si el contenido es legible y compatible; conservar la marca. No prometer detectar todas las marcas de agua ni tratarlas como bloqueo DRM. |
| Contenedor/variante reconocida, pero parser o renderizador no implementado | «Formato aún no compatible». Para KFX sin un diagnóstico fiable de protección, este es el resultado normal. |
| Bytes recibidos inferiores al tamaño anunciado o falta comprobada de partes | «La descarga está incompleta. Vuelve a descargar el archivo». Mantener opción Reintentar y progreso de importación. |
| Estructura/CRC/hash falla y no hay evidencia de truncamiento | «El archivo parece dañado. Prueba a descargarlo otra vez». No afirmar que falló la red ni que tiene DRM. |
| Causa no determinada | «No pudimos abrir este archivo y todavía no sabemos la causa». Ofrecer Reintentar y exportar un registro sin claves ni contenido del libro. |

**EPUB merece una excepción importante:** `META-INF/encryption.xml` puede describir únicamente ofuscación de fuentes. Su presencia no demuestra DRM del libro. Inspeccionar algoritmos, recursos afectados y metadatos; admitir la ofuscación de fuentes definida por EPUB sin etiquetarla como DRM editorial. La especificación oficial distingue explícitamente ofuscación y cifrado. [E1]

Un PDF cifrado tampoco implica DRM Adobe. Usar el resultado del motor y el manejador de seguridad. PDFium distingue contraseña requerida/incorrecta (`FPDF_ERR_PASSWORD`) de mecanismo no admitido (`FPDF_ERR_SECURITY`). Respetar los permisos de uso del documento; no explotar permisos de propietario ni convertirlo para eliminarlos. [P2]

LCP queda fuera del alcance de todas las fases actuales. Si se reconoce una publicación LCP, se informa protección no compatible. No se sustituye ese rechazo por una ruta de descifrado.

## 6. Criterios de aceptación futuros

Estos casos pertenecen a Fases 1–4; **no se han ejecutado en Fase 0**:

1. El mismo archivo abre igual al renombrar su extensión; un archivo engañoso no se envía ciegamente al parser sugerido por su nombre.
2. PDF con contraseña correcta/incorrecta, PDF no cifrado, mecanismo de seguridad no admitido, EPUB con ofuscación de fuentes, libro con marca de agua, variante DRM conocida y archivo de origen desconocido producen mensajes distintos.
3. CBZ ZIP64, RAR4, RAR5, RAR sólido, variantes no admitidas y contenedores truncados tienen pruebas independientes.
4. Una página o imagen gigante no fuerza una textura sin límites. Se comprueba consumo CPU y GPU, cancelación, cierre de libros y recuperación tras presión de memoria.
5. La apertura y cambio de página de tres libros comparten presupuestos y prioridades. PDFium no recibe llamadas concurrentes.
6. Cambiar tipografía y márgenes regenera el índice sin perder progreso, marcadores ni TOC. La búsqueda es incremental y cancelable.
7. La ruta aprobada de cada formato demuestra memoria acotada; un ejemplo pequeño que abre no justifica declarar soporte de 100.000 páginas.
8. APK y bibliotecas incluyen inventario de dependencias fijadas, licencias, avisos, procedencia y hashes verificables; las dependencias LGPL tienen un procedimiento probado de cumplimiento.

## 7. Fuentes primarias consultadas

Consulta HTTPS realizada el 5 de noviembre de 2026. Se leyeron los documentos de los repositorios oficiales; sus ramas pueden cambiar. En la fase de integración se fijarán etiquetas o commits y se repetirá la revisión. Aquí no se afirma que estas dependencias ya estén instaladas o compiladas.

- **R1:** [Licencia Readium Kotlin](https://github.com/readium/kotlin-toolkit/blob/develop/LICENSE).
- **R2:** [README Readium: formatos, OPDS y requisitos](https://github.com/readium/kotlin-toolkit/blob/develop/README.md).
- **R3:** [Navegadores Readium: integración Android y posiciones](https://github.com/readium/kotlin-toolkit/blob/develop/docs/guides/navigator/navigator.md); [nuevos navegadores Web](https://github.com/readium/kotlin-toolkit/blob/develop/docs/guides/navigator/web-navigators.md).
- **P1:** [Licencia PDFium, espejo oficial Chromium](https://github.com/chromium/pdfium/blob/main/LICENSE).
- **P2:** [API pública PDFium y reglas de concurrencia](https://github.com/chromium/pdfium/blob/main/public/fpdfview.h).
- **P3:** [Render progresivo PDFium](https://github.com/chromium/pdfium/blob/main/public/fpdf_progressive.h).
- **P4:** [Compilación PDFium y opciones V8/XFA](https://github.com/chromium/pdfium/blob/main/README.md).
- **A1:** [Licencias libarchive](https://github.com/libarchive/libarchive/blob/master/COPYING).
- **A2:** [Formatos y diseño de libarchive](https://github.com/libarchive/libarchive/blob/master/README.md).
- **A3:** [Lector RAR5 oficial](https://github.com/libarchive/libarchive/blob/master/libarchive/archive_read_support_format_rar5.c).
- **M1:** [libmobi: formatos, memoria, plataformas y licencia](https://github.com/bfabiszewski/libmobi/blob/master/README.md).
- **M2:** [LGPL de libmobi](https://github.com/bfabiszewski/libmobi/blob/master/COPYING).
- **M3:** [Opciones de compilación libmobi](https://github.com/bfabiszewski/libmobi/blob/master/configure.ac).
- **O1:** [Proyecto Apache POI](https://github.com/apache/poi/blob/trunk/README.md).
- **O2:** [Licencias Apache POI](https://github.com/apache/poi/blob/trunk/legal/LICENSE).
- **E1:** [Especificación EPUB 3.3 en el repositorio oficial W3C](https://github.com/w3c/epub-specs/blob/main/epub33/core/index.html), secciones sobre `encryption.xml` y ofuscación de fuentes. La web `www.w3.org` no fue accesible desde este entorno; se consultó la fuente oficial en GitHub.
