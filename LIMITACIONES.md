# Limitaciones conocidas — TempPanel 3.0.0

Verificadas en Lenovo IdeaPad Slim 3 15IRH10 (i7-13620H, 24 GB, Intel UHD)
con Windows 11 Pro (build 26200).

1. **Temperaturas de CPU (RESUELTO 2026-10-16)**: LHM 0.9.x lee el procesador SOLO
   mediante el driver **PawnIO** (ya no usa WinRing0). Con PawnIO 2.2.0 instalado
   + app elevada: 23/23 temperaturas con valor, estado `ok`. Sin PawnIO no hay
   lecturas con ni sin admin. La app lo detecta y lo explica en Ayuda.
   Desde 2.5.0 se descartan las lecturas imposibles (≤ 2 °C o > 130 °C) que
   algunos drivers devuelven como 0/negativas: se tratan como «No disponible».
2. **GPU integrada sin temperatura independiente**: la Intel UHD no publica
   sensor térmico propio. Se muestran carga y frecuencia reales.
3. **Ventiladores sin RPM / almacenamiento sin sensor**: el EC del IdeaPad no
   publica RPM ni el NVMe expone temperatura vía LHM en este modo. Mensaje honesto.
4. **Batería (MEJORADA en 2.5.0)**: nivel con 1 decimal desde las capacidades
   reales del firmware (`root\wmi`): p. ej. 92.9 % = 49040/52800 mWh, en vez del
   entero 92 % que da `Win32_Battery`. Estado de carga real desde
   `BatteryStatus` (Charging / PowerOnline), que sí distingue «cargando» aunque
   `Win32_Battery` diga «sin cargar» (2). Verificado: diseño 50000 mWh,
   capacidad completa 52800 mWh, carga a ~18–22 W con el nivel subiendo.
   Los centinelas de Windows (`EstimatedRunTime` = 71582788 → «desconocido») se
   filtran y quedan «No disponible» en vez de mostrarse como autonomía falsa.
   Si el portátil **retiene** el nivel (modo conservación ~80 %), la app lo dice
   tal cual («Cargando · retenida por el equipo») y mide la tendencia observada
   en lugar de fingir que sube. Temperatura y ciclos: no expuestos por Windows;
   no se muestran.
5. **WebView2 Runtime**: necesario; preinstalado en la mayoría de equipos. La
   transparencia real (cristal) usa el control con composición de WebView2 y el
   acrílico de Windows 11; si el sistema no lo soporta (o falla al inicializar),
   la app pasa sola al modo opaco y avisa en el registro. Nunca deja la ventana
   en negro (bug corregido en 2.5.1).
6. **Aviso de SmartScreen**: el instalador no está firmado con un certificado de
   pago, así que al abrirlo desde el navegador puede aparecer «Windows protegió
   tu PC» (Más información → Ejecutar de todos modos). Descargándolo desde la
   propia app ese aviso es menos frecuente (se quita la marca de Internet del
   archivo). No se puede eliminar del todo sin firmar el binario.
7. **Defender y bloqueos de archivo**: el antivirus puede retener el instalador
   recién descargado unos segundos. La app reintenta abrirlo (10 intentos x 3 s)
   y, si aun así molesta, en **Ayuda → Antivirus, Defender y SmartScreen** hay
   botones para añadir o quitar una exclusión **solo de las rutas de TempPanel**
   (carpeta instalada + `TempPanel-Setup-*.exe`). Reversible; no desactiva nada.
8. **Historial**: solo en memoria (máx. 900 puntos). CSV siempre en °C.
9. **SO probado**: Windows 11 Pro (build 26200) en el hardware objetivo.
   Compatible con Windows 10 x64 y Windows 11: en Windows 10 (o si el cristal
   acrílico no está disponible) la app usa el tema oscuro sólido, con las
   mismas funciones. El cristal translúcido y las esquinas redondeadas son
   característica de Windows 11.
10. **Consumo (MEJORADO en 2.5.0 y 3.0.0)**: con la GUI abierta el conjunto de
    trabajo depende del sistema (autocontenida + WebView2) y se ve en
    **Resumen → Sistema → «Memoria de la app»**. Al minimizar, ocultar a la
    bandeja, abrir la mini ventana, bloquear la sesión o tener otra app a
    pantalla completa, el motor WebView2 se **suspende** (CPU en reposo y
    memoria liberada) y los sensores pasan a 5 s; sin foco visible, mínimo 3 s.
    En primer plano se respeta el intervalo elegido. La reducción adaptativa se
    puede desactivar en **Configuración → Integración con Windows**.
    La lista completa de sensores solo se lee cuando «Todos los sensores» está
    a la vista, y la tabla ahora se virtualiza (solo se dibujan las filas
    visibles). Pausar congela gráficas e historial sin detener la lectura.
11. **Almacenamiento**: `MSStorageDriver_FailurePredictData` sin instancias y
    `Get-StorageReliabilityCounter` no disponible en este equipo; no hay fuente
    de temperatura de disco sin drivers de terceros.
12. **Mini ventana (v2.3.5)**: series conmutables, tamaño ampliado y posición
    persistente. La posición se guarda al mover/cerrar; si cambia el número de
    monitores, se recoloca sola para no quedar fuera de pantalla.
13. **Notificación de batería**: solo avisa; no limita la carga física (sin método
    seguro/verificable para este modelo). No probada con carga real al 80 % (la
    batería estaba descargando durante las pruebas); lógica verificada con
    pruebas automatizadas + ventana OK verificada por automatización UIA.
14. **Atajo global Ctrl+Alt+T (v3.0.0)**: si otra aplicación ya lo usa, Windows
    no lo concede; Configuración → Integración con Windows muestra el estado
    real («No registrado») y se puede desactivar. Como cualquier atajo de
    Windows, no se recibe mientras un juego exclusivo a pantalla completa tiene
    el foco (en ese caso la app ya está reducida a 5 s).
15. **Snap Layouts (v3.0.0)**: el menú de distribuciones al pasar el ratón por
    maximizar es una función de **Windows 11**. En Windows 10 el botón sigue
    maximizando/restaurando con normalidad, pero el sistema no muestra el menú.
    Solo se activa en modo cristal (el modo opaco conserva el clic normal del
    HTML).
16. **Barra de tareas (v3.0.0)**: el distintivo aparece cuando la interfaz
    detecta una alerta térmica (amarillo/rojo) y el progreso solo mientras la
    batería está cargando. Con la ventana en la bandeja no hay botón en la barra
    de tareas (no hay nada que mostrar), pero el tooltip del icono sigue vivo.
17. **Tema y color de acento (v3.0.0)**: TempPanel conserva su diseño oscuro
    aunque Windows esté en tema claro; el color de acento del sistema se usa en
    los anillos de foco y se muestra en Configuración. El **alto contraste** sí
    se respeta: refuerza bordes, texto y desactiva animaciones. Cambiar el tema
    o el acento de Windows se refleja sin reiniciar la app.
18. **Detección de pantalla completa (v3.0.0)**: se considera pantalla completa
    cualquier ventana de otro proceso que cubra al menos el 98 % de su monitor
    (juegos, vídeo, presentaciones). Es intencionado para ahorrar mientras no
    miras la app; si te molesta que una presentación reduzca el ritmo, desactiva
    la reducción adaptativa.
19. **Arquitectura**: la build publicada es **x64**. En Windows 11 ARM64
    funciona por emulación (más lenta que una build nativa); no hay binario
    ARM64 publicado. Windows 10 x64 está soportado con el tema sólido.
20. **Ventana mínima 820×560 (v3.0.0)**: por debajo de 980 px de ancho la barra
    lateral pasa a iconos para no recortar el contenido; no se admite una
    ventana más pequeña que 820×560 (evita gráficas ilegibles).
