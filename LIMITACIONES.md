# Limitaciones conocidas — TempPanel 2.3.5

Verificadas en Lenovo IdeaPad Slim 3 15IRH10 (i7-13620H, 24 GB, Intel UHD).

1. **Temperaturas de CPU (RESUELTO 2026-10-16)**: LHM 0.9.x lee el procesador SOLO
   mediante el driver **PawnIO** (ya no usa WinRing0). Con PawnIO 2.2.0 instalado
   + app elevada: 23/23 temperaturas con valor, estado `ok`. Sin PawnIO no hay
   lecturas con ni sin admin. La app lo detecta y lo explica en Ayuda.
2. **GPU integrada sin temperatura independiente**: la Intel UHD no publica
   sensor térmico propio. Se muestran carga y frecuencia reales.
3. **Ventiladores sin RPM / almacenamiento sin sensor**: el EC del IdeaPad no
   publica RPM ni el NVMe expone temperatura vía LHM en este modo. Mensaje honesto.
4. **Batería**: % preciso (70–59 % verificado descargando), estado, alimentación
   externa, capacidades (50000/52550 mWh), descarga (30–35 W), autonomía y tensión
   verificados. Temperatura y ciclos: no expuestos por Windows; no se muestran.
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
9. **SO probado**: Windows 10 Pro x64 sobre el hardware objetivo. Compatible con
   Windows 11 x64 sin cambios (misma API).
10. **Consumo**: ~260 MB de conjunto de trabajo con GUI abierta (autocontenida +
    WebView2); minimizada el motor sigue muestreando cada 2 s por diseño.
    Pausar congela gráficas e historial sin detener la lectura.
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
