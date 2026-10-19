# TempPanel — Monitor de hardware en tiempo real

Aplicación de escritorio para Windows (WPF + WebView2) que muestra temperaturas,
cargas, memoria, batería y ventiladores de tu portátil en una interfaz moderna.
En Windows 11 la ventana es **translúcida** (cristal acrílico del sistema con
esquinas redondeadas) y de estilo futurista-minimalista; en Windows 10 o si el
cristal no está disponible, se usa exactamente la misma interfaz en tema oscuro
sólido. Funciona 100 % en local, sin Internet.

## Instalar (sin programar nada)

1. Haz doble clic en `TempPanel-Setup-x64.exe` (carpeta `dist/`).
2. Elige la carpeta → marca «icono en el escritorio» si lo quieres → Instalar → Finalizar.
3. Abre **TempPanel** desde el escritorio o el menú Inicio.

No necesitas instalar .NET (autocontenida) ni abrir PowerShell o VS Code.
Si falta WebView2, el instalador te muestra el enlace oficial de Microsoft.

> Desde la v1.3.0 TempPanel pide elevación al arrancar (autorizado por el
> usuario): sin permisos de administrador no hay temperaturas en este equipo.

## Qué verás

- **Resumen**: CPU, carga, RAM y batería de un vistazo + gráficas.
- **CPU**: modelo, temperatura del paquete y por núcleo (si el equipo las expone),
  carga total y por núcleo/hilo, frecuencia, potencia, margen a TjMax y
  estadísticas de sesión (mín/máx/promedio).
- **GPU**: Intel UHD integrada (temperatura solo si existe sensor real).
- **RAM**: total, usada, disponible y %.
- **Batería**: nivel con **1 decimal** calculado con las capacidades reales del
  firmware (mWh, el mismo dato que usa Windows pero sin redondear a entero),
  estado de carga real (cargando / conectada sin cargar / retenida por el
  equipo), tendencia observada (%/hora medidos en la sesión, nunca inventados),
  potencia de carga, alimentación externa, autonomía estimada, capacidades de
  diseño/carga completa/restante, tensión y tasa de descarga (cuando
  Windows/LHM los exponen). Avisos al llegar al valor configurado y al
  completar la carga (99 %). Sin temperatura ni ciclos inventados.
- **Ayuda**: diagnóstico del driver de temperatura (funcionando, bloqueado,
  sin intentar…) para saber exactamente por qué no hay lecturas.
- **Refrigeración**: RPM si el EC las publica + temperaturas auxiliares +
  temperatura de almacenamiento (NVMe) si existe sensor.
- **Todos los sensores**: tabla completa con filtro en vivo y ordenación por
  cualquier columna (clic en la cabecera); los que no tienen lectura quedan al
  final, nunca como cero.
- **Historial**: 60 s / 5 min / 15 min / 30 min, mín/máx/promedio, pausar,
  exportar CSV (siempre en °C), guardar sesión, limpiar.
- **Configuración**: intervalo 1–10 s, °C/°F, umbrales térmicos (85/95 CPU por
  defecto, con histéresis), alerta de batería, inicio con Windows (off por
  defecto), iniciar minimizado, restablecer ajustes, versión.

> «No disponible» significa que tu hardware no publica ese sensor. Nunca se
> muestran cifras inventadas.

## Interfaz translúcida y consumo (novedad v2.5.0; v2.5.1 corrige la ventana en negro)

- **v2.5.1 — ventana en negro corregida**: la 2.5.0 podía abrirse sin interfaz
  (faltaba un ensamblado del SDK de Windows que necesita el cristal). La 2.5.1 lo
  incluye y, si el cristal no está disponible, la app pasa sola al tema oscuro
  opaco en vez de quedarse vacía.
- **Cristal real de Windows 11**: la ventana no tiene marco; el fondo es
  acrílico translúcido con esquinas redondeadas dibujado por Windows (efecto
  «Mica»/«Acrílico»), y la interfaz se adapta a él. La barra superior
  personalizada permite arrastrar la ventana y tiene los botones
  minimizar / maximizar / cerrar.
- **Sin Windows 11 no se rompe nada**: la app detecta si el cristal está
  disponible y, si no, usa el mismo diseño en tema oscuro sólido.
- **Menos RAM**: al minimizar, ocultar a la bandeja o usar la mini ventana, el
  motor de la interfaz (WebView2) se **suspende** y libera memoria; los
  sensores siguen midiendo. Además, la lista completa de sensores solo se lee
  cuando abres «Todos los sensores».
- **Precisión térmica**: se descartan lecturas imposibles (0 °C, negativas o
  superiores a 130 °C) que algunos drivers devuelven como «fantasma», en lugar
  de mostrarlas como si fueran reales.

### La interfaz, ahora en Svelte (más interactiva e inmersiva)

- **Escrita en Svelte 5 y compilada a HTML/JS/CSS local**: sigue sin Internet,
  sin CDN y sin dependencias en tiempo de ejecución. Los datos y su significado
  son exactamente los mismos que antes.
- **Fondo vivo**: auras animadas que cambian de color según la temperatura del
  equipo y el estado de la batería (decorativo: no son datos), rejilla sutil y
  brillo que recorre la pantalla. Se respeta «reducir movimiento» de Windows.
- **Transparencia ajustable**: en Configuración → Apariencia puedes mover el
  cristal de más transparente a más sólido; la preferencia se guarda en el
  equipo y no toca `config.json` (es solo de la interfaz).
- **Modo inmersivo** (botón o tecla **F**): la barra lateral y la cabecera se
  apartan y el fondo gana protagonismo; **Esc** vuelve.
- **Gráficas interactivas**: pasa el ratón para ver hora y valor exacto, haz
  clic para **fijar** un punto (📌) y **doble clic** para quitarlo. También se
  navegan con las flechas del teclado (←/→ mueven la guía, Intro fija). Las
  gráficas se dibujan al abrir la página y muestran mín/máx/promedio reales.
- **Tarjetas con foco**: el resaltado sigue al cursor; los medidores y anillos
  se animan al cambiar de valor (el número siempre es el valor exacto, nunca un
  valor intermedio inventado).
- **Atajos de teclado**: **1-9** y **0** cambian de página, **F** inmersivo,
  **P** pausa (congela gráficas e historial; los sensores siguen midiendo),
  **R** pide una lectura ahora y **M** abre la mini ventana.
- **Avisos flotantes** para acciones (copiar resumen, guardar, exportar) en
  lugar de ventanas de diálogo, y pestañas que se deslizan con transición.

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

Botón **Mini** (arriba a la derecha) o desde la bandeja: ventana flotante que se
puede fijar encima de otras apps, con temperatura, carga, batería, RAM y GPU.
Minigráfico suavizado con relleno degradado y **leyenda conmutable**: haz clic
en cada serie (CPU/Temp/Bat/RAM/GPU) para mostrarla u ocultarla. Botón **⤢**
para **ampliar o reducir**: en tamaño ampliado aparecen chips con RAM, GPU,
potencia y el rango térmico de la sesión, y el gráfico crece. La posición se
guarda sola y «Configuración → Mini ventana flotante» permite elegir las
series, el tamaño inicial y recentrarla. Arrastrable, sin robar el foco:
sigue visible mientras usas otras aplicaciones.

## Desinstalar

Panel de control → Programas → TempPanel → Desinstalar. Solo borra lo suyo.

## Actualizaciones

- En **Configuración → Versión** pulsa «Buscar actualizaciones».
- Si hay release nueva en GitHub, puedes descargarla e instalarla desde la app.
- La comprobación automática está **desactivada por defecto** (la app no usa
  Internet salvo que lo pidas o la actives).
- Sin Internet o sin releases, la app sigue funcionando en local sin errores.
- Si Windows Defender retiene el instalador unos segundos (bloqueo de archivo),
  en **Ayuda → Antivirus, Defender y SmartScreen** puedes añadir o quitar una
  exclusión de Defender **solo para las rutas de TempPanel** (reversible, no
  desactiva ninguna protección). El aviso azul de SmartScreen del navegador
  solo desaparece con un certificado de firma de pago; desde la propia app el
  instalador sale con menos fricción (se quita la marca de Internet).

## Para desarrolladores

- `TempPanel.sln` — `TempPanel/` (WPF .NET 8) + `TempPanel.Tests/` (xUnit).
- Capas: `App/MainWindow` (WPF+WebView2), `Web/` (fuentes **Svelte 5 + Vite** de
  la interfaz) → `Frontend/` (salida compilada: HTML/JS/CSS planos, sin CDN),
  `Sensors/` (lectura LHM 0.9.6 + WMI + `BatteryReader`), `Services/` (alerta
  batería, notificaciones, arranque con Windows, actualizador, exclusiones de
  Defender), `WindowFx.cs` (cristal DWM), bandeja, instalador, pruebas.
- Comunicación frontend↔backend por **WebMessage** (sin HTTP, sin red).
- Interfaz: `cd TempPanel\Web` → `npm install` (solo la primera vez) →
  `npm run build`. El resultado se commitea en `TempPanel/Frontend/`, así que
  **publicar la app no necesita Node**.
- Comprobaciones: `node uitest\run.js` (52 pruebas DOM reales sobre el bundle,
  con JSDOM), `node uitest\preview.js` (genera `Frontend/_preview.html` con el
  puente emulado y datos reales del `--selftest`), `node uitest\csslint.js`
  (paréntesis desbalanceados en CSS, error silencioso clásico).
- `dotnet test TempPanel.sln -c Release` (70 pruebas) · publish autocontenido
  win-x64 · `--selftest` escribe `%TEMP%\temppanel-selftest.json` sin GUI.
- Instalador: `TempPanel/InnoSetup/setup.iss` (Inno Setup 6).
- Actualizador: `Services/UpdateService.cs` lee `releases/latest` de
  `aldrinnicolassalazaravilas-collab/Temp-panel` y descarga el asset
  `TempPanel-Setup-x64.exe`. Para publicar: sube versión en `.csproj` +
  `setup.iss`, compila el instalador y crea el Release en GitHub con el exe.

## Limitaciones conocidas

Ver `LIMITACIONES.md` y `INFORME-PRUEBAS.md`.
