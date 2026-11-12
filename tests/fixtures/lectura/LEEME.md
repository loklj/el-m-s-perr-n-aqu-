# Corpus original para índice y lectura

Regenerar desde la raíz con `python3 tests/lectura/generar.py`. Fechas ZIP fijas; textos, estructuras y marcadores creados para Folio XR. El generador reutiliza funciones propias del corpus de formatos, que también regenera de forma determinista.

| Archivo | Caso |
| --- | --- |
| `indice.epub` | EPUB nav: landmarks separado, dos anclas en el mismo capítulo y jerarquía; enlace externo excluido |
| `indice-ncx.epub` | Reserva NCX, etiquetas y jerarquía sin mezclar texto de hijos |
| `indice.mobi` | INDX/NCX/CNCX propio con un destino a mitad de capítulo |
| `indice-html.mobi` | MOBI antiguo con guía TOC y enlaces filepos |
| `indice.azw3` | KF8 con destino dentro de un fragmento reconstruido |
| `indice.pdf` | Dos páginas, tres marcadores, título UTF-16, destino directo y destino nombrado |
| `indice-stream.pdf` | Mismo índice en ObjStm y xref stream con Flate/PNG predictor |
| `indice-incremental.pdf` | Revisión incremental que sustituye la etiqueta de un marcador |
| `ciclo.pdf`, `truncado.pdf` | Metadatos dañados: ciclo de marcadores y offset fuera de rango |
| `cifrado-metadata.pdf` | Solo indicador sintético Encrypt para comprobar rechazo; no es un PDF cifrado válido ni contiene cifrado real |

El PDF clásico, el comprimido y el incremental se generan con estructuras propias. No se copian libros, código ni muestras de otros lectores. El PDF disperso de más de 5 GiB y el HTML de composición se crean temporalmente durante las pruebas, no se distribuyen. Ver [pruebas y límites](../../lectura/LEEME.md).
