# Folio XR

Nombre provisional de un lector propio de libros, cómics y manga para Meta Quest 3, con libros 3D, VR y passthrough. Repositorio: `loklj/el-m-s-perr-n-aqu-`.

**Estado: Fase 1, Hito 1 (libro 3D con un PDF).** El código está en `hito1/` y la guía para compilarlo e instalarlo en el Quest 3 en [docs/HITO1_GUIA.md](docs/HITO1_GUIA.md). Todavía no se ha compilado ni probado en el visor.

Empieza por [PROGRESO.md](PROGRESO.md): decisiones, estado, fases, riesgos y próximos pasos.

- [Arquitectura y estructura propuesta](docs/ARQUITECTURA.md).
- [Formatos y licencias](docs/FORMATOS_Y_LICENCIAS.md).
- [Importación, nubes, OPDS y guía Kindle](docs/IMPORTACION_Y_NUBES.md).
- [Preparación de Pop!_OS, compilación y pruebas en Quest](docs/PC_Y_VALIDACION.md).
- [Hito 1: guía paso a paso](docs/HITO1_GUIA.md).

La propuesta es Unity LTS con Meta XR y un módulo Android Kotlin/NDK. Solo se abrirán formatos y mecanismos de protección compatibles: nunca se quitará ni eludirá DRM. Las limitaciones y las verificaciones pendientes figuran en los documentos; no hay resultados de rendimiento ni pruebas de dispositivo todavía.
