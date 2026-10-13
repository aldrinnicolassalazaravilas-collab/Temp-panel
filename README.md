# TempPanel — Monitor de hardware en tiempo real

Aplicación de escritorio para Windows (WPF + WebView2) que muestra temperaturas,
cargas, memoria, batería y ventiladores de tu portátil en una interfaz moderna
(tema oscuro, azul eléctrico y cian). Funciona 100 % en local, sin Internet.

## Instalar (sin programar nada)

1. Haz doble clic en `TempPanel-Setup-x64.exe` (carpeta `dist/`).
2. Elige la carpeta → marca «icono en el escritorio» si lo quieres → Instalar → Finalizar.
3. Abre **TempPanel** desde el escritorio o el menú Inicio.

No necesitas instalar .NET (autocontenida) ni abrir PowerShell o VS Code.
Si falta WebView2, el instalador te muestra el enlace oficial de Microsoft.

## Qué verás

- **Resumen**: CPU, carga, RAM y batería de un vistazo + gráficas.
- **CPU**: modelo, temperatura del paquete y por núcleo (si el equipo las expone),
  carga total y por núcleo/hilo, frecuencia, potencia, margen a TjMax y
  estadísticas de sesión (mín/máx/promedio).
- **GPU**: Intel UHD integrada (temperatura solo si existe sensor real).
- **RAM**: total, usada, disponible y %.
- **Batería**: % preciso, estado, alimentación externa, autonomía estimada,
  capacidades de diseño/carga completa, tensión y tasa de descarga (cuando
  Windows/LHM los exponen). Avisos al llegar al valor configurado y al
  completar la carga (99 %). Sin temperatura ni ciclos inventados.
- **Ayuda**: diagnóstico del driver de temperatura (funcionando, bloqueado,
  sin intentar…) para saber exactamente por qué no hay lecturas.
- **Refrigeración**: RPM si el EC las publica + temperaturas auxiliares +
  temperatura de almacenamiento (NVMe) si existe sensor.
- **Todos los sensores**: tabla completa con filtro.
- **Historial**: 60 s / 5 min / 15 min / 30 min, mín/máx/promedio, pausar,
  exportar CSV (siempre en °C), guardar sesión, limpiar.
- **Configuración**: intervalo 1–10 s, °C/°F, umbrales térmicos (85/95 CPU por
  defecto, con histéresis), alerta de batería, inicio con Windows (off por
  defecto), iniciar minimizado, restablecer ajustes, versión.

> «No disponible» significa que tu hardware no publica ese sensor. Nunca se
> muestran cifras inventadas.

## Alerta de batería al 80 % (prioritaria)

Mientras TempPanel se ejecuta (incluso minimizado), vigila la batería cada
15 s. Al llegar al umbral cargando, muestra «TempPanel — Batería al 80 %»
(ventana con botón **OK** o notificación de Windows, a elegir) y no repite
hasta un nuevo ciclo (reactivación bajo el 75 %). Notificar **no** detiene la
carga física: ningún método seguro existe para este modelo, así que solo avisa.

## Bandeja del sistema

Cerrar la ventana minimiza a la bandeja (sigue monitorizando). Doble clic o
«Abrir» restaura; «Salir» cierra del todo. Solo puede haber una instancia:
abrir de nuevo restaura la existente. El icono muestra el resumen vivo
(CPU, carga y batería). En Ajustes hay botón para abrir la carpeta de registros.

## Mini ventana

Botón **Mini** (arriba a la derecha) o desde la bandeja: ventana pequeña que se
puede fijar encima de otras apps, con temperatura, carga, batería y minigráfico.
Arrastrable, sin foco robado; doble clic o ✕ vuelve a la principal.

## Desinstalar

Panel de control → Programas → TempPanel → Desinstalar. Solo borra lo suyo.

## Actualizaciones

- En **Configuración → Versión** pulsa «Buscar actualizaciones».
- Si hay release nueva en GitHub, puedes descargarla e instalarla desde la app.
- La comprobación automática está **desactivada por defecto** (la app no usa
  Internet salvo que lo pidas o la actives).
- Sin Internet o sin releases, la app sigue funcionando en local sin errores.

## Para desarrolladores

- `TempPanel.sln` — `TempPanel/` (WPF .NET 8) + `TempPanel.Tests/` (xUnit).
- Capas: `App/MainWindow` (WPF+WebView2), `Frontend/` (HTML/CSS/JS),
  `Sensors/` (lectura LHM 0.9.5 + WMI), `Services/` (alerta batería,
  notificaciones, arranque con Windows), bandeja, instalador, pruebas.
- Comunicación frontend↔backend por **WebMessage** (sin HTTP, sin red).
- `dotnet test TempPanel.sln -c Release` · publish autocontenido win-x64.
- Instalador: `TempPanel/InnoSetup/setup.iss` (Inno Setup 6).
- Actualizador: `Services/UpdateService.cs` lee `releases/latest` de
  `aldrinnicolassalazaravilas-collab/Temp-panel` y descarga el asset
  `TempPanel-Setup-x64.exe`. Para publicar: sube versión en `.csproj` +
  `setup.iss`, compila el instalador y crea el Release en GitHub con el exe.

## Limitaciones conocidas

Ver `LIMITACIONES.md` y `INFORME-PRUEBAS.md`.
