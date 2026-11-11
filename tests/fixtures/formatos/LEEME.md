# Fixtures originales — Formatos 2

Generados por `tests/formatos/generar.py` con texto e imagen propios. No contienen obras de terceros ni datos personales.

- `propio.mobi`: PalmDB/MOBI sin compresión, texto UTF-8 e imagen PNG por registro.
- `palmdoc.azw`: el mismo texto, comprimido como literales PalmDOC; el test añade referencias superpuestas y corruptas.
- `huff.mobi`: HUFF/CDIC de códigos de ocho bits; pruebas adicionales de diccionario recursivo y ciclos en CodecTest.
- `kf8.azw3`: esqueleto, fragmento, tablas INDX/TAGX y FDST e imagen kindle:embed.
- `cifrado-sintetico.mobi`: indicador DRM activado; prueba de rechazo, sin cifrado real.
- `truncado.mobi`: entrada dañada para rechazar sin acceder fuera del archivo.
- TXT UTF-16LE/BE sin BOM y Latin-1. Se complementan con los cuatro TXT existentes del directorio superior.
- `zip-disfrazado.cbr`: ZIP con dos imágenes, para elegir el lector por contenido.
- `rar4-almacenado.cbr`: RAR4 propio sin compresión, una imagen con CRC y encabezados válidos; no se implementa compresión RAR.
- `rar5-firma.cbr`: firma RAR5 sin archivo completo, para mostrar el mensaje de formato no compatible antes de extraer.

Comandos y límites de la evidencia en `tests/formatos/LEEME.md`. Ninguno de estos archivos acredita por sí solo todas las variantes comerciales de un formato.
