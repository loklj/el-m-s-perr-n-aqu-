# Corpus original de documentos

Generar desde la raíz con `python3 tests/documentos/generar.py`; los ZIP tienen fechas y orden deterministas. Texto y PNG de 2 × 2 píxeles creados para Folio XR, sin contenido de terceros.

| Archivo | Propósito |
| --- | --- |
| `propio.docx`, `propio.odt`, `propio.rtf` | Documentos legibles con título, acentos, negrita, cursiva, lista, tabla e imagen. RTF añade Unicode, euro/emoji, destino desconocido y un campo con resultado visible |
| `danado.docx`, `danado.odt`, `danado.rtf` | XML o grupo RTF truncado; se deben rechazar |
| `entidad.docx` | DTD con entidad externa: rechazo antes de resolverla |
| `externo.docx` | Relación de imagen externa: aviso y omisión, sin petición de red |
| `cifrado-sintetico.odt` | Manifest con indicador encryption-data para comprobar rechazo; no contiene cifrado real y no se usa para probar descifrado |
| `objeto.rtf` | Objeto OLE ficticio: omisión, aviso y conservación del texto restante |
| `ole-firma.doc`, `kfx-firma.kfx` | Solo firmas sintéticas, **no archivos completos válidos**; prueban diagnóstico sin atribuir DRM |
| `desconocido.bin` | Datos no reconocidos: diagnóstico explícito |

Los tres documentos válidos abrieron en LibreOffice y se exportaron a PDF. No se certifica fidelidad ni soporte general por ello. Los casos grandes, binarios RTF, codificaciones y límites adicionales se generan temporalmente dentro de las pruebas JVM. Comandos y resultados en [tests/documentos/LEEME.md](../../documentos/LEEME.md).
