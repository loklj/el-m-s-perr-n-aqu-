# Hito 1 — Guía paso a paso (Pop!_OS + Quest 3)

Objetivo: instalar en el Quest 3 una app mínima que muestre un **libro 3D con páginas de un PDF**, con passthrough, mandos Touch y manos, y que se pueda **agarrar y mover**.

> **Estado:** el Hito 1 está terminado y probado en Quest 3 por el usuario; la base está en `fase-1/preparacion`. La nitidez del Hito 1.1 mejoró en el visor. El Hito 1.2 (ya unido a `fase-1/preparacion`) añade el ajuste con el stick izquierdo, pendiente de prueba específica en Quest 3; en la nube solo se revisa el código a mano, sin compilar Unity. Si algo falla, anota en qué paso y qué mensaje apareció.

Tiempo estimado: 1–2 horas la primera vez (casi todo es descarga e instalación). Espacio en disco: unos 15–20 GB.

---

## Qué trae esta rama

| Archivo | Para qué sirve |
| --- | --- |
| `hito1/Assets/FolioXR/Scripts/Reading/FolioBook.cs` | El libro: carga el PDF, muestra dos páginas, botones para avanzar y retroceder |
| `hito1/Assets/FolioXR/Scripts/Reading/PdfPageRenderer.cs` | Puente entre Unity y el plugin de Android |
| `hito1/Assets/Plugins/Android/FolioPdfRenderer.java` | Dibuja las páginas con el lector de PDF de Android, en un hilo aparte |
| `hito1/Assets/FolioXR/Editor/FolioXRHito1Setup.cs` | Agrega el menú **Folio XR** al Editor de Unity |
| `hito1/Assets/StreamingAssets/folio-prueba.pdf` | PDF de prueba de 8 páginas, con contenido propio |
| `tools/generar_pdf_prueba.py` | Script que generó ese PDF |

Los scripts no dependen del SDK de Meta: compilan aunque el SDK todavía no esté instalado.

---

## Paso 1 — Bajar el repositorio

En una terminal:

```bash
sudo apt install git curl
git clone https://github.com/loklj/el-m-s-perr-n-aqu-.git ~/folio-xr
cd ~/folio-xr
git checkout fase-1/preparacion   # incluye el Hito 1, el 1.1 y el 1.2
```

## Paso 2 — Instalar Unity Hub (comandos oficiales para Ubuntu)

Unity solo da soporte oficial a Ubuntu 22.04/24.04. Pop!_OS se basa en Ubuntu, así que normalmente funciona, pero no está garantizado.

```bash
sudo install -d /etc/apt/keyrings
curl -fsSL https://hub.unity3d.com/linux/keys/public | sudo gpg --dearmor -o /etc/apt/keyrings/unityhub.gpg
echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/unityhub.gpg] https://hub.unity3d.com/linux/repos/deb stable main" | sudo tee /etc/apt/sources.list.d/unityhub.list
sudo apt update
sudo apt install unityhub
```

Abre Unity Hub e inicia sesión con una cuenta de Unity (la licencia Personal es gratis si cumples sus condiciones).

> **Si usas Pop!_OS 24.04 con el escritorio COSMIC:** Unity está pensado para GNOME sobre X11. Si el Editor se ve mal o se cierra, anótalo; la alternativa documentada es usar Ubuntu 24.04 LTS.

## Paso 3 — Instalar el Editor

En Unity Hub: **Installs → Install Editor →** la versión más reciente de **Unity 6.3 LTS**. En módulos, marca:

- **Android Build Support**
  - **OpenJDK**
  - **Android SDK & NDK Tools**

## Paso 4 — Crear el proyecto dentro del repositorio

En Unity Hub: **Projects → New project**

- Plantilla: **Universal 3D**
- Project name: `unity`
- Location: la carpeta del repositorio (`~/folio-xr`)

Así el proyecto queda en `~/folio-xr/unity`. Cierra Unity cuando termine de abrirse por primera vez.

## Paso 5 — Copiar los archivos del Hito 1 al proyecto

```bash
cd ~/folio-xr
cp -r hito1/Assets/. unity/Assets/
```

Vuelve a abrir el proyecto en Unity Hub. En la barra de menú debe aparecer **Folio XR**.

## Paso 6 — Configurar Unity para el Quest

Son los pasos de la guía oficial de Meta (*Manual Project Setup*):

1. **File → Build Profiles** → elige **Meta Quest** → **Enable Platform** (si pide instalar `com.unity.xr.openxr`, **Install**) → luego **Switch Platform**. En versiones anteriores a Unity 6.1, si no aparece Meta Quest, elige **Android**.
2. **Edit → Project Settings → XR Plug-in Management** → si lo pide, **Install XR Plugin Management**. En la pestaña de **Meta Quest/Android**, marca **OpenXR**.
3. En Unity: **Window → Package Management → Package Manager** → botón **+** → **Add package by name** → escribe `com.meta.xr.sdk.all` → **Add**. No hace falta pasar por la Asset Store.
4. Si pide activar el conjunto de funciones Meta XR, **Enable**. Si pide reiniciar el Editor, acepta. Si pregunta por *OpenXR Hand Skeleton*, elige **Use OpenXR Hand**.
5. **Project Settings → XR Plug-in Management → OpenXR**, pestaña Android: activa el grupo de funciones **Meta XR**.
6. Abre el **Project Setup Tool** (menú **Meta XR Tools** o **Window → Meta → Meta XR SDK**) y toca **Fix All** y luego **Apply All**.

## Paso 7 — Armar la escena

1. Abre la escena de la plantilla (`Assets/Scenes/SampleScene`).
2. Borra **Main Camera** de la jerarquía.
3. Abre **Building Blocks** (desde la ventana Meta XR SDK) y agrega, en este orden: **Camera Rig**, **Passthrough**, **Controller Tracking** y **Hand Tracking**.
4. Menú **Folio XR → Crear libro de prueba (Hito 1)**. Aparece el libro y se fija el identificador del paquete en `com.loklj.folioxr`.
5. Clic derecho sobre **Libro de prueba (Folio XR)** → **Interaction SDK → Add Grab Interaction** → **Fix All** → **Create**. Esto lo hace agarrable con manos y mandos.
6. Guarda la escena (**Ctrl+S**).
7. Menú **Folio XR → Comprobar Hito 1**. Todo debe salir **OK**.

> Si al agregar un bloque aparece un segundo **Camera Rig**, borra el duplicado y deja solo uno.

## Paso 8 — Compilar el APK

1. **File → Build Profiles**: revisa que la escena esté en la lista (**Open Scene List → Add Open Scenes**).
2. Marca **Development Build** (en este hito es más fácil de diagnosticar).
3. **Build** → guarda como `~/folio-xr/Builds/FolioXR-hito1.apk`.

## Paso 9 — Instalar en el Quest 3

Con el modo desarrollador activo (ver `docs/PC_Y_VALIDACION.md`), conecta el visor por USB y acepta la depuración USB dentro del visor:

```bash
sudo apt install adb                          # si no tienes adb
adb devices -l                                # debe decir "device"
adb install -r ~/folio-xr/Builds/FolioXR-hito1.apk
```

En el visor: **Biblioteca → Orígenes desconocidos → Folio XR**.

## Paso 10 — Qué probar

| Prueba | Resultado esperado |
| --- | --- |
| Abrir la app | Ves tu habitación (passthrough) y un libro frente a ti |
| Portada | La página izquierda muestra la portada azul «Folio XR» |
| A o X | Avanza dos páginas; el texto de abajo dice «Páginas 3-4 de 8» |
| B o Y | Retrocede dos páginas |
| Página 2 y 4 | Se distinguen los tamaños de letra (8 a 18 puntos) |
| Agarrar con la mano o el gatillo de agarre | El libro se mueve contigo |
| Clic en el stick derecho | El libro vuelve frente a tu vista |
| Stick izquierdo arriba/abajo (Hito 1.2) | Sube/baja la nitidez entre 1,4 y 1,8, de 0,1 en 0,1: un paso por movimiento, sin repetición al mantenerlo inclinado; vuelve al centro para rearmarlo. El valor inicial es 1,4 y el elegido se conserva al cerrar y abrir la app. Muestra «Nitidez: 1,6x (stick izquierdo arriba/abajo). Más nitidez usa más batería.» durante 2,5 s y vuelve al estado actual de páginas; un error visible tiene prioridad |
| Pellizco (índice + pulgar) con la mano **derecha**, lejos del libro (Hito 1.1) | Avanza dos páginas |
| Pellizco con la mano **izquierda**, lejos del libro (Hito 1.1) | Retrocede dos páginas |
| Nitidez (Hito 1.1) | Texto de 14 puntos legible a ~45 cm; comparar con la primera versión |

### Actualizar a una versión nueva del código (por ejemplo, el Hito 1.2)

Con Unity cerrado: `git pull`, volver a copiar `hito1/Assets` al proyecto (paso 5), abrir Unity, **Build** y reinstalar (paso 9). La escena y los Building Blocks se conservan.

## Paso 11 (opcional) — Probar con tu propio PDF

```bash
adb push mi-libro.pdf /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
```

Cierra la app y ábrela de nuevo. Si existe `libro.pdf`, se abre ese en vez del de prueba. Para volver al de prueba:

```bash
adb shell rm /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
```

> PDF con contraseña: el lector de PDF de Android no los abre, y la app lo indica con un mensaje.

## Paso 12 (opcional) — Probar con tus PDF grandes (hasta 20 GB)

El lector debe abrir archivos de varios GB. Esta prueba usa los PDF grandes que ya tienes en el visor.

> **Importante:** mientras el PDF esté dentro de la carpeta de la app, **desinstalar Folio XR lo borra**. Actualizar con `adb install -r` no lo borra. Al terminar, devuélvelo a su lugar (punto 5).

1. Abre Folio XR una vez para que exista su carpeta de datos y ciérrala.

2. Busca tus PDF de más de 1 GB y anota la ruta exacta:

   ```bash
   adb shell "find /sdcard -iname '*.pdf' -size +2000000 2>/dev/null"
   ```

3. **Mueve** (no copies) uno a la carpeta de la app con el nombre `libro.pdf`. Usa comillas si el nombre tiene espacios:

   ```bash
   adb shell mv "/sdcard/Download/Mi PDF grande.pdf" /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
   ```

   Dentro del mismo almacenamiento debería ser instantáneo. Si demora, se está copiando y necesita espacio libre igual al tamaño del PDF. Si `mv` da error, avísame: hay otra forma.

4. Abre la app con el registro activo y prueba:

   ```bash
   adb logcat -s Unity FolioXR AndroidRuntime > folio-pdf-grande.log
   ```

   Anota los resultados en esta tabla:

   | Archivo | Tamaño | Páginas | Segundos hasta ver la 1.ª página | ¿Pasan las páginas con A/X? | Errores |
   | --- | --- | --- | --- | --- | --- |
   | | | | | | |

5. **Devuélvelo a su lugar** al terminar:

   ```bash
   adb shell mv /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf "/sdcard/Download/Mi PDF grande.pdf"
   ```

Qué esperar y qué significa cada caso:

- **Abre y pasan las páginas:** el lector de PDF de Android del Quest soporta ese tamaño.
- **«La página es demasiado grande para la memoria disponible»:** el archivo abrió, pero esa página trae una imagen gigante (típico de escaneos). Anota el número de página.
- **«No se pudo abrir el archivo…» u otro error:** guarda `folio-pdf-grande.log`. Dice si el límite está en el lector de PDF de Android; en ese caso se cambia de motor (MuPDF ya abrió un PDF de 20 GB en las pruebas en PC).

**Alternativa sin tus archivos:** genera un PDF de prueba en tu PC y cópialo al visor como `libro.pdf` (necesita espacio libre en el visor igual a su tamaño):

```bash
cd ~/folio-xr
sudo apt install python3-pil
python3 tools/test-data/gigantes.py --solo pdf-gb --gb 5                    # 5 GB, ~1 minuto
python3 tools/test-data/gigantes.py --solo pdf-gb --gb 20 --limite-gb 25    # 20 GB
adb push tools/test-data/salida/pdf-5gb-imagenes.pdf /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf
```

Sus páginas son ruido de colores con «Página N de M» encima: es a propósito, para que el archivo realmente pese lo indicado. Bórralo al terminar con `adb shell rm /sdcard/Android/data/com.loklj.folioxr/files/libro.pdf`.

## Si algo falla

Guarda el registro mientras reproduces el problema y termínalo con Ctrl+C:

```bash
adb logcat -s Unity FolioXR AndroidRuntime > folio-hito1.log
```

Anota qué paso falló y qué mensaje viste (en Unity, en la terminal o dentro del visor).

| Síntoma | Qué revisar |
| --- | --- |
| No aparece el menú Folio XR | Que `unity/Assets/FolioXR` exista y la consola de Unity no tenga errores en rojo |
| El libro sale en blanco con «No se pudo preparar el PDF» | Que exista `unity/Assets/StreamingAssets/folio-prueba.pdf` |
| Los botones A/B/X/Y no hacen nada | En OpenXR → Android, que esté activo un perfil de interacción de los mandos Touch (el Project Setup Tool suele activarlo) |
| No hay passthrough (fondo negro) | Que esté el bloque **Passthrough** y que el Project Setup Tool no muestre pendientes |
| `adb devices` dice `no permissions` o no muestra el visor | Desconecta y conecta el cable, acepta el aviso dentro del visor y revisa los permisos USB de Linux: <https://developer.android.com/studio/run/device> |
| Error de clase Java no encontrada en el log | Si activaste Minify, marca **Custom Proguard File** en Publishing Settings |
| El build falla con `.../NDK/toolchains/llvm/prebuilt/linux-x86_64/bin/clang++: not found` | El módulo **Android SDK & NDK Tools** quedó a medias (pasó el 7 de noviembre de 2026 en el PC de pruebas). Con Unity cerrado, reemplazar la carpeta `.../PlaybackEngines/AndroidPlayer/NDK` por el NDK **r27c** oficial: `https://dl.google.com/android/repository/android-ndk-r27c-linux.zip`, descomprimido con `unzip` (conserva los enlaces simbólicos) |
| El disco del sistema se llena al instalar paquetes o compilar | En Unity 6 la caché de paquetes está en `~/.cache/Unity` (no en `~/.config/unity3d/cache`); moverla al disco grande y dejar un enlace simbólico. Unity Hub descarga en su «Ubicación de descargas» (Ajustes → Instalaciones), no en `TMPDIR` |

## Después

El Hito 1 ya está confirmado. Prueba el ajuste de nitidez del Hito 1.2 y registra el resultado en `PROGRESO.md`. El **Hito 2: prototipo de página EPUB en el libro 3D** no está iniciado ni autorizado.
