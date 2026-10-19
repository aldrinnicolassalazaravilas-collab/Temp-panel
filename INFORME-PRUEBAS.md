# Informe de pruebas — TempPanel 3.0.0

Fecha: 2026-10-19 (3.0.0), sobre las pruebas de 2.5.1 del 2026-10-19. Equipo:
Lenovo 83K1, i7-13620H, 24 GB, Intel UHD, Windows 11 Pro (build 26200).
Distingue pruebas automatizadas, compilación y pruebas reales en el dispositivo.
Todo lo que aparece aquí se ejecutó de verdad; lo que no se pudo comprobar se
dice explícitamente en §10 y en la matriz de §11.

## 1. Compilación Release

- `dotnet test TempPanel.sln -c Release` → **86/86 pruebas superadas, 0 errores**
  (70 de versiones anteriores + 16 nuevas de v3.0.0: gobernador de rendimiento,
  detección de pantalla completa y compatibilidad de config.json).
- `dotnet publish -r win-x64 --self-contained` → `TempPanel.exe`
  **191.883.631 bytes (191,9 MB)**; `DebugType=embedded`: no se publica ningún
  `.pdb` suelto (0 encontrados). Incluye `Microsoft.Windows.SDK.NET.dll`
  (23,6 MB) + `WinRT.Runtime.dll` para el control WebView2 con composición.
- Interfaz: `npm run build` en `TempPanel/Web` → 138 módulos,
  `assets/app.js` **129,2 kB** + `assets/style.css` **18,2 kB**, script clásico,
  rutas relativas, sin CDN. El bundle compilado se commitea y el publish lo copia
  tal cual (`Frontend/index.html` + `Frontend/assets/*`).

## 2. Pruebas automatizadas

- **xUnit (backend)**: 86/86 (`dotnet test -c Release`).
  - Tasas de batería: con el firmware publicando el mismo valor en carga y
    descarga (caso real de este portátil), solo se muestra la dirección que el
    firmware confirma; la otra queda en «No disponible».
  - Alerta de batería: dispara cargando al umbral, no descargando, una vez por
    ciclo, rearma por debajo del valor, valida rearma < alerta, nivel nulo nunca
    dispara. Niveles simulados SOLO en tests.
  - **v3.0.0 — gobernador**: primer plano respeta el intervalo; oculto/pantalla
    completa → 5 s + motor suspendido; ahorro de energía → 5 s sin suspender la
    interfaz visible; sin foco → mínimo 3 s; reducción desactivada respeta el
    intervalo (salvo oculto, que siempre suspende); base limitada a 1-10 s.
  - **v3.0.0 — pantalla completa**: cubrir el monitor cuenta; maximizada con
    barra de tareas (95,5 %) no cuenta; ventana parcial no cuenta; ventana mayor
    que el monitor se recorta y cuenta; monitor inválido nunca cuenta.
  - **v3.0.0 — config**: un `config.json` de v2.5.1 sin las claves nuevas carga
    con `HotkeyEnabled`, `TaskbarBadge` y `AdaptivePerf` activos; restablecer
    ajustes también los devuelve a su valor.
- **DOM real sobre el bundle compilado** (`node uitest\run.js`) → **71/71 OK**:
  montaje, cristal (`body.glass`), barra de título y botones de ventana,
  10 páginas, tarjetas/medidor/anillo/minigráficos/gráficas, valores concretos
  del snapshot sintético, sesión CPU, tendencia calculada, barras por núcleo,
  TjMax, selector de sensor, GPU, RAM, batería (1 decimal, fuente, tendencia,
  estado de carga real, flujo de carga), tabla de sensores con orden por columna,
  historial (chips, gráfica, estadísticas), ayuda (diagnóstico, Defender, HVCI),
  configuración (chips de mini ventana, versión, transparencia),
  **protocolo completo de 34 comandos** hacia la app nativa, bienvenida y su
  comando, banner crítico a 96 °C y pausa. Además: exportación **CSV** con la
  cabecera exacta, una fila por lectura y los huecos vacíos (nunca ceros), y
  ausencia de errores de ejecución en la página durante todo el recorrido.
  **Nuevas v3.0.0**: alto contraste (`body.hc`), acento del sistema en
  `--os-accent`, chip de rendimiento y ritmo efectivo (3 s), desaparición del
  chip en primer plano, hover nativo del botón maximizar, navegación desde la
  bandeja, salud crítica enviada a la barra de tareas, equipo sin batería
  (Batería y Resumen lo explican, no inventan), **400 sensores virtualizados**
  (<100 filas montadas y desplazamiento hasta la fila 300), tarjeta de
  integración con Windows y comandos `setHotkey`/`openWindowsSettings`.
- **CSS**: `node uitest\csslint.js` → 218 bloques (fuente) + 215 (compilado),
  0 paréntesis desbalanceados (la errata que dejó sin efecto el degradado de
  `#app::before` en v2.5.0 se detectó y corrigió con esta herramienta).

## 3. Interfaz (verificada en navegador real, no solo en tests)

> El diseño es el mismo de 2.5.1 (aprobado por el usuario). La revisión visual
> en navegador de esta sección corresponde a esa base; los elementos nuevos de
> 3.0.0 (tarjeta de integración, chip de rendimiento, barra lateral compacta,
> alto contraste) se verificaron en DOM en §2 y en la app real en §7/§11.

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

## 4. Sensores reales (último `--selftest` real: 2026-10-18 20:30:03; el backend de sensores no cambia en 3.0.0, solo se crea después de mostrar la interfaz)

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
  (61,5 %) y el equipo cargado; ver §10.

## 6. Instalador

- `dist\TempPanel-Setup-x64.exe` recompilado con Inno Setup 6
  (**54.727.373 bytes / 54,7 MB**, 2026-10-19 16:42) a partir de
  `publish\v3.0.0\`, que contiene el `Frontend/` **compilado**
  (`index.html` + `assets/app.js` + `assets/style.css`) y ningún `.pdb` suelto.
  Versión del instalador y del exe: **3.0.0**. Incluye `[InstallDelete]` para
  borrar los restos del frontend antiguo (`Frontend\app.js`,
  `Frontend\styles.css`) al actualizar.
- Sigue siendo por usuario (`PrivilegesRequired=lowest`): instala en
  `%LocalAppData%\Programs\TempPanel` y registra en `HKCU\...\Uninstall`.
- La app instalada no necesita Node, ni .NET, ni Internet.

## 7. Consumo y memoria (v3.0.0)

- **Lector perezoso**: el lector LHM ya no se crea en el constructor; se abre en
  segundo plano cuando la interfaz está lista. En las pruebas reales la ventana
  apareció a **0,5-2,3 s** y la interfaz a **1,7-5,2 s** (la variación depende
  de la carga del equipo y del antivirus; el publish y el instalador compilando
  en paralelo alargan la última medición); los sensores tardaron **2,5-5,6 s**
  sin bloquear la ventana. Antes, la creación del lector (~1-2 s) bloqueaba el
  primer pintado.
- **Mini ventana**: con la ventana principal oculta, la mini (nativa y ligera)
  conserva el ritmo elegido por el usuario; el motor WebView2 sí permanece
  suspendido, que es lo que ahorra memoria. La reducción adaptativa vuelve a
  aplicarse en cuanto no hay mini abierta.
- **Pantalla de carga nativa** (logo + progreso) visible al instante mientras
  WebView2 arranca; se retira al terminar la navegación, también si falla.
- **Gobernador de rendimiento**: primer plano = intervalo elegido (1 s en el
  perfil de prueba); sin foco = mínimo 3 s; oculto/minimizado/bloqueado o app a
  pantalla completa = 5 s y motor WebView2 suspendido. Se ve en el log:
  `Rendimiento: background · cada 3000 ms · WebView2 activa` y
  `Rendimiento: normal · cada 1000 ms · WebView2 activa`. No se suspende nunca
  durante la inicialización de WebView2 (bug detectado y corregido en la prueba).
- **Tabla de sensores virtualizada**: con 400 sensores solo se montan las filas
  visibles (<100 en la prueba DOM) y el desplazamiento conserva la altura.
- La tabla completa de sensores solo se construye cuando la página está abierta
  (`view=sensores`); el resto de páginas no la recibe.
- GC de estación de trabajo no concurrente y `meta.appMemoryMb` visible en la
  interfaz para poder comprobarlo sin herramientas externas.
- **Eventos del sistema**: suspensión/reanudación y bloqueo de sesión reducen la
  actividad; al volver se refresca y se reanuda el motor.

## 8. Offline

- Sin CDN, sin fuentes externas y sin dependencias en tiempo de ejecución: el
  bundle es JS/CSS local y la comunicación con la app es WebMessage.
- La app solo usa Internet si pulsas «Buscar actualizaciones» o activas la
  comprobación automática (desactivada por defecto).

## 9. Corrección v2.5.1: ventana negra al abrir (bug de v2.5.0)

La v2.5.0 instalada abría una ventana **negra sin interfaz** (reportado por el
usuario con captura). El registro de la app lo delataba:

```
[2026-10-19 14:05:28] [INFO] Cristal: acrílico aplicado
[2026-10-19 14:05:28] [ERROR] DispatcherException: System.IO.FileNotFoundException:
  File name: 'Microsoft.Windows.SDK.NET, Version=10.0.17763.10'
  at Microsoft.Web.WebView2.Wpf.WebView2CompositionControl.WebView2CompositionControl_Loaded(...)
```

Causa doble:

1. El TFM era `net8.0-windows`, así que el publish no incluía las proyecciones
   del SDK de Windows (`Microsoft.Windows.SDK.NET.dll`) que necesita
   `WebView2CompositionControl` (el control que permite ver el acrílico a través
   de la interfaz). El control lanzaba `FileNotFoundException` al cargarse y la
   ventana quedaba en negro; el respaldo al control opaco nunca llegaba a
   ejecutarse porque `EnsureCoreWebView2Async` se quedaba esperando.
2. Al corregir lo anterior apareció un segundo problema: el control estaba
   `Collapsed` (sin tamaño) al inicializarse y `EnsureCoreWebView2Async` tampoco
   terminaba. Debe estar visible y con layout antes de inicializar.

Arreglos:

- TFM `net8.0-windows10.0.17763.0` en la app **y en los tests** (así se publica
  `Microsoft.Windows.SDK.NET.dll` dentro del bundle single-file).
- El control se hace `Visible` + `UpdateLayout()` antes de
  `EnsureCoreWebView2Async`, con tope de 20 s.
- Red de seguridad: si faltan las proyecciones o el control no responde, se quita
  del árbol y la app usa el control clásico (opaco) — **nunca** una ventana vacía.
- Instalador: `[InstallDelete]` de los archivos viejos del frontend.

Verificación (nuevo modo `--uicheck`: abre la ventana real sin elevar, con perfil
de WebView2 aparte, espera a que la interfaz cargue y sale con 0/1):

| Ejecución | Resultado | Tiempo |
|---|---|---|
| `dotnet ...\TempPanel.dll --uicheck` (framework-dependent) | `UICHECK OK · control cristal (transparencia)` | 2,6 s |
| Exe **single-file autocontenido** (manifiesto asInvoker solo para la prueba) | `UICHECK OK · control cristal (transparencia)` | 6,1 s |

Ambas resolviendo `Microsoft.Windows.SDK.NET.dll` desde el bundle; el registro no
muestra ningún `DispatcherException`. En aquel momento: `dotnet test` → 70/70 y
`node uitest\run.js` → 52/52.

## 11. Integración con Windows y rendimiento (novedades v3.0.0)

| Función | Cómo se verificó | Estado |
|---|---|---|
| Render adaptativo | 7 pruebas unitarias + log real (`background`/`normal`/`fullscreen`) | Verificado |
| Motor suspendido | Log `WebView2 suspendida` al ocultar/pantalla completa; reanudación al recuperar foco | Verificado en lógica; falta prueba física de juego |
| Lector perezoso | `--uicheck`: ventana 0,5-1,5 s, interfaz 1,7-4,0 s, sensores 2,5-5,6 s | Verificado |
| Pantalla de carga | Visible hasta `NavigationCompleted`; se retira también en error | Verificado |
| Tabla virtualizada | 400 sensores → <100 filas + salto a la fila 300 | Verificado |
| Sin batería | Batería y Resumen muestran un aviso honesto (DOM) | Verificado |
| Tema/acento/alto contraste | `config.os` → `body.hc`, `--os-accent`, tarjeta en Configuración | Verificado en DOM |
| Snap Layouts | `HTMAXBUTTON` + hover nativo + contrato CSS 38×30/gap 4/derecha 8 | Implementado; menú visual pendiente |
| Atajo Ctrl+Alt+T | `RegisterHotKey` OK en el log; comando `setHotkey` y estado en Config | Registrado; pulsación física pendiente |
| Barra de tareas | `TaskbarItemInfo` + mensaje `health` crítico + progreso de carga | Implementado; distintivo visual pendiente |
| Bandeja | Menú «Sensores»/«Configuración» → mensaje `navigate` (DOM) | Verificado en DOM |
| Eventos energía/sesión | Suscripción y desuscripción en `Cleanup`; reanudación forzada | Implementado; suspensión física pendiente |
| Accesos a Windows | Botones → `ms-settings:personalization-colors` / `batterysaver` | Comando verificado en DOM; apertura real pendiente |
| Ventana 820×560 / rail <980 | CSS `@media` + `MinWidth/MinHeight` | Implementado; revisión visual pendiente |
| Windows 10 sólido | Fallback de cristal de 2.5.1 sin cambios | No probado en hardware Win10 |
| ARM64 | Build x64 por emulación | No probado en hardware ARM |

### Cómo comprobarlo en la app instalada

1. Configuración → **Integración con Windows**: debe mostrar tu versión de
   Windows, el acento, el alto contraste y «Atajo Ctrl+Alt+T: Registrado».
2. Pulsa **Ctrl+Alt+T** con otra ventana delante: TempPanel aparece/desaparece.
3. Pasa el ratón por el botón **maximizar** (cristal, Windows 11): aparece el
   menú de Snap Layouts; un clic maximiza/restaura.
4. Minimiza la ventana y provoca una temperatura alta (o espera una alerta):
   el icono de la barra de tareas muestra el distintivo; cargando, el progreso.
5. Con la app en segundo plano, el chip superior indica «Segundo plano» y el
   ritmo pasa a 3 s; al volver, desaparece.

## 10. Lo que NO se pudo verificar (y por qué)

- **Aspecto visual del cristal sobre el escritorio**: el modo `--uicheck`
  confirma que el control con composición inicializa, que la interfaz carga y que
  no hay errores, pero el acrílico/DWM real solo se ve en la app instalada y
  elevada; la valoración final del aspecto la hace el usuario.
- **Aviso físico de batería al 80 %**: la batería estaba al 61,5 % descargando.
- **RPM de ventiladores, temperatura/ciclos de batería**: este hardware no los
  expone; mostrarlos sería inventarlos.
- **v3.0.0 — Snap Layouts**: el rectángulo `HTMAXBUTTON` está implementado y el
  hover se verificó en DOM, pero el menú flotante de distribuciones necesita un
  ratón real sobre la ventana instalada; no se puede automatizar aquí.
- **v3.0.0 — atajo global**: el registro de `Ctrl+Alt+T` se confirma en el log,
  pero la pulsación física con la ventana en segundo plano no se puede simular
  sin interacción de escritorio.
- **v3.0.0 — distintivo de la barra de tareas**: la lógica (salud de la interfaz
  + progreso de carga) está cubierta por DOM y por el mensaje `health`, pero el
  distintivo amarillo/rojo solo se ve con una alerta térmica real y la ventana
  minimizada (no oculta a la bandeja).
- **v3.0.0 — exe publicado**: el binario single-file lleva manifiesto
  `requireAdministrator`; el agente no lo ejecuta para no lanzar UAC. La
  verificación de interfaz/cristal/sensores se hizo con la misma compilación
  Release alojada en `dotnet.exe` (`--uicheck`), que es el procedimiento usado
  también en 2.5.1.
- **v3.0.0 — Windows 10 y ARM64**: no hay hardware Windows 10 ni ARM64 en este
  equipo; el modo sólido y la emulación x64 están implementados pero no se
  probaron físicamente.
- **v3.0.0 — barra lateral compacta (<980 px)**: el CSS está aplicado y los
  `nav-label` se ocultan, pero la revisión visual en ventana estrecha queda
  pendiente de la app instalada.

## Nota

Este informe se actualizará con lo que se observe en el uso diario de la versión
instalada (v3.0.0). El historial de la interfaz se guarda solo en memoria: el CSV
y el registro de sesión se escriben únicamente si los pides.
