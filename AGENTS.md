# AGENTS.md — TempPanel

Windows-only repo (WPF .NET 8 app + xUnit tests + Inno Setup installer). Shell tool runs **PowerShell 5.1**, not bash.

## Shell gotchas (will bite you)

- No `&&` / `||` chaining; no `?:` ternary; one statement per line or `;`.
- Non-ASCII chars (em-dash, `ó`) in `.ps1` files break parsing unless saved **UTF-8 with BOM**. Prefer ASCII-only scripts or wildcard matching (`-like "TempPanel*Prueba*"`).
- `dotnet` is NOT on PATH in fresh shells. Use the full path every time:
  `C:\Program` Files\dotnet\dotnet.exe <cmd>` (backtick-escaped space).
- Inno Setup lives per-user: `C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe`.

## Layout

- `TempPanel/` — WPF app. Entry: `App.xaml(.cs)` (handles `--selftest`, `--test-battery-alert`, `--uicheck`); UI: `MainWindow.xaml(.cs)` + WebView2; `Sensors/` (LHM/HWiNFO/WMI reading, never invent values — null = unavailable); `Services/` (battery alert, notifier, startup, **PerformanceGovernor, SystemInfo** v3.0.0); `InnoSetup/setup.iss`.
- `TempPanel/Web/` — **fuentes de la interfaz** (Svelte 5 + Vite, runes). Se compilan a `TempPanel/Frontend/` (HTML/JS/CSS planos, sin CDN): esa carpeta es salida generada, no se edita a mano.
- `TempPanel/Frontend/` — salida de la interfaz que embebe el instalador (`index.html` + `assets/app.js` + `assets/style.css`) + `_preview.html` (vista previa de desarrollo, excluida del .csproj).
- `TempPanel.Tests/` — xUnit: `BatteryTests.cs`, `HwinfoTests.cs` (synthetic SHM buffer), `PrecisionTests.cs` (tasas coherentes de batería y precisión), `SystemIntegrationTests.cs` (gobernador de rendimiento, pantalla completa, config v3). **86 pruebas**.
- `uitest/` — arneses de la interfaz: `run.js` (DOM real con JSDOM sobre el bundle compilado, **71 comprobaciones**), `preview.js` (genera `Frontend/_preview.html` con el puente emulado y datos reales del selftest), `csslint.js` (detecta paréntesis desbalanceados en CSS).
- Outputs: publish → `C:\tempanel\publish\<versión>\`; installer → `C:\tempanel\dist\TempPanel-Setup-x64.exe`.
- Root `check-*.js` / `test-*.ps1` are throwaway verification harnesses run with `node` / `powershell -ExecutionPolicy Bypass -File`.

## Commands

```powershell
C:\Program` Files\dotnet\dotnet.exe test C:\tempanel\TempPanel.sln -c Release   # build + 86 tests
cd C:\tempanel\TempPanel\Web; npm run build        # interfaz Svelte -> TempPanel\Frontend (necesita npm; la app publicada NO)
node C:\tempanel\uitest\run.js                     # 71 comprobaciones DOM sobre el bundle compilado
node C:\tempanel\uitest\preview.js                 # genera Frontend\_preview.html (datos reales del selftest)
node C:\tempanel\uitest\csslint.js                 # CSS compilado + fuente, paréntesis balanceados
C:\Program` Files\dotnet\dotnet.exe publish C:\tempanel\TempPanel\TempPanel.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o C:\tempanel\publish\v3.0.0
& 'C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe' /DPublishDir=C:\tempanel\publish\v3.0.0 C:\tempanel\TempPanel\InnoSetup\setup.iss
C:\tempanel\publish\v3.0.0\TempPanel.exe --selftest   # writes %TEMP%\temppanel-selftest.json, no GUI
& "C:\Program Files\dotnet\dotnet.exe" C:\tempanel\TempPanel\bin\Release\net8.0-windows10.0.17763.0\TempPanel.dll --uicheck   # ventana real sin elevar: 0 = interfaz + cristal + sensores (revisa el log)
```

Antes de publicar hay que **compilar la interfaz** (`npm run build`): el proyecto .NET solo copia `TempPanel/Frontend/`. En una máquina sin Node la app se puede compilar/publicar igual, con el `Frontend/` ya compilado que está en el repo.

## Hard constraints

- **Never `Stop-Process` a TempPanel PID you didn't spawn** — the user runs the app live from `TempPanel\bin\publish\win-x64\`. PID-scoped control only; that dir is often file-locked, hence the separate `C:\tempanel\publish\` output.
- Installs are per-user (`PrivilegesRequired=lowest`): files land in `%LocalAppData%\Programs\TempPanel`, registry under `HKCU\...\Uninstall\{8A3F2B1C-...}_is1`.
- NuGet versions must exist: LHM `0.9.6`, WebView2 `1.0.4258.31`, xunit `2.9.0` + runner `2.8.2`. Verify via `https://api.nuget.org/v3-flatcontainer/<id>/index.json` before pinning.
- Interfaz (Svelte): el bundle compilado **se commitea** en `TempPanel/Frontend/` (así `dotnet publish` nunca necesita npm). Prohibido CDN/fuentes externas: todo local. El bundle sale como script clásico (no módulo) para que la vista previa y JSDOM puedan cargarlo igual; `vite.config.js` lo baja al final del body.
- La ventana sin marco depende de dos contratos con el nativo: franja de arrastre de **44 px** (`CaptionHeight`) y **150 px** a la derecha reservados para los botones (`win-min`, `win-max`, `win-close` en `#titlebar`). El título de la ventana WPF (`TempPanel — Monitor de hardware en tiempo real`) lo usa `App.RestoreExisting` vía `FindWindow`: no cambiarlo.
- El cristal lo decide la app nativa: añade `body.glass` o `body.no-glass`. La transparencia interna (`--panel-a`) es solo de la interfaz (preferencia local en `localStorage`, no va a `config.json`).
- **TFM `net8.0-windows10.0.17763.0` (app y tests) es obligatorio**: `WebView2CompositionControl` (transparencia real) necesita `Microsoft.Windows.SDK.NET.dll`. Con `net8.0-windows` el control falla al cargarse (`FileNotFoundException`) y la ventana queda **negra** (bug de v2.5.0). Además el control debe estar **Visible y con tamaño** antes de `EnsureCoreWebView2Async`; estando `Collapsed` la inicialización no termina. Red de seguridad: sin proyecciones o si tarda >20 s se quita del árbol y se usa el control clásico opaco (la app nunca queda vacía). Verifícalo con `--uicheck`: el log debe decir `control cristal (transparencia)`.
- `UseWPF + UseWindowsForms` both true (NotifyIcon). Missing `using System.IO;` in `Sensors/`/`Services/` breaks the build — implicit usings don't cover it here; and don't name a member `Path` (collides with `System.IO.Path`).
- Sensor honesty is a product rule: null/absent stays "No disponible"; `0`/guesses are bugs (see `TUTORIAL.md`, `LIMITACIONES.md`).
- **v3.0.0 — rendimiento adaptativo**: el intervalo real SIEMPRE lo decide `PerformanceGovernor.Evaluate(baseMs, visible, focused, fullscreen, powerSaver, adaptive)`; no llamar a `_sensorTimer.Change` con el valor base directamente (solo el gobernador, `force` al cargar la interfaz). Oculto/pantalla completa = 5 s + WebView2 suspendida; sin foco = mínimo 3 s. Re-evaluar en `Activated`/`Deactivated`/`StateChanged` y cada 3 s. El usuario puede desactivarlo (`AdaptivePerf`), pero lo oculto sigue suspendiendo el motor.
- **v3.0.0 — lector perezoso**: `_monitor` es **nullable** y se crea en `Task.Run` desde `NavigationCompleted` (nunca en el constructor: bloquea el primer pintado). Todo acceso va con `lock (_monLock)` y `if (_monitor != null)`; `PushSnapshot`/`CheckBattery`/reciclado/ajustes deben tolerar null. La pantalla de carga nativa (`LoadingLayer`) se colapsa al terminar la navegación (éxito o fallo).
- **v3.0.0 — Snap Layouts**: en modo cristal el hook devuelve `HTMAXBUTTON` en el rectángulo [W-88,W-50]×[7,37] (botones 38×30, gap 4, padding derecho 8, caption 44) y el clic se gestiona con `WM_NCLBUTTONDOWN` (el HTML no lo recibe). El hover nativo (`WM_NCMOUSEMOVE`/`WM_NCMOUSELEAVE`) se envía a la interfaz como `{type:"hover", data:{id:"win-max"|null}}`; el CSS pinta `.tb-btn.os-hover`. Cambiar tamaños exige tocar CSS + constantes `CaptionButtonW/H/Gap/RightPad`.
- **v3.0.0 — atajo global**: `RegisterHotKey(hwnd, 0xB10, MOD_CONTROL|MOD_ALT|MOD_NOREPEAT, 0x54)` en `Loaded`, `UnregisterHotKey` en `Cleanup`. Si falla, se registra en el log y Config muestra «No registrado» (no romper nada). El hook WndProc se instala SIEMPRE (no solo con cristal) para recibir `WM_HOTKEY`.
- **v3.0.0 — eventos del sistema**: `PowerModeChanged`/`SessionSwitch`/`UserPreferenceChanged` se suscriben en `Loaded` y se desuscriben en `Cleanup` (si no, el proceso puede quedar vivo). Los handlers marshalean con `Dispatcher.BeginInvoke`.
- **v3.0.0 — ventana mínima 820×560** y barra lateral de iconos por debajo de 980 px (los `nav-label`/`nav-key` se ocultan; el `#conn-text` también). La tabla de sensores se virtualiza con `ROW_H=34` (CSS `tr.row{height:34px}`): mantener ambos sincronizados.
- UIA popup test (`test-popup.ps1`) needs the window to appear first — wait-loop up to ~20 s; cold single-file start is slow.
