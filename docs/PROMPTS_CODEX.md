# Prompts para terminar Folio XR con Codex (GPT-6 Astra)

Fecha: 9 de noviembre de 2026. Escritos por Claude a pedido del usuario. Esta es la lista de todos los encargos que faltan para que la app quede lista para venderse en la Meta Horizon Store.

## Cómo se usan

1. **Encabezado.** Cada prompt empieza con el **encabezado común** de abajo, reemplazando `{RAMA}`.
2. **Modelo y esfuerzo.** Se usa **GPT-6 Astra con esfuerzo Alto**; Ultra agota el cupo del plan Plus muy rápido. Ojo: los chats nuevos parten en «GPT-6.1 Sol Bajo» y hay que cambiarlos.
3. **Orden.** Van uno a la vez, en el orden de la lista. Claude revisa e integra cada entrega en `fase-1/preparacion` antes del siguiente.
4. **Pruebas.** Donde dice **🧪 Prueba en el visor**, el usuario compila una vez y prueba todo lo acumulado.
5. **Cupo.** Si a Codex se le acaba el cupo, se espera a que se renueve y se le pide «continúa donde quedaste, commit y push».
6. **Ajustes.** Los prompts se pueden ajustar según lo que salga en cada prueba. Las reglas que valen para todos están en [REGLAS_CODEX.md](REGLAS_CODEX.md).

### Encabezado común

```text
Responde siempre en español. Repositorio: https://github.com/loklj/el-m-s-perr-n-aqu- (clónalo si no está). Proyecto "Folio XR": lector de libros 3D para Meta Quest 3 (Unity 6000.3 + URP 17 + Meta XR SDK 207, OpenXR). Antes de empezar lee PROGRESO.md y docs/REGLAS_CODEX.md y cúmplelas. Parte de: git fetch origin && git checkout -B {RAMA} origin/fase-1/preparacion.
```

## Índice

| # | Encargo | Rama | Estado |
| --- | --- | --- | --- |
| 1 | Tanda C: reloj de herramientas y linterna triple | `interaccion/tanda-c` | En curso (enviado el 9 de noviembre de 2026) |
| 2 | Tanda D: lápices, tinta invisible y libreta | `interaccion/tanda-d` | Pendiente |
| 3 | Tanda E: lupa y regla | `interaccion/tanda-e` | Pendiente |
| | 🧪 Prueba en el visor: herramientas, movimiento y libro | | |
| 4 | Menú esférico | `menus/esfera-1` | Pendiente |
| 5 | Biblioteca persistente e importación local | `fase1/biblioteca` | Pendiente |
| 6 | Formatos base: PDF robusto y CBZ/ZIP | `fase1/formatos-base` | Pendiente |
| 7 | Hito 2: EPUB en el libro 3D (WebView + Readium) | `hito2/epub` | Necesita la autorización del usuario |
| 8 | Animación de páginas, tres libros y cómics | `fase1/tres-libros` | Pendiente |
| 9 | Estabilidad: recentrar, reanudar y seguimiento | `fase1/estabilidad` | Pendiente |
| | 🧪 Prueba en el visor: MVP de la Fase 1 | | |
| 10 | Formatos 2: TXT, CBR, MOBI/AZW/AZW3 | `fase2/formatos` | Pendiente |
| 11 | Formatos 3: DOCX, ODT, RTF y diagnóstico de no compatibles | `fase2/documentos` | Pendiente |
| 12 | Carga desde el PC por Wi-Fi | `fase3/wifi-pc` | Pendiente |
| 13 | Nubes: Google Drive, Dropbox y MEGA | `fase3/nubes` | Pendiente; antes hay que decidir el permiso de Drive |
| 14 | OPDS (Calibre-Web, Kavita, Komga) | `fase3/opds` | Pendiente |
| | 🧪 Prueba en el visor: formatos e importación | | |
| 15 | Archivos gigantes y rendimiento | `fase4/rendimiento` | Pendiente |
| 16 | Los otros 19 entornos | `entornos/19-temas` | Pendiente |
| 17 | Pulido de lectura: modo noche, sepia, fuentes, búsqueda e índice | `fase5/lectura` | Pendiente |
| 18 | Estantería 3D, carpetas y accesibilidad | `fase5/estanteria` | Pendiente |
| | 🧪 Prueba en el visor: versión candidata | | |
| 19 | Preparación para la tienda: licencias, privacidad, VRC y build de lanzamiento | `tienda/lanzamiento` | Pendiente |
| 20 | Correcciones finales tras la prueba completa | `tienda/correcciones` | Se escribe con el resultado de la prueba |

---

## 1. Tanda C: reloj de herramientas y linterna triple (en curso)

Ya se envió a Codex el 9 de noviembre de 2026. Especificación en `docs/INTERACCION.md`, sección «Tanda C».

## 2. Tanda D: lápices, tinta invisible y libreta

```text
[ENCABEZADO con {RAMA} = interaccion/tanda-d]
Lee docs/INTERACCION.md y haz SOLO la "Tanda D — Lápices de colores, tinta invisible y libreta". Lee también la sección "Estado de la Tanda C" y los scripts de Scripts/Tools/ (infraestructura de herramientas, reloj de la muñeca y UltravioletLight o la API equivalente de la linterna), Reading/FolioBook.cs y Reading/BookGrab.cs.
TAREA:
(1) Lápices de colores como herramienta del reloj: 6 colores (negro, azul, rojo, verde, morado y resaltador amarillo semitransparente). Se cambia de color tocando el extremo del lápiz con el índice de la otra mano o con A/X si está en un mando. Tiene goma en la punta contraria, que borra los trazos que toca.
(2) Se escribe en CUALQUIER superficie con colisionador (casa, libro, libreta, regla), nunca en el aire. Cuando la punta queda a menos de 5 mm, el trazo se pega a la superficie con un desplazamiento de 1 mm según la normal. Los trazos son mallas de cintas que se amplían de a poco, sin rehacerlas en cada cuadro, con un máximo de puntos por trazo y trazos combinados al terminarlos.
(3) Si la punta pasa a menos de 1 cm del borde de la regla (llegará en la Tanda E), el trazo sale recto, alineado con el borde. Deja el gancho listo: una interfaz IStraightEdge que la regla implementará.
(4) Persistencia en JSON dentro de persistentDataPath, con escritura atómica (archivo temporal + reemplazo):
- En el libro: por identificador del PDF y por página, en coordenadas de la página. Reaparecen al volver a esa página y siguen la escala del libro.
- En la libreta: por página.
- En la casa: por entorno, en coordenadas de la raíz de la casa.
(5) Tinta invisible: pluma aparte en el reloj. Sus trazos solo se ven dentro del cono ultravioleta de la linterna encendida, a menos de 3 m. Por CPU, actualiza solo los trazos cercanos al cono y como mucho 10 veces por segundo.
(6) Libreta: herramienta del reloj, tamaño A5, 20 páginas. Se pasa de página tocando las esquinas con el índice. Se puede agarrar, dejar en cualquier parte y tiene física igual que el libro. Las páginas se dibujan con una textura de líneas generada por código.
(7) En el menú, en Ajustes: "Borrar trazos de esta página" y "Borrar trazos de la casa", con confirmación (segundo toque).
Rendimiento: 72 FPS con 2.000 trazos guardados; registra en el log la cantidad de trazos y vértices al cargar.
Agrega filas a la tabla del Paso 10 de docs/HITO1_GUIA.md y una sección "Estado de la Tanda D" en docs/INTERACCION.md. Commit en interaccion/tanda-d y push. Responde con un resumen, archivos y riesgos.
```

## 3. Tanda E: lupa y regla

```text
[ENCABEZADO con {RAMA} = interaccion/tanda-e]
Lee docs/INTERACCION.md y haz SOLO la "Tanda E — Lupa y regla". Lee los Estados de las Tandas C y D y los scripts de Scripts/Tools/.
TAREA:
(1) Lupa como herramienta del reloj: lente de 8 cm con mango.
- Muestra ampliado 2,5× lo que hay detrás, con una Camera secundaria que renderiza a una RenderTexture de 512 px, alineada desde el ojo a través del centro de la lente.
- Solo renderiza mientras está en la mano o a menos de 1 m del libro, y como mucho a 36 Hz si hace falta para mantener 72 FPS.
- Aumento de 1,5× a 4×: con el stick del mando que la sostiene, o tocando los botones + y − de la lupa con el índice de la otra mano.
- La cámara secundaria no renderiza las capas de UI del menú ni el reloj.
(2) Regla como herramienta del reloj: 30 cm, con marcas cada 1 mm y números cada 1 cm en una textura generada por código.
- Tiene física (Realista/Flotante como el libro), se puede apoyar sobre el libro y sirve de guía para leer línea por línea.
- Implementa IStraightEdge de la Tanda D, para que el lápiz trace rectas junto a su borde.
(3) Opcional, si no complica: al tocar dos puntos con la punta del lápiz junto a la regla, muestra la distancia en cm unos segundos.
Agrega filas a la tabla del Paso 10 de docs/HITO1_GUIA.md y una sección "Estado de la Tanda E" en docs/INTERACCION.md. Commit en interaccion/tanda-e y push. Responde con un resumen, archivos y riesgos.
```

> 🧪 **Prueba en el visor:** herramientas, movimiento y libro (Tandas A a E). El usuario compila una vez y Claude lee el registro.

## 4. Menú esférico

```text
[ENCABEZADO con {RAMA} = menus/esfera-1]
Lee docs/MENU_ESFERICO.md (especificación completa), docs/MENU_SIMPLE.md y los scripts de Scripts/Menu/ (MenuModel, MenuController, PanelMenuView y MenuHandInput).
TAREA: programa la vista esférica (SphereMenuView) sobre el MISMO MenuModel, tal como describe MENU_ESFERICO.md: esfera azul anclada a la mano o mando izquierdo, giro con el índice derecho con inercia y ajuste a cada categoría, stick derecho por pasos, segundo anillo para las opciones, "Volver", clic y vibración. Usa el mismo gesto y botón para abrir. En Ajustes, "Estilo de menú: Esfera / Simple" pasa a funcionar de verdad (PlayerPrefs folioxr.menuStyle); la esfera queda por defecto, como pide el documento.
Mientras la esfera esté abierta, los controles del libro y el movimiento quedan en pausa, igual que con el panel. Agrega las opciones nuevas de las Tandas A a E que ya existan en el panel (física, tamaño, guardar herramientas, borrar trazos, viñeta y giro).
Máximo 10 draw calls para la esfera; textos que siempre quepan en sus casillas.
Actualiza docs/MENU_ESFERICO.md con una sección "Estado" y agrega filas al Paso 10 de docs/HITO1_GUIA.md. Commit en menus/esfera-1 y push. Responde con un resumen, archivos y riesgos.
```

## 5. Biblioteca persistente e importación local

```text
[ENCABEZADO con {RAMA} = fase1/biblioteca]
Lee PROGRESO.md (Fase 1, paso 3; Decisiones registradas; Mensajes de error requeridos), docs/ARQUITECTURA.md, docs/IMPORTACION_Y_NUBES.md (archivos locales) y Reading/FolioBook.cs y PdfPageRenderer.cs.
TAREA:
(1) Biblioteca persistente en Android con Room (AndroidX, Apache-2.0), en un plugin Java dentro de hito1/Assets/Plugins/Android con su puente a C#. Si Room complica el build de Unity, usa SQLite del sistema (android.database.sqlite) y explica por qué.
Datos por libro: id estable (hash parcial + tamaño + fecha), título, autor, formato, ruta de la copia privada, portada en miniatura, progreso (página o posición), marcadores, fecha de última lectura, estado de importación y tamaño.
(2) Importación local con el selector del sistema (SAF, ACTION_OPEN_DOCUMENT, múltiples archivos):
- Copia privada transaccional por streaming: archivo temporal, verificación de tamaño, renombrado y recién ahí el registro en la base.
- Barra de progreso y cancelación.
- Comprobar el espacio libre antes de copiar.
- Recuperación de importaciones interrumpidas al abrir la app.
- Sin cargar archivos completos en memoria: deben funcionar con archivos de 20 GB.
Si en Horizon OS el selector no aparece o no devuelve archivos, regístralo claramente en el log y muestra un mensaje que sugiera la carga desde el PC (llega en otro encargo).
(3) Categoría Biblioteca del menú: lista de libros con portada, título y progreso; tocar uno lo abre en el libro 3D; opciones "Importar libros", "Quitar de la biblioteca" (con confirmación) y "Marcadores".
(4) Progreso: guardar la página al pasarla (con debounce) y al pausar la app; al abrir un libro, volver a su última página. Los marcadores se agregan desde el menú.
(5) Respaldo versionado básico de la base (copia con fecha, máximo 5) y migraciones de esquema.
(6) Mensajes de error específicos según la tabla "Mensajes de error requeridos" de PROGRESO.md (archivo dañado, DRM detectado, contraseña requerida, formato aún no compatible, falta de espacio, etc.).
Prueba obligatoria descrita en la guía: importar, leer, cerrar la app, reiniciar el visor, actualizar el APK (adb install -r) y comprobar que la biblioteca y el progreso siguen ahí.
Crea docs/BIBLIOTECA.md con el diseño y una sección "Estado", y agrega filas al Paso 10 de docs/HITO1_GUIA.md. Commit en fase1/biblioteca y push. Responde con un resumen, archivos y riesgos.
```

## 6. Formatos base: PDF robusto y CBZ/ZIP

```text
[ENCABEZADO con {RAMA} = fase1/formatos-base]
Lee PROGRESO.md (Fase 1, paso 4; Archivos de varios GB; Riesgos), docs/FORMATOS_Y_LICENCIAS.md, docs/LICENCIAS_Y_TIENDA.md, docs/BIBLIOTECA.md y Reading/PdfPageRenderer.cs + Plugins/Android/FolioPdfRenderer.java.
TAREA:
(1) PDF:
- Mantén el PdfRenderer de Android, salvo que midas un motivo para cambiarlo. Si propones PDFium, solo con un binario de licencia permisiva (BSD/Apache) y anotado en el inventario.
- Quita el PNG intermedio: pasa los píxeles del Bitmap a Unity sin codificar (ByteBuffer → Texture2D.LoadRawTextureData) para bajar la latencia.
- Caché LRU de páginas renderizadas con límite de memoria, y precarga de la doble página siguiente y la anterior.
- Apertura cancelable.
- Errores específicos: PDF con contraseña, dañado y página demasiado grande, con renderizado reducido en vez de cerrarse.
- Debe abrir PDF de 20 GB y más de 100.000 páginas sin leer el archivo entero.
(2) CBZ/ZIP de imágenes:
- Lector propio con java.util.zip o ZipFile del sistema, con acceso aleatorio sin descomprimir todo.
- Orden natural de los nombres de archivo.
- Decodificación con BitmapFactory e inSampleSize según el tamaño de la página, con límite de memoria.
- Portada para la biblioteca.
(3) Detección del formato por contenido (firma de bytes), no por extensión.
(4) Portadas en miniatura para la biblioteca.
Pruebas con tests/fixtures y tools/test-data/gigantes.py (CBZ y PDF de estrés): documenta en la guía cómo copiarlos al visor y qué medir.
Agrega una sección "Estado" en docs/FORMATOS_Y_LICENCIAS.md y filas al Paso 10. Commit en fase1/formatos-base y push. Responde con un resumen, archivos y riesgos.
```

## 7. Hito 2: EPUB en el libro 3D (WebView + Readium)

> ⚠️ El Hito 2 **necesita la autorización explícita del usuario** antes de enviarse (regla vigente del proyecto).

```text
[ENCABEZADO con {RAMA} = hito2/epub]
Lee docs/HITO2_ENFOQUE.md (decisión: opción A, WebView del sistema; Readium para la estructura del EPUB), docs/FORMATOS_Y_LICENCIAS.md, docs/LICENCIAS_Y_TIENDA.md, docs/BIBLIOTECA.md y los scripts de Reading/.
TAREA: EPUB sin DRM en el libro 3D.
(1) Plugin Android: un WebView fuera de pantalla que carga cada capítulo y lo pagina con columnas CSS al tamaño de una página del libro. Captura cada página a Bitmap (enableSlowWholeDocumentDraw donde haga falta) y la pasa a Unity sin PNG intermedio, igual que en el encargo 6. Todo en el hilo de UI de Android, con una cola y cancelación.
(2) Readium Kotlin Toolkit (BSD-3) para leer el contenedor EPUB, el spine, los metadatos, el índice (TOC) y los recursos. Anota la versión en el inventario de licencias. Si integrarlo con Unity es inviable, documenta por qué y usa un lector propio del contenedor (ZIP + OPF + NCX/nav) con java.util.zip.
(3) Paginación con tamaño de letra ajustable (Ajustes → "Tamaño de letra") y nueva paginación al cambiarlo. El progreso se guarda como posición de Readium (locator) en la biblioteca.
(4) Fuente: una fuente libre incluida (SIL OFL u otra permisiva, anotada en el inventario), con la del libro como alternativa.
(5) Caché de páginas capturadas con límite de memoria y precarga de la siguiente doble página. Capítulos enormes (prueba con el EPUB de estrés de tools/test-data/gigantes.py).
(6) EPUB con DRM (encryption.xml con algoritmos de DRM): mensaje "Este libro tiene DRM y no se puede abrir". Nunca intentes quitarlo.
Meta: texto nítido de 12 pt a 45 cm con la nitidez 1,4. Mide el tiempo de captura por página y regístralo en el log.
Agrega una sección "Estado" en docs/HITO2_ENFOQUE.md y filas al Paso 10. Commit en hito2/epub y push. Responde con un resumen, archivos y riesgos.
```

## 8. Animación de páginas, tres libros y cómics

```text
[ENCABEZADO con {RAMA} = fase1/tres-libros]
Lee PROGRESO.md (Fase 1, paso 5; Presión de memoria con tres libros), docs/BIBLIOTECA.md y los scripts de Reading/.
TAREA:
(1) Animación de pasar página: la hoja se curva (malla deformada por código, sin física) en unos 0,35 s, con sonido corto generado por código. Con manos: pellizcar la esquina y arrastrar permite pasar la página a mano (si se suelta antes de la mitad, vuelve).
(2) Tres libros abiertos a la vez, desde la biblioteca: "Abrir otro libro". Cada uno con su agarre, física, escala y progreso. Presupuesto de memoria global de texturas compartido entre los tres, con LRU y resolución adaptable al tamaño en pantalla; regístralo en el log.
(3) Cómics y manga: modo de lectura de derecha a izquierda (manga) por libro, doble página con portada sola y ajuste de página completa.
(4) "Cerrar libro" en el menú y en un gesto simple (definirlo y documentarlo).
Agrega filas al Paso 10 y una sección "Estado" en docs/BIBLIOTECA.md. Commit en fase1/tres-libros y push. Responde con un resumen, archivos y riesgos.
```

## 9. Estabilidad: recentrar, reanudar y seguimiento

```text
[ENCABEZADO con {RAMA} = fase1/estabilidad]
Lee PROGRESO.md (Fase 1, paso 6; Pérdida de archivo o progreso por suspensión/cierre).
TAREA:
(1) Posturas: sentado, de pie y acostado. Opción "Modo acostado" en Ajustes: el libro y el menú se orientan hacia la mirada aunque se mire al techo, y el movimiento libre se desactiva.
(2) Recentrar del sistema (botón Meta mantenido): los libros, el menú y las herramientas vuelven a una posición cómoda sin saltos raros.
(3) Suspender y reanudar (quitarse el visor, menú del sistema, apagar la pantalla): se guarda el progreso, ningún libro sale lanzado ni se cae al reanudar, y las herramientas siguen en su lugar.
(4) Pérdida de seguimiento de manos o mandos durante un agarre o un trazo: se suelta de forma segura, sin lanzar objetos.
(5) Pruebas de interrupción documentadas en la guía: cerrar la app a mitad de una importación, reiniciar el visor y quedarse sin espacio.
Agrega filas al Paso 10 y una sección "Estabilidad" en docs/ARQUITECTURA.md. Commit en fase1/estabilidad y push. Responde con un resumen, archivos y riesgos.
```

> 🧪 **Prueba en el visor:** MVP de la Fase 1 (biblioteca, PDF, CBZ, EPUB, tres libros y estabilidad).

## 10. Formatos 2: TXT, CBR, MOBI/AZW/AZW3

```text
[ENCABEZADO con {RAMA} = fase2/formatos]
Lee PROGRESO.md (Fase 2), docs/FORMATOS_Y_LICENCIAS.md y docs/LICENCIAS_Y_TIENDA.md (libmobi está descartado por LGPL; MOBI con un parser propio; CBR con libarchive o UnRAR según su licencia), y el encargo de EPUB ya integrado.
TAREA:
(1) TXT: detección de codificación (UTF-8, UTF-16 con y sin BOM, Windows-1252, ISO-8859-1), con corrección manual en Ajustes. Se pagina con el mismo motor de páginas del EPUB (HTML generado) y funciona con TXT de cientos de MB sin cargarlos enteros.
(2) CBR (RAR4/RAR5): usa una biblioteca de licencia compatible con código cerrado (libarchive, BSD, compilada para Android ARM64, o UnRAR con su licencia). Documenta la elección en el inventario. Detecta los RAR sólidos: extráelos a disco con una cuota y progreso, o explica el límite.
(3) MOBI/AZW/AZW3 sin DRM con un PARSER PROPIO:
- Encabezados PalmDB/MOBI/EXTH, descompresión PalmDOC y HUFF/CDIC, KF8 (AZW3) a HTML.
- Se muestran con el motor del EPUB y se lee por registros, sin cargar el libro entero.
- DRM detectado: mensaje claro, nunca quitarlo.
(4) Marca cada formato como soportado, experimental o no compatible, en docs/FORMATOS_Y_LICENCIAS.md, con un corpus de prueba en tests/fixtures generado o con licencia libre.
Agrega filas al Paso 10. Commit en fase2/formatos y push. Responde con un resumen, archivos y riesgos.
```

## 11. Formatos 3: DOCX, ODT, RTF y diagnóstico de no compatibles

```text
[ENCABEZADO con {RAMA} = fase2/documentos]
Lee PROGRESO.md (Fase 2) y docs/FORMATOS_Y_LICENCIAS.md.
TAREA:
(1) DOCX con java-mammoth (BSD-2) convertido a HTML y mostrado con el motor del EPUB.
(2) ODT (ZIP + content.xml) y RTF con conversores propios simples a HTML: texto, títulos, cursivas, negritas, listas, tablas simples e imágenes.
(3) DOC binario, KFX y formatos desconocidos: diagnóstico explícito ("formato aún no compatible", con la razón), sin prometer soporte.
(4) Corpus de prueba válido y dañado en tests/fixtures.
Actualiza la tabla de formatos y agrega filas al Paso 10. Commit en fase2/documentos y push. Responde con un resumen, archivos y riesgos.
```

## 12. Carga desde el PC por Wi-Fi

```text
[ENCABEZADO con {RAMA} = fase3/wifi-pc]
Lee docs/IMPORTACION_Y_NUBES.md (Archivos locales y PC por Wi-Fi) y docs/BIBLIOTECA.md.
TAREA: Importar → "Desde el PC por Wi-Fi".
(1) La app levanta un servidor HTTP local solo mientras esa pantalla está abierta, con un código de 6 dígitos que se muestra en el visor y la URL http://IP:puerto.
(2) Página web mínima generada por código, para subir varios archivos con barra de progreso.
(3) Subida por streaming a la copia privada transaccional (como la importación local), con un límite de tamaño configurable y espacio comprobado antes.
(4) Seguridad: solo red local, el código es obligatorio, se cierra al salir de la pantalla y no expone otros archivos.
(5) Funciona con archivos de 20 GB (subida por partes o con reanudación si es viable).
Agrega una sección "Estado" y filas al Paso 10. Commit en fase3/wifi-pc y push. Responde con un resumen, archivos y riesgos.
```

## 13. Nubes: Google Drive, Dropbox y MEGA

> ⚠️ Antes de enviarlo, el usuario debe decidir el permiso de Google Drive: `drive.file` con Google Picker (sin la evaluación CASA) o `drive.readonly` (exige verificación de Google y una evaluación anual pagada). Ver PROGRESO.md, «Publicación comercial». Además, el usuario tiene que crear las apps de desarrollador de Google, Dropbox y MEGA con su cuenta. Codex **no** debe guardar credenciales.

```text
[ENCABEZADO con {RAMA} = fase3/nubes]
Lee docs/IMPORTACION_Y_NUBES.md, PROGRESO.md (Fase 3 y Publicación comercial) y docs/LICENCIAS_Y_TIENDA.md. Decisión del usuario sobre Drive: {drive.file con Picker | drive.readonly}.
TAREA: conectores detrás de un contrato propio (ICloudSource: listar carpetas, buscar, descargar con progreso, cancelar y reanudar).
(1) Google Drive: OAuth 2.0 con PKCE abriendo el navegador del sistema del Quest y un redirect admitido por Google para apps Android. Primero haz un prototipo que confirme que el flujo funciona en Horizon OS y registra el resultado. No uses un flujo de TV/dispositivo que no permita explorar los archivos del usuario.
(2) Dropbox: OAuth PKCE (sin client secret) y API v2 por HTTP.
(3) MEGA: SDK oficial (BSD-2) compilado SIN FreeImage ni FFmpeg. Si compilarlo para Android ARM64 resulta inviable, documenta la alternativa.
(4) Descargas directo a la copia privada transaccional, en streaming, con reanudación por Range si el servidor lo permite, detección de archivo incompleto y lectura sin conexión después.
(5) Tokens guardados con Android Keystore (EncryptedSharedPreferences o equivalente permitido). Los client IDs van en un archivo local ignorado por git con plantilla .example.
(6) Pruebas documentadas: cortar el Wi-Fi a mitad de descarga, token vencido o revocado, disco lleno y archivo cambiado en el servidor.
Agrega una sección "Estado" en docs/IMPORTACION_Y_NUBES.md y filas al Paso 10. Commit en fase3/nubes y push. Responde con un resumen, archivos y riesgos.
```

## 14. OPDS (Calibre-Web, Kavita, Komga)

```text
[ENCABEZADO con {RAMA} = fase3/opds]
Lee docs/IMPORTACION_Y_NUBES.md (OPDS) y el contrato ICloudSource del encargo de nubes.
TAREA: cliente OPDS 1.2 y 2.0:
- Agregar un servidor (URL, usuario y contraseña opcionales, guardados con Keystore).
- Navegar catálogos con paginación y buscar (OpenSearch).
- Descargar los enlaces de adquisición compatibles (EPUB, PDF, CBZ y CBR sin DRM) con progreso, hacia la biblioteca.
Pruébalo con las respuestas de ejemplo de Calibre-Web, Kavita y Komga guardadas en tests/fixtures/opds. Si una adquisición viene con DRM (por ejemplo, LCP o Adobe), muestra el mensaje y no la descargues como legible.
Agrega una sección "Estado" y filas al Paso 10. Commit en fase3/opds y push. Responde con un resumen, archivos y riesgos.
```

> 🧪 **Prueba en el visor:** formatos e importación (local, PC, nubes y OPDS).

## 15. Archivos gigantes y rendimiento

```text
[ENCABEZADO con {RAMA} = fase4/rendimiento]
Lee PROGRESO.md (Fase 4 y Archivos de varios GB) y tools/test-data/gigantes.py.
TAREA:
(1) Amplía el generador: PDF de 100.001 páginas, TXT de cientos de MB, CBZ con miles de imágenes, RAR sólido y EPUB con capítulo enorme. Debe usar semillas, generar en streaming con límite de espacio, guardar un manifiesto con hashes y no subir nada pesado al repositorio.
(2) Medición dentro de la app (activable en Ajustes → "Modo diagnóstico"), registrada en el log y exportable a un CSV en la carpeta de la app:
- Apertura en frío y en caliente, primera página y cambio de página precargada o no.
- Saltos aleatorios, uso de memoria (PSS con Debug.getMemoryInfo y memoria gráfica estimada) y FPS / tiempo de cuadro.
- Medianas y percentiles p95 y p99.
(3) Aplica límites de memoria globales y degradación controlada (menos resolución y menos precarga) cuando la memoria se acerque al presupuesto.
Metas: primera página en menos de 3 s desde un archivo local, y cambio de página precargada en menos de 100 ms, sin bajar de 72 FPS.
Crea docs/RENDIMIENTO.md con cómo medir y una tabla para los resultados reales del visor. Commit en fase4/rendimiento y push. Responde con un resumen, archivos y riesgos.
```

## 16. Los otros 19 entornos

```text
[ENCABEZADO con {RAMA} = entornos/19-temas]
Lee docs/ENTORNOS.md, PROGRESO.md (Entornos virtuales: 20 casas de 2 pisos con terraza, biblioteca, balcón, sala de estar y dormitorio) y Environments/ (EnvironmentTheme, HouseGenerator).
TAREA:
(1) Agrega 19 temas como DATOS en EnvironmentThemes. Cada uno con paleta, medidas, densidad de muebles, semilla, exterior y cielo/hora del día propios. Estilos: cabaña de montaña, casa de playa, loft industrial, casa japonesa, villa mediterránea, casa victoriana, casa nórdica, granja rústica, casa colonial, casa del desierto, casa en el bosque, casa futurista, casa en el lago, casa tropical, casa de piedra en la nieve, casa de campo inglesa, casa de ciudad nocturna, casa de otoño y casa en la isla.
(2) Si un estilo lo necesita, agrega VARIANTES DE DISTRIBUCIÓN al generador por parámetros (forma de la planta, posición de la escalera y de la terraza, techo inclinado o plano), siempre con los 5 espacios pedidos, puntos de lectura, colisionadores y pisos de teletransporte.
(3) En el menú, en Entornos: la lista de los 20, con el actual marcado. Al cambiar, se genera el nuevo y se libera el anterior.
(4) Presupuesto por entorno: 60 draw calls, 150.000 triángulos y 8 materiales. Genera los 20 en una prueba del editor (menú Folio XR → "Comprobar entornos") que registre triángulos, mallas y materiales de cada uno.
Actualiza docs/ENTORNOS.md con la lista y el estado, y agrega filas al Paso 10. Commit en entornos/19-temas y push. Responde con un resumen, archivos y riesgos.
```

## 17. Pulido de lectura

```text
[ENCABEZADO con {RAMA} = fase5/lectura]
Lee PROGRESO.md (Fase 5) y los scripts de Reading/ y Menu/.
TAREA:
(1) Modos de página Día, Sepia y Noche: un tinte por material en PDF y CBZ, y CSS en EPUB, TXT, MOBI y DOCX.
(2) EPUB y similares: fuente (2 o 3 libres incluidas), tamaño, interlineado y márgenes, con nueva paginación.
(3) Búsqueda dentro del libro con un teclado virtual propio en el menú, cancelable, con resultados y salto a la página. Se adelanta para PDF con texto y EPUB.
(4) Índice (TOC) navegable desde el menú.
(5) Marcadores con nombre y lista.
(6) Panel 2D clásico opcional (Ajustes → "Lectura en panel plano") para quien prefiera una página plana grande.
Agrega filas al Paso 10. Commit en fase5/lectura y push. Responde con un resumen, archivos y riesgos.
```

## 18. Estantería 3D, carpetas y accesibilidad

```text
[ENCABEZADO con {RAMA} = fase5/estanteria]
Lee PROGRESO.md (Fase 5) y docs/BIBLIOTECA.md.
TAREA:
(1) Estantería 3D de la biblioteca, en passthrough y en la casa (en la casa, en la estantería real de la biblioteca): portadas como lomos o tapas que se toman con la mano para abrir el libro.
(2) Carpetas o colecciones, renombrar, ordenar (reciente, título, autor) y filtrar por formato.
(3) Accesibilidad: tamaño del menú, modo zurdo (intercambia las manos de los gestos y de los controles), alto contraste, reducir movimiento (sin animaciones bruscas) y subtítulos de los avisos.
(4) Pantalla "Acerca de" con la versión y acceso a "Licencias de terceros" (texto completo de cada licencia del inventario).
Agrega filas al Paso 10. Commit en fase5/estanteria y push. Responde con un resumen, archivos y riesgos.
```

> 🧪 **Prueba en el visor:** versión candidata completa.

## 19. Preparación para la tienda

> ⚠️ Antes, el usuario decide el **nombre final**, que no debe chocar con marcas existentes (hoy es «Folio XR», provisional). Además, crea su cuenta de organización en Meta y su keystore de firma, que nunca se sube al repositorio.

```text
[ENCABEZADO con {RAMA} = tienda/lanzamiento]
Lee docs/LICENCIAS_Y_TIENDA.md (lista de verificación antes de publicar) y PROGRESO.md (Publicación comercial). Nombre final de la app: {NOMBRE}.
TAREA:
(1) Inventario final de licencias, verificado contra los paquetes reales del proyecto, y pantalla "Licencias de terceros" completa.
(2) Política de privacidad en docs/PRIVACIDAD.md (qué datos se guardan en el visor, conectores de nube y tokens; sin analítica de terceros si no se agrega), con versión en español e inglés para publicarla en una URL.
(3) Revisión de los requisitos técnicos de Meta (VRC) que apliquen:
- Rendimiento a 72 FPS.
- Manejo de pausa, foco y menú del sistema; sin escrituras fuera de la carpeta de la app; permisos mínimos en el AndroidManifest.
- Manos y mandos; recentrado; idioma; nombre e ícono.
Deja una lista con el estado de cada punto.
(4) Build de lanzamiento: IL2CPP ARM64, compilación de desarrollo desactivada, logs detallados solo en Modo diagnóstico, versionCode/versionName y firma con un keystore que el usuario configura localmente. Documenta los pasos sin incluir contraseñas.
(5) Íconos y pantalla de inicio originales generados por código o diseñados para la app (no de Livro), y textos de la ficha de la tienda en español e inglés, sin mencionar otras apps.
Crea docs/LANZAMIENTO.md con la lista de verificación final. Commit en tienda/lanzamiento y push. Responde con un resumen, archivos y riesgos.
```

## 20. Correcciones finales

Este prompt se escribe con los resultados de la prueba completa en el visor y del registro. Se sigue el mismo formato: encabezado, lista concreta de fallas con su línea del registro y criterio de aceptación de cada una.
