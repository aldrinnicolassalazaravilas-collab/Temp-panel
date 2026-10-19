# Informe de pruebas — TempPanel 2.5.0

Fecha: 2026-10-18. Equipo: Lenovo 83K1, i7-13620H, 24 GB, Intel UHD, Windows 11
Pro (build 26200). Distingue pruebas automatizadas, compilación y pruebas reales
en el dispositivo. Todo lo que aparece aquí se ejecutó de verdad; lo que no se
pudo comprobar se dice explícitamente en §9.

## 1. Compilación Release

- `dotnet test TempPanel.sln -c Release` → **70/70 pruebas superadas, 0 errores**
  (incluye las nuevas de precisión: tasas de carga/descarga coherentes, nivel con
  capacidades reales, filtro de plausibilidad térmica).
- `dotnet publish -r win-x64 --self-contained` → `TempPanel.exe` 167,6 MB;
  `DebugType=embedded`: no se publica ningún `.pdb` suelto.
- Interfaz: `npm run build` en `TempPanel/Web` → 138 módulos, `assets/app.js`
  123,7 kB + `assets/style.css` 17,2 kB, script clásico y rutas relativas.

## 2. Pruebas automatizadas

- **xUnit (backend)**: 70/70 (`dotnet test -c Release`).
  - Tasas de batería: con el firmware publicando el mismo valor en carga y
    descarga (caso real de este portátil), solo se muestra la dirección que el
    firmware confirma; la otra queda en «No disponible».
  - Alerta de batería: dispara cargando al umbral, no descargando, una vez por
    ciclo, rearma por debajo del valor, valida rearma < alerta, nivel nulo nunca
    dispara. Niveles simulados SOLO en tests.
- **DOM real sobre el bundle compilado** (`node uitest\run.js`) → **49/49 OK**:
  montaje, cristal (`body.glass`), barra de título y botones de ventana,
  10 páginas, tarjetas/medidor/anillo/minigráficos/gráficas, valores concretos
  del snapshot sintético, sesión CPU, tendencia calculada, barras por núcleo,
  TjMax, selector de sensor, GPU, RAM, batería (1 decimal, fuente, tendencia,
  estado de carga real, flujo de carga), tabla de sensores con orden por columna,
  historial (chips, gráfica, estadísticas), ayuda (diagnóstico, Defender, HVCI),
  configuración (chips de mini ventana, versión, transparencia),
  **protocolo completo de 29 comandos** hacia la app nativa, bienvenida y su
  comando, banner crítico a 96 °C y pausa.
- **CSS**: `node uitest\csslint.js` → 197 bloques (fuente) + 200 (compilado),
  0 paréntesis desbalanceados (la errata que dejó sin efecto el degradado de
  `#app::before` en v2.5.0 se detectó y corrigió con esta herramienta).

## 3. Interfaz (verificada en navegador real, no solo en tests)

Sobre el bundle compilado con el puente de WebView2 emulado y datos reales del
`--selftest` (`uitest/preview.js` → `Frontend/_preview.html`), comprobado en un
navegador Chromium con consultas al DOM:

- Barra de título de 44 px (contrato de arrastre nativo) y 3 botones de ventana;
  barra lateral de 232 px; 10 entradas de navegación; radio de tarjeta 18 px.
- `body.glass` activo con `--panel-a` en 0,26 (transparencia ajustable en vivo) y
  auras cuyo color cambia con la temperatura del equipo.
- 4 tarjetas de resumen, medidor SVG con su arco, anillo de batería, 4
  minigráficos y 2 gráficas canvas en Resumen; 9 páginas con contenido real.
- Interacción verificada: guía de la gráfica con hora y valor exacto, punto
  fijado con clic (📌) y quitado con doble clic, filtro de sensores en vivo
  («core» → 69/122 filas), orden por Valor (ascendente/descendente, nulos al
  final), atajos de teclado (3 → GPU, F → inmersivo, P → pausa), aviso flotante
  al copiar el resumen y `view:<página>` enviado al cambiar de página.
- **Cero errores en la consola del navegador** durante todo el recorrido.

## 4. Sensores reales (equipo del usuario, `--selftest` 2026-10-18 20:30:03)

- CPU i7-13620H: **paquete 82 °C** en carga, 23/23 sensores de temperatura de CPU
  con valor (driver `ok`), carga total 20,5 %, 16 cargas núcleo/hilo.
- RAM: 18.770 MB usados de 24.237 MB (77,4 %).
- Batería: **61,5 %** calculado con las capacidades reales del firmware
  (diseño 50.000 mWh, carga completa 52.800 mWh, restante 32.450 mWh),
  estado «Descargando», alimentación «Con batería», tensión 11,35 V,
  autonomía estimada 60 min.
- **Tasas coherentes**: `DischargeRateW` = 32,3 W y **`ChargeRateW` = null**
  (antes el firmware publicaba los mismos 32,6 W en ambos campos y la app
  mostraba una carga que no existía). Corregido y verificado en el equipo real.
- Tendencia por hora: nula con una sola muestra (correcto: no se inventa).
- 122 sensores detectados, 121 con valor; 0 ventiladores (el EC no publica RPM:
  se informa, no se rellena). Temperatura y ciclos de batería: no expuestos.
- Memoria del proceso: se muestra en Resumen → Sistema (`appMemoryMb`).

## 5. Alerta de batería

- Ventana `--test-battery-alert` con título y botón OK (automatización UIA).
- Lógica de umbral/rearme cubierta por pruebas unitarias (§2).
- **No verificada físicamente** en esta pasada: la batería estaba descargando
  (61,5 %) y el equipo cargado; ver §9.

## 6. Instalador

- `dist\TempPanel-Setup-x64.exe` recompilado con Inno Setup 6 (48,8 MB,
  2026-10-18 20:32) a partir de `publish\v2.5.0\`, que contiene el `Frontend/`
  **compilado** (`index.html` + `assets/app.js` + `assets/style.css`) y ningún
  `.pdb` suelto.
- Sigue siendo por usuario (`PrivilegesRequired=lowest`): instala en
  `%LocalAppData%\Programs\TempPanel` y registra en `HKCU\...\Uninstall`.
- La app instalada no necesita Node, ni .NET, ni Internet.

## 7. Consumo y memoria

- WebView2 se **suspende** al minimizar/ocultar a la bandeja o al usar la mini
  ventana (motor de interfaz en pausa; los sensores siguen midiendo).
- La tabla completa de sensores solo se construye cuando la página está abierta
  (`view=sensores`); el resto de páginas no la recibe.
- GC de estación de trabajo no concurrente y `meta.appMemoryMb` visible en la
  interfaz para poder comprobarlo sin herramientas externas.

## 8. Offline

- Sin CDN, sin fuentes externas y sin dependencias en tiempo de ejecución: el
  bundle es JS/CSS local y la comunicación con la app es WebMessage.
- La app solo usa Internet si pulsas «Buscar actualizaciones» o activas la
  comprobación automática (desactivada por defecto).

## 9. Lo que NO se pudo verificar (y por qué)

- **GUI en el escritorio real**: la app pide elevación y el usuario canceló el
  aviso de UAC durante las pruebas. La interfaz se verificó sobre el mismo
  bundle compilado en un navegador real y con JSDOM; el cristal acrílico real
  detrás de la ventana lo dibuja Windows y solo puede comprobarse ejecutando la
  app instalada.
- **Aviso físico de batería al 80 %**: la batería estaba al 61,5 % descargando.
- **RPM de ventiladores, temperatura/ciclos de batería**: este hardware no los
  expone; mostrarlos sería inventarlos.

## Nota

Este informe se actualizará con lo que se observe en el uso diario de la versión
instalada (v2.5.0). El historial de la interfaz se guarda solo en memoria: el CSV
y el registro de sesión se escriben únicamente si los pides.
