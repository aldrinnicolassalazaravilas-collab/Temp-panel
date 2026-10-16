# Limitaciones conocidas — TempPanel 1.0.0

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
6. **Historial**: solo en memoria (máx. 900 puntos). CSV siempre en °C.
7. **SO probado**: Windows 10 Pro x64 sobre el hardware objetivo. Compatible con
   Windows 11 x64 sin cambios (misma API).
8. **Consumo**: ~260 MB de conjunto de trabajo con GUI abierta (autocontenida +
   WebView2); minimizada el motor sigue muestreando cada 2 s por diseño.
   Pausar congela gráficas e historial sin detener la lectura.
9. **Almacenamiento**: `MSStorageDriver_FailurePredictData` sin instancias y
   `Get-StorageReliabilityCounter` no disponible en este equipo; no hay fuente
   de temperatura de disco sin drivers de terceros.
9. **Notificación de batería**: solo avisa; no limita la carga física (sin método
   seguro/verificable para este modelo). No probada con carga real al 80 % (la
   batería estaba descargando durante las pruebas); lógica verificada con 15
   pruebas automatizadas + ventana OK verificada por automatización UIA.
