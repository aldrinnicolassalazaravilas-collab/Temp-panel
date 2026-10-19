# Limitaciones conocidas — TempPanel 2.5.0

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
5. **WebView2 Runtime**: necesario; preinstalado en la mayoría de equipos.
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
10. **Consumo (MEJORADO en 2.5.0)**: con la GUI abierta el conjunto de trabajo
    depende del sistema (autocontenida + WebView2) y se ve en
    **Resumen → Sistema → «Memoria de la app»**. Al minimizar, ocultar a la
    bandeja o abrir la mini ventana, el motor WebView2 se **suspende**
    (CPU en reposo y memoria liberada) mientras los sensores siguen
    muestreándose cada 2 s. La lista completa de sensores solo se lee cuando
    «Todos los sensores» está a la vista. Pausar congela gráficas e historial
    sin detener la lectura.
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
