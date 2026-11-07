# Licencias para vender Folio XR (USD 4,99, código cerrado)

Revisión: 7 de noviembre de 2026. Resume qué permite cada licencia y qué obliga a hacer para vender la app en la Meta Horizon Store **sin publicar el código**. Es información para decidir, no asesoría legal: antes de publicar conviene que alguien con formación jurídica revise el inventario final.

Complementa a [Formatos, bibliotecas y licencias](FORMATOS_Y_LICENCIAS.md), que detalla cómo se usaría técnicamente cada biblioteca.

## 1. Conclusiones

1. **Unity Personal sirve** (gratis): Sergio cumple el límite y en Unity 6 la pantalla «Made with Unity» es opcional.
2. **El SDK de Meta permite vender la app** sin pagar regalías. Además, **prohíbe** usarlo de forma que quede sujeto a una licencia de código abierto. Eso refuerza la regla del proyecto: nada GPL ni LGPL junto al SDK.
3. **libmobi (LGPL) sale del plan de producción.** MOBI/AZW/AZW3 se leerán con un **parser propio**. También resuelve su otro problema: libmobi carga el libro entero en memoria.
4. Para CBR, si libarchive falla con algún RAR, **UnRAR** es una alternativa legal en una app de pago.
5. Para DOCX hay una opción permisiva: **java-mammoth (BSD-2)**.
6. MEGA: el SDK oficial en C++ es BSD-2, pero hay que **compilarlo sin FreeImage ni FFmpeg** (licencias GPL/LGPL). El código de la app Android de MEGA **no** se puede reutilizar.
7. La app necesita una pantalla **«Licencias de terceros»** con los avisos de todo lo que se incluya.
8. **El repositorio es público hoy.** Para vender con código cerrado conviene hacerlo privado. Es un cambio de configuración que debe confirmar el usuario.

## 2. Unity

| Punto | Situación |
| --- | --- |
| Plan | **Unity Personal**, gratis. Pueden usarlo personas y organizaciones pequeñas con **menos de USD 200.000 de ingresos y financiamiento en los últimos 12 meses**. Por encima de eso corresponde Unity Pro. [U1, U2] |
| Pantalla de inicio | En proyectos hechos con **Unity 6** (6.3 LTS lo es), la pantalla «Made with Unity» es **opcional**. [U1] |
| Runtime Fee | Cancelada: no hay cobro por instalación con ninguna versión. [U1] |
| Propiedad | El contenido creado con Unity Personal es del usuario. [U2] |
| Activación | En Unity Hub: **Ajustes → Licencias → Agregar → licencia personal gratuita**. Sin licencia activa, el editor no abre. |
| Paquetes de Unity | URP, TextMeshPro y el plugin OpenXR vienen con las condiciones de Unity para usarse en proyectos Unity. No requieren publicar código. |

## 3. Meta

### Licencia del SDK (Meta Platform Technologies SDK License Agreement)

El Meta XR All-in-One SDK está en la Asset Store como «activo restringido»: se rige por la licencia de Meta, no por la EULA general de la Asset Store. [M2]

- **Concede:** una licencia gratuita (sin regalías), no exclusiva y revocable, para usar y reproducir el SDK con el fin de desarrollar, probar y distribuir la aplicación. [M1]
- **Solo para productos aprobados por Meta** (Quest), salvo que la documentación permita un uso más amplio. [M1]
- **Prohíbe:** modificar el SDK (salvo el código de ejemplo) y hacerle ingeniería inversa. También prohíbe interferir con el botón Meta, los mensajes del sistema o las advertencias de salud y seguridad. [M1]
- **Prohíbe usarlo o redistribuirlo de forma que quede sujeto a una licencia de código abierto.** [M1] → Ningún componente GPL/LGPL en el APK.
- **Privacidad:** el desarrollador debe avisar de forma clara que terceros, incluida Meta, pueden recibir información desde la app. [M1] → Debe figurar en la política de privacidad.

### Tienda

| Punto | Situación |
| --- | --- |
| Comisión | Meta descuenta primero el impuesto a la venta y después su **comisión del 30 %** (salvo otro acuerdo). [T1] Con un precio de USD 4,99, el desarrollador recibe unos **USD 3,49 por venta, antes de impuestos**. |
| Pagos | En dólares. A desarrolladores fuera de EE. UU. y Canadá les paga Meta Platforms Technologies Ireland Ltd. [T2] El nombre de la cuenta bancaria debe coincidir con el de la cuenta de desarrollador: es la causa más común de pagos rechazados. [T3] |
| Precio | Después de cambiar el precio hay que esperar **30 días** para volver a cambiarlo. [T4] |
| Requisitos | Cuenta de desarrollador, revisiones técnicas VRC, política de privacidad, clasificación por edades (IARC), ficha con imágenes propias. Nada de Livro en la ficha. |
| Impuestos en Chile | Tema aparte, a revisar antes de la primera venta. |

## 4. Inventario de componentes

«Incluido» significa que va dentro del APK. Las obligaciones de BSD, MIT y Apache-2.0 son parecidas: conservar el aviso de copyright y el texto de la licencia. Apache-2.0 exige además copiar su archivo `NOTICE` si existe.

| Componente | Licencia | Para qué | Obligaciones | Estado |
| --- | --- | --- | --- | --- |
| Motor Unity, URP, TextMeshPro, OpenXR | Condiciones de Unity | Motor | Plan Personal vigente | En uso |
| Meta XR SDK | Licencia SDK de Meta | VR, passthrough, manos | Ver sección 3 | En uso |
| `PdfRenderer` de Android | Parte del sistema | PDF (Hito 1) | Ninguna: no se incluye en el APK | En uso |
| Android System WebView | Parte del sistema | EPUB (Hito 2) | Ninguna: no se incluye en el APK | Decidido |
| PDFium propio | BSD-3 / Apache-2.0 + terceros | Solo si `PdfRenderer` no basta | Avisos del conjunto. **FreeType** (licencia FTL) exige un crédito en la documentación; libjpeg-turbo, el crédito del Independent JPEG Group | Candidato |
| Readium Kotlin Toolkit | BSD-3-Clause | Modelo EPUB | Aviso | Candidato |
| libarchive | BSD-2-Clause (y algunos archivos BSD-3, dominio público o CC0) | CBZ, CBR | Aviso | Candidato |
| UnRAR | Licencia UnRAR (freeware) | CBR, si libarchive falla | Se permite usarlo gratis en cualquier programa para **extraer** RAR. Se prohíbe usarlo para crear un compresor compatible con RAR. Si se modifica, incluir el párrafo de la licencia. [R1] | Alternativa |
| libmobi | **LGPL-3.0+** | MOBI/AZW/AZW3 | Exige permitir al usuario reemplazar la biblioteca, algo dudoso en un APK firmado y vendido en tienda. Choca con la cláusula de Meta sobre licencias abiertas. [L1] | **Descartado** → parser propio |
| java-mammoth | BSD-2-Clause [D1] | DOCX → HTML | Aviso | Candidato |
| Apache POI | Apache-2.0 | DOC binario | LICENSE y NOTICE | A evaluar (pesado en Android) |
| Lectores TXT, RTF, ODT | Código propio | — | — | Plan |
| KFX | — | — | No hay biblioteca permisiva y casi siempre trae DRM: solo diagnóstico | Plan |
| AppAuth-Android | Apache-2.0 | Inicio de sesión de Google | LICENSE y NOTICE | Candidato |
| Dropbox Java SDK | MIT (confirmar versión al integrar) | Dropbox | Aviso | Candidato |
| MEGA SDK (C++) | BSD-2-Clause [G1] | MEGA | Aviso. **Compilar sin FreeImage ni FFmpeg** (GPL/LGPL). La app Android de MEGA tiene otra licencia, de solo revisión: no copiar nada de ella. [G2] | Candidato |
| Fuentes Noto | SIL OFL 1.1 | Texto de la app y de los libros | Incluir la licencia. No vender la fuente por separado | Plan |
| `LegacyRuntime.ttf` de Unity | Fuente interna de Unity | Texto del Hito 1 | Reemplazar por TextMeshPro + Noto antes de publicar | Temporal |

### Solo para desarrollo (no van en la app)

reportlab (BSD), Pillow (licencia MIT-CMU), epubcheck (BSD-3), poppler y qpdf (herramientas del sistema). **PyMuPDF (AGPL)** solo lo usa el script de medición `tools/prototipos/medir_epub_mupdf.py`: nunca debe entrar en la app.

## 5. Condiciones de las nubes (no son licencias de código, pero también obligan)

| Servicio | Condición |
| --- | --- |
| Google Drive | Leer todo el Drive (`drive.readonly`) es un permiso restringido: exige verificación de Google y una evaluación de seguridad anual (CASA). Con `drive.file` solo se accede a lo que el usuario elige. El inicio de sesión debe hacerse en el navegador del sistema, no dentro de la app. Decidir antes de la Fase 3 (ver `PROGRESO.md`). |
| Dropbox | Una app nueva parte en modo desarrollo, con un máximo de 500 usuarios. **Al llegar a 50 usuarios hay dos semanas** para pedir y obtener la aprobación de producción; si no, la app deja de aceptar usuarios nuevos. Después de aprobada, **no se puede cambiar el nombre**. [X1] |
| MEGA | No tiene inicio de sesión delegado (OAuth): el usuario tendría que escribir su correo y contraseña de MEGA dentro de la app. Alternativa más simple y segura: admitir solo **enlaces públicos** de MEGA. Revisar en mega.nz si el SDK exige registrar una clave de aplicación. |

## 6. Qué hay que hacer antes de publicar

1. Pantalla **Ajustes → Acerca de → Licencias de terceros**, generada desde un archivo `THIRD_PARTY_NOTICES` que se actualiza con cada versión.
2. Inventario con la versión exacta de cada biblioteca y su licencia (se completa al integrarlas).
3. Política de privacidad: datos que recibe Meta según su SDK, tokens de nubes guardados solo en el visor y libros que nunca se envían a servidores propios.
4. Revisar que el nombre final no choque con marcas registradas («Folio XR» es provisional).
5. Hacer privado el repositorio o, como mínimo, dejar de publicar ahí el código de la app (decisión del usuario).

## 7. Fuentes

Consultadas el 7 de noviembre de 2026.

- **U1:** [Unity: cambios de planes y precios](https://unity.com/pricing-updates).
- **U2:** [Unity Personal: requisitos](https://unity.com/products/unity-personal).
- **M1:** [Meta Platform Technologies SDK License Agreement](https://developers.meta.com/horizon/licenses/oculussdk/).
- **M2:** [Meta XR All-in-One SDK (UPM)](https://developers.meta.com/horizon/downloads/package/meta-xr-sdk-all-in-one-upm/).
- **T1–T4:** [Meta: gestión de cuenta, banco e impuestos](https://developers.meta.com/horizon/resources/publish-account-management-bank-tax/) (comisión, pagos, rechazos y período de 30 días).
- **R1:** [Licencia UnRAR (SPDX)](https://spdx.org/licenses/UnRAR.html).
- **L1:** Discusiones sobre LGPL en apps de tienda, por ejemplo [Qt Forum: cumplir la LGPL en Android](https://forum.qt.io/post/614585). No hay un criterio jurídico único: por eso se evita.
- **D1:** [java-mammoth en Maven Central (licencia BSD-2)](https://central.sonatype.com/artifact/org.zwobble.mammoth/mammoth/1.12.0).
- **G1:** [MEGA SDK: ficha de licencias de Debian](https://dfsg-new-queue.debian.org/reviews/megacmd/2.4.0-1).
- **G2:** [F-Droid: licencia de la app Android de MEGA](https://f-droid.org/forums/topic/mega-nz/).
- **X1:** [Guía para desarrolladores de Dropbox: aprobación de producción](https://www.dropbox.com/developers/reference/developer-guide).
