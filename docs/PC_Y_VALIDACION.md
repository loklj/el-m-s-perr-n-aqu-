# Preparación y validación en Pop!_OS y Quest 3

Fase 0 — instrucciones para el trabajo posterior, 5 de noviembre de 2026. **Todavía no existe proyecto Unity/Android ni APK: los pasos de compilación no pueden ejecutarse con el contenido actual del repositorio.** No se han probado en este entorno. Aplicarlos al comenzar la Fase 1, después del OK del usuario, y actualizar este documento con versiones y resultados reales.

## Preparar el PC

1. Anotar versión de Pop!_OS, CPU, GPU/controlador, RAM y espacio libre. Para consultar sin instalar nada:

   ```bash
   cat /etc/os-release
   uname -m
   free -h
   df -h .
   lspci | grep -E 'VGA|3D|Display'
   ```

2. Consultar los requisitos Linux de la versión LTS candidata en la documentación oficial de Unity y la matriz Meta XR. Pop!_OS deriva de Ubuntu, pero eso no garantiza soporte oficial. Si el Editor o su instalador falla por distribución/controladores, usar una instalación de Ubuntu LTS expresamente admitida por esa versión; no desactivar comprobaciones TLS ni instalar dependencias al azar para ocultar el error.
3. Instalar Unity Hub para Linux siguiendo <https://docs.unity3d.com/hub/manual/InstallHub.html>. En Hub, instalar la LTS compatible elegida con **Android Build Support**, **Android SDK & NDK Tools** y **OpenJDK**. Usar primero los componentes suministrados por ese Editor. Revisar los términos y elegibilidad de la licencia Unity vigente.
4. Instalar Android Studio para Linux desde <https://developer.android.com/studio/install> si se va a editar el módulo Kotlin/NDK. El proyecto Unity exportado puede requerir versiones distintas a un proyecto Android Studio recién creado: respetar las versiones fijadas, no actualizar automáticamente Gradle/AGP.
5. Instalar las herramientas de plataforma Android (`adb`) desde el SDK Manager de Android Studio. Anotar la ruta mostrada por el SDK Manager y añadir su subdirectorio `platform-tools` al PATH del usuario, o invocar `adb` con la ruta completa. Comprobar `adb version`.
6. Cuando se creen los proyectos, guardar versión exacta del Editor, paquetes Meta/XR y sus lockfiles, SDK/NDK/JDK, AGP/Gradle/Kotlin, arquitectura ARM64, backend gráfico y modo de compilación. No hay versiones exactas fijadas en esta Fase 0 porque no se ha verificado una combinación.

No es necesario instalar rclone, MEGA, credenciales cloud ni generar corpus gigante para el MVP local. En Linux, desplegar APK al visor es el camino base; no depender de Quest Link para PC, que no se presupone disponible en este sistema.

## Preparar el visor para pruebas locales

1. Seguir el procedimiento vigente de Meta para una cuenta/organización de desarrollador y completar la verificación que Meta solicite. Fuente de referencia: <https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/>. La interfaz de registro puede cambiar; no compartir claves o contraseñas en el repositorio.
2. Vincular el Quest 3 con la cuenta correspondiente y activar el modo desarrollador mediante la app móvil Meta Horizon, en la configuración del visor, según las instrucciones actuales. Reiniciar si el propio procedimiento lo indica.
3. Conectar un cable USB que transfiera datos, desbloquear el visor y aceptar el diálogo de depuración USB desde el PC de confianza.
4. En una terminal ejecutar:

   ```bash
   adb devices -l
   ```

   Debe aparecer un dispositivo con estado `device`. Si aparece `unauthorized`, aceptar el permiso dentro del visor y repetir. Si no aparece, comprobar cable, modo desarrollador, conexión y permisos USB/udev de Linux siguiendo la guía Android: <https://developer.android.com/studio/run/device>. No resolverlo ejecutando toda la sesión como root.

5. Como alternativa de instalación, consultar SideQuest en <https://sidequestvr.com/setup-howto> y elegir una distribución Linux vigente si está disponible. También requiere modo desarrollador y autorización USB. Si no hay instalador compatible con Pop!_OS, continuar con `adb`; no es necesario SideQuest para instalar un APK propio.

## Crear y compilar cuando comience la Fase 1

Estos pasos describen el procedimiento previsto. Los nombres de menús pueden variar con el Editor; la Fase 1 documentará los exactos de la versión elegida.

1. Abrir en Unity Hub el proyecto que se creará en `unity/`, usando su `ProjectVersion.txt`. Instalar los paquetes Meta/XR mediante sus fuentes oficiales y fijar las versiones verificadas.
2. Crear una escena mínima con cámara/rig XR, controles, interacción y passthrough según los ejemplos de ese SDK; validar su configuración antes de integrar lectores.
3. Seleccionar Android en Build Profiles/Build Settings. Configurar ARM64 e IL2CPP. Fijar el identificador de paquete y el mínimo/objetivo Android que requieran el SDK y la distribución seleccionados. No se especifican números inventados como requisito de Quest.
4. Elegir y probar un backend gráfico compatible con Meta y con el renderizador nativo. No alternar Vulkan/GLES para “arreglar” texturas sin revisar la implementación del puente.
5. Cuando exista el wrapper Gradle y el módulo propuesto `reader`, compilar su AAR desde `android/` con la tarea que se documente allí. Si mantiene ese nombre, la tarea estándar prevista es `./gradlew :reader:assembleDebug`; **no está disponible aún**. Integrar el AAR y sus dependencias con el procedimiento reproducible del proyecto, evitando copias manuales divergentes.
6. Hacer un Development Build desde Unity para inspección y generar el APK. En el primer hito usar un libro propio pequeño, no un corpus gigante. Conservar logs y anotar qué bibliotecas nativas se empaquetan para ARM64.
7. Medir el rendimiento final también en una compilación comparable a distribución: un build de desarrollo y el Profiler pueden alterar los resultados.

No existe todavía un método Unity de compilación por línea de comandos ni CI; se añadirá y probará durante Fase 1 antes de dar un comando de automatización como funcional.

## Instalar, actualizar y obtener registros

Cuando exista un APK, sustituir los marcadores por la ruta y el identificador reales registrados por la Fase 1:

```bash
adb devices -l
adb install -r '/ruta/real/al/FolioXR.apk'
```

Abrir la aplicación desde el apartado de aplicaciones de orígenes desconocidos del visor, cuyo nombre puede variar con Horizon OS. La actualización debe conservar el mismo identificador y firma; `-r` solicita reinstalación conservando datos. Si Android rechaza una firma diferente, no desinstalar sin exportar antes la biblioteca que se quiera conservar. Desinstalar o borrar datos elimina el almacenamiento privado.

Para guardar un log de una prueba, iniciar esta captura justo antes, reproducir el caso y terminar con Ctrl+C:

```bash
adb logcat -v threadtime > folio-xr-prueba.log
```

Revisar y quitar datos personales antes de compartirlo; el log general puede incluir información de otras aplicaciones. La app tendrá su propio exportador de diagnóstico con redacción de datos sensibles.

Para consultar memoria una vez que se conozca el paquete, reemplazar el marcador:

```bash
adb shell dumpsys meminfo IDENTIFICADOR_REAL_DEL_PAQUETE
```

No es una medición completa de FPS o memoria GPU. Usar además Unity Profiler y herramientas de rendimiento Meta compatibles con Linux/Quest y la versión elegida; comprobar su disponibilidad antes de depender de ellas. No hay resultados de rendimiento obtenidos aquí.

## Pruebas de aceptación por hito

| Hito | Pasos en PC/Quest | Resultado exigido |
| --- | --- | --- |
| Arranque XR | Instalar, abrir VR/passthrough, probar ambos Touch y manos | Escena estable, permisos claros y recuperación de pérdida de seguimiento |
| EPUB a libro 3D | Abrir un EPUB propio con texto, imágenes y TOC; cambiar tamaño/layout y pasar páginas | Página legible sobre la malla, locator consistente, sin presentarlo como panel plano terminado |
| Lectores locales | Importar PDF, CBZ/ZIP y EPUB desde Download mediante selector; cancelar otra importación | Copia privada, portada/lectura, progreso correcto y cancelación sin archivo “listo” falso |
| Persistencia | Avanzar páginas y marcar; cerrar y abrir, reiniciar visor, actualizar APK compatible | Libros, progreso confirmado, marcadores y ajustes conservados |
| Recuperación | Interrumpir una importación y volver a abrir; probar poco espacio y permiso revocado | Recuperar o explicar el estado; no duplicar ni perder otros libros |
| Respaldo | Exportar un respaldo, validar su contenido y restaurarlo en una biblioteca de prueba | Libros/ajustes según opciones, hashes correctos y recuperación sin credenciales exportadas |
| DRM/errores | Usar muestras legales con protección conocida, PDF con contraseña, watermark, formato pendiente y archivo truncado | Diagnósticos distintos, apertura de watermark y PDF con contraseña correcta, sin intentos de eludir DRM |
| Tres libros | Abrir PDF, cómic y EPUB juntos, alternar páginas, agarrar y fijar | Presupuesto compartido, liberación al cerrar y rendimiento medido |
| Posturas | Leer de pie, sentado y acostado; recenter, pausa y quitar/poner visor | Orientación cómoda, estado conservado y controles accesibles |
| Nubes/OPDS (Fase 3) | Autenticar en navegador, explorar, cancelar/reanudar y leer sin conexión | Permisos adecuados y archivo completo; probar cada servidor/conector por separado |
| Estrés (Fase 4) | Ejecutar generadores y pruebas definidas en PROGRESO.md | Informes con corpus, dispositivo, versiones, tiempos, memoria y temperatura |

No usar un libro comprado como material de repositorio ni intentar obtener una muestra quitando protección. Crear material propio o usar fixtures cuya redistribución esté autorizada; los libros personales de prueba permanecen fuera de Git.

## Cierre de Fase 0

- [x] Arquitectura, formato de biblioteca, riesgos y fases documentados.
- [x] Instrucciones para el trabajo que requiere PC y visor registradas.
- [x] Identificado qué exige documentación vigente y una prueba física.
- [ ] Compilar Unity/Android: corresponde a Fase 1; no hay proyecto todavía.
- [ ] Instalar en Quest y validar lectura/controles: corresponde a Fase 1.
- [ ] OAuth real, soporte de todos los formatos y rendimiento gigante: fases posteriores.

Las casillas pendientes son validaciones futuras explícitas, no resultados fallidos de una aplicación existente. No avanzar sin el OK de Fase 1.
