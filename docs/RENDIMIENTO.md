# Archivos gigantes y rendimiento

## Estado

Encargo 15, partes 1, 2 y 3, implementado el 12 de noviembre de 2026 en `fase4/rendimiento`, con rebase final el 13 de noviembre sobre `2329aff` de `fase-1/preparacion` (estantería, accesibilidad y búsqueda de Claude). Caso prioritario: PDF de cómics escaneados de 2,4–4,2 GB. Se revisó la cadena de tamaños de 64 bits hasta 8 GiB, sin imponer un tope nuevo de 8 GiB a los libros. **Pendiente probar el APK en Quest 3 con los archivos reales del usuario.** Las metas de 3 s, 100 ms y 72 FPS no se declaran alcanzadas con pruebas de PC.

No se modifican entornos, herramientas, `Resources`, `herramientas/` ni la esfera. En `MenuModel` solo se anexa «Modo diagnóstico». No se incorporan bibliotecas ni componentes nativos nuevos.

## Transferencia y recuperación

- Wi-Fi interpreta `Content-Length` como `long`, rechaza valores negativos, desbordados, repetidos y `Transfer-Encoding` ambiguo. No hay un límite de 2/4 GB ni un arreglo del tamaño del libro. Tres conexiones como máximo y una subida a la vez; cerrar el servidor cierra también sus conexiones. El navegador envía el `File` directamente y muestra progreso; el visor recibe contadores `long` cada 250 ms y actualiza su aviso al sondear.
- SAF consulta `OpenableColumns.SIZE` mediante `getLong`. Admite tamaño desconocido; verifica tamaño final y detecta si el proveedor anuncia menos bytes de los que entrega. El permiso temporal alcanza para completar la copia privada; no se conserva una dependencia posterior del URI.
- `FolioLargeFiles` comparte el bucle de copia (buffer de 256 KiB), cancelación, validación y reservas de espacio entre SAF y Wi-Fi. Reserva lo pendiente para tamaños conocidos, comprueba el espacio antes de cada escritura y deja 128 MiB libres. Las restas evitan desbordar al sumar tamaño + margen. SAF desconocido comprueba espacio por bloque. Las reservas son del proceso: otros procesos pueden consumir espacio, por lo que también se verifica durante la copia.
- El parcial se sincroniza antes de publicar. `ingest` comprueba longitud, calcula la identidad leyendo solo principio/final mediante `RandomAccessFile.seek(long)`, escribe el diario mediante temporal + sincronización + renombrado, mueve el archivo dentro del mismo almacenamiento y registra la ficha SQLite. No hace una segunda copia del libro.
- Reinicio: descarta copias incompletas; recupera archivos completos ya movidos pero sin ficha. Verifica que destino, ruta y tamaño coincidan con el diario. Si SQLite falla o hay discrepancia, conserva el diario para revisión/reintento en otro arranque. No publica libros truncados. No se añade reanudación por rangos: una transferencia interrumpida debe reenviarse.
- Sigue pendiente la alternativa «abrir en su ubicación» para archivos del selector. En este encargo SAF copia a la biblioteca, como la base. OPDS mantiene su implementación anterior; las nuevas reservas corresponden a SAF y Wi-Fi.

## Render y presupuestos

`ParcelFileDescriptor` entrega el archivo al `PdfRenderer` del sistema; `File.length()` y `getStatSize()` son `long`, sin conversión a `int`. Solo el número de página y las dimensiones de un bitmap son enteros de 32 bits. El trabajo permanece en una cola de fondo. Cancelar evita trabajos pendientes y resultados tardíos; **una llamada nativa ya en curso termina antes de poder cerrar su descriptor**.

| Recurso | Límite / comportamiento |
| --- | --- |
| Capturas Java compartidas PDF/CBZ/CBR/HTML | Reserva global de 128 MiB para buffers de captura; no espera bloqueando la UI de WebView |
| Página normal | Hasta 8 millones de píxeles, ancho solicitado hasta 4096 y ningún lado mayor de 8192 |
| PSS del proceso | Umbral de precaución 1152 MiB; presupuesto 1536 MiB; muestreo cada 2 s |
| Memoria gráfica | Estimación: máximo entre páginas vivas RGBA con mipmaps y dato del profiler del controlador; precaución 192 MiB, presupuesto 256 MiB |
| Margen del heap Java | Precaución bajo 96 MiB libres; presión alta bajo 48 MiB |
| Presión media / alta | Máximo 3 / 1 millones de píxeles; PDF reduce además ancho a 1400 / 832; desactiva precarga PDF y HTML y vacía caché HTML al capturar |
| Bitmap PDF que no cabe | Hasta tres reducciones adicionales a la mitad; error explícito si tampoco cabe el mínimo |
| Caché de FXR1 | 192 MiB de disco, LRU por fecha; elimina temporales; respeta espacio reservado para importaciones |
| Caché HTML | 24 MiB comprimidos; captura y precarga seriales |
| Imagen temporal hacia Unity | Máximo 40 MiB; FXR1 exige cabecera, dimensiones y longitud exactas antes de crear textura |
| Diagnóstico | 1024 muestras recientes por métrica, hasta 48 métricas; CSV de unos 2 MiB más una rotación anterior |

Los presupuestos de captura se comparten entre los motores; las asignaciones internas del PDFium/WebView del sistema y del controlador gráfico no se pueden acotar desde este código. Un PDF hostil o una imagen que el decodificador nativo no pueda reducir todavía puede fallar. No se implementan tres libros simultáneos.

FXR1 se escribe por filas RGBA de abajo hacia arriba, con un buffer de una fila; desaparece la segunda copia completa del bitmap en memoria directa. La siguiente doble página se precarga solo sin presión. Una petición nueva invalida la precarga pendiente. La caché usa ruta canónica, longitud, fecha del documento, página y ancho; se valida antes de consumir y se mueve al temporal de Unity sin copiar de nuevo sus bytes.

Unity lee cada imagen acotada en un trabajador. La subida a la textura, la remapificación nocturna y los mipmaps siguen en el hilo de Unity. Esto cambia el intercambio: hay un buffer administrado temporal por página (máximo 40 MiB), a cambio de sacar la lectura de disco del cuadro. Las texturas liberan su copia CPU tras subir a GPU. Al iniciar la sesión se limpian los temporales de páginas huérfanos, sin borrar la caché FXR1 ni los libros.

## Cómo medir en Quest

1. Compilar e instalar como indica [HITO1_GUIA.md](HITO1_GUIA.md). Mantener la misma versión, entorno, escala del libro, nitidez 1,4 y estado térmico entre pruebas. Anotar también si se usan controladores o manos.
2. Ajustes → **Modo diagnóstico: sí**. El ajuste persiste. Se registran PSS, gráficos estimados, presión, FPS, tiempos de cuadro y latencias. No se guardan rutas, títulos, contraseñas ni texto de libros en el CSV. Los registros generales existentes de biblioteca son independientes.
3. Primera apertura después de reiniciar el proceso: serie `pdf_open_cold_ms`. Reabrir un archivo ya abierto en ese proceso: `pdf_open_warm_ms`. «Frío» aquí significa primera apertura de ese proceso, **no** caché del sistema operativo vacía. Para repetir una prueba sin precarga persistida, borrar solo la carpeta de caché `folio-paginas/folio-fxr1`, con la app detenida; nunca borrar datos de la app ni `files/libros`.
4. `first_page_ms` mide desde crear el lector hasta obtener la primera textura. `first_spread_ms` mide preparación de la pareja, después de contar páginas. `page_precached_ms` / `page_uncached_ms` incluyen espera, lectura y subida de la textura PDF. `pdf_cached_ms` / `pdf_render_ms` miden solo el lado Java. El CSV distingue ambos: no comparar una medida Java con la meta del usuario.
5. Pasar al menos 50 parejas a ritmo pausado y otras 50 rápidamente, avanzar y retroceder. `spread_ready_ms` excluye la animación de hoja de 0,35 s. Medir cambios precargados y no precargados por separado.
6. Hacer al menos 30 saltos no adyacentes con índice/marcadores, distribuidos entre principio, medio y final. Se anotan `random_spread_ms` y `pdf_random_ms` cuando el desplazamiento supera dos páginas. No se cambia de página automáticamente al activar diagnóstico. En pruebas desde código puede llamarse `FolioBook.GoToPage(indiceDesdeCero)` con posiciones pseudoaleatorias reproducibles; no se añadió otra opción al menú.
7. Esperar 10 s para el volcado periódico, desactivar diagnóstico o suspender la app para solicitar otro volcado. Copiar `rendimiento.csv` y, si existe, `rendimiento.csv.previous` desde `Application.persistentDataPath` (normalmente `/sdcard/Android/data/com.loklj.folioxr/files/`). Por ejemplo: `adb pull /sdcard/Android/data/com.loklj.folioxr/files/rendimiento.csv`.
8. CSV: `utc_ms,metric,unit,total,window,median,p95,p99`. Percentiles por rango más próximo sobre las últimas 1024 muestras de **cada métrica**, no promedio de percentiles. Para cuadros se envían todos los intervalos de la ventana desde un arreglo preasignado; no se reservan objetos por cuadro. FPS es el promedio por ventana de aproximadamente 2 s. PSS es del proceso de la app; no se suman procesos externos de WebView. No son tiempos de GPU ni métricas del compositor XR.

### Resultados reales del visor (por completar)

| Archivo / tamaño / páginas | Método de importación | Apertura fría / caliente p50/p95/p99 | Primera página | Cambio precargado / sin precarga p50/p95/p99 | Saltos p50/p95/p99 | PSS / gráficos máx. | FPS / cuadro p95/p99 | Resultado |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Cómic real 2,4 GB | Wi-Fi y SAF | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Sin prueba Quest |
| Cómic real 4,2 GB | Wi-Fi y SAF | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Sin prueba Quest |
| PDF generado >4 GiB | Wi-Fi y SAF | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Sin prueba Quest |
| PDF de 8 GiB | Wi-Fi y SAF | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Pendiente | Sin prueba Quest |

Metas: primera página local <3 s; cambio precargado <100 ms; mantener 72 FPS. Registrar cuántas muestras incumplen y el estado térmico, sin marcar aprobado por una mediana favorable si p95/p99 o los cuadros fallan.

## Generadores y evidencia de PC

[Generadores](../tools/test-data/LEEME.md): PDF de 100.001 páginas, TXT de cientos de MB, CBZ de miles de imágenes, EPUB por párrafos sin cargar un capítulo gigante entero y PDF de imágenes de más de 4 GB. `--solo pdf-escaneado` usa imágenes de 4096×5792 por defecto (ajustable con `--scan-width`); `--gb 8 --limite-gb 10` permite generar el caso de 8 GiB si hay espacio. Semilla, parámetros, tamaño y SHA-256 quedan en el manifiesto. Hay comprobación previa y límite durante la escritura, dejando 1 GiB libre. Nunca subir esas salidas a Git.

`--solo rar-solido --rar-command /ruta/a/rar` invoca el compresor RAR del usuario, con su licencia, fuera de la app. Se comprobó un RAR5 sólido de ocho imágenes con RAR 7.23: encabezado «RAR 5, solid» y comprobación CRC correcta. El lector de la app sigue mostrando el diagnóstico de RAR5 no compatible. **No se distribuye RAR ni se implementa compresión derivada de Junrar/UnRAR**; ver [inventario](LICENCIAS_Y_TIENDA.md).

Evidencia Linux del 12 de noviembre: PDF de **4.423.496.009 bytes**, 208 páginas con imágenes 4096×5792, índice PDF 1.5 *xref stream*. `pdfinfo` lo reconoce y PyMuPDF lo abre sin reparar; dibuja páginas 1, 104 y 208 a 420×595 en 352, 357 y 430 ms aproximadamente; RSS máximo 106.820 KiB. Estos son datos del lector externo de PC, **no** de Android. Se borró el archivo pesado tras medir. Parámetros y hash: [medicion-pc.json](../tests/rendimiento/medicion-pc.json).

Pruebas reproducibles y desglose en [tests/rendimiento/LEEME.md](../tests/rendimiento/LEEME.md). Las pruebas dispersas crean longitudes y posiciones reales hasta 8 GiB; el destino de prueba conserva huecos para no ocupar ese disco. En producción se escriben todos los bytes del usuario. SQLite, proveedor SAF, socket Wi-Fi real del visor y render nativo se comprueban en la tabla pendiente, no con los sustitutos JVM.
