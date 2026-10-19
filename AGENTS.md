# AGENTS.md — TempPanel

Windows-only repo (WPF .NET 8 app + xUnit tests + Inno Setup installer). Shell tool runs **PowerShell 5.1**, not bash.

## Shell gotchas (will bite you)

- No `&&` / `||` chaining; no `?:` ternary; one statement per line or `;`.
- Non-ASCII chars (em-dash, `ó`) in `.ps1` files break parsing unless saved **UTF-8 with BOM**. Prefer ASCII-only scripts or wildcard matching (`-like "TempPanel*Prueba*"`).
- `dotnet` is NOT on PATH in fresh shells. Use the full path every time:
  `C:\Program` Files\dotnet\dotnet.exe <cmd>` (backtick-escaped space).
- Inno Setup lives per-user: `C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe`.

## Layout

- `TempPanel/` — WPF app. Entry: `App.xaml(.cs)` (handles `--selftest`, `--test-battery-alert`); UI: `MainWindow.xaml(.cs)` + WebView2; `Sensors/` (LHM/HWiNFO/WMI reading, never invent values — null = unavailable); `Services/` (battery alert, notifier, startup); `InnoSetup/setup.iss`.
- `TempPanel/Web/` — **fuentes de la interfaz** (Svelte 5 + Vite, runes). Se compilan a `TempPanel/Frontend/` (HTML/JS/CSS planos, sin CDN): esa carpeta es salida generada, no se edita a mano.
- `TempPanel/Frontend/` — salida de la interfaz que embebe el instalador (`index.html` + `assets/app.js` + `assets/style.css`) + `_preview.html` (vista previa de desarrollo, excluida del .csproj).
- `TempPanel.Tests/` — xUnit: `BatteryTests.cs`, `HwinfoTests.cs` (synthetic SHM buffer), `PrecisionTests.cs` (tasas coherentes de batería y precisión).
- `uitest/` — arneses de la interfaz: `run.js` (DOM real con JSDOM sobre el bundle compilado), `preview.js` (genera `Frontend/_preview.html` con el puente emulado y datos reales del selftest), `csslint.js` (detecta paréntesis desbalanceados en CSS).
- Outputs: publish → `C:\tempanel\publish\<versión>\`; installer → `C:\tempanel\dist\TempPanel-Setup-x64.exe`.
- Root `check-*.js` / `test-*.ps1` are throwaway verification harnesses run with `node` / `powershell -ExecutionPolicy Bypass -File`.

## Commands

```powershell
C:\Program` Files\dotnet\dotnet.exe test C:\tempanel\TempPanel.sln -c Release   # build + 70 tests
cd C:\tempanel\TempPanel\Web; npm run build        # interfaz Svelte -> TempPanel\Frontend (necesita npm; la app publicada NO)
node C:\tempanel\uitest\run.js                     # 49 comprobaciones DOM sobre el bundle compilado
node C:\tempanel\uitest\preview.js                 # genera Frontend\_preview.html (datos reales del selftest)
node C:\tempanel\uitest\csslint.js                 # CSS compilado + fuente, paréntesis balanceados
C:\Program` Files\dotnet\dotnet.exe publish C:\tempanel\TempPanel\TempPanel.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o C:\tempanel\publish\v2.5.0
& 'C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe' /DPublishDir=C:\tempanel\publish\v2.5.0 C:\tempanel\TempPanel\InnoSetup\setup.iss
C:\tempanel\publish\v2.5.0\TempPanel.exe --selftest   # writes %TEMP%\temppanel-selftest.json, no GUI
```

Antes de publicar hay que **compilar la interfaz** (`npm run build`): el proyecto .NET solo copia `TempPanel/Frontend/`. En una máquina sin Node la app se puede compilar/publicar igual, con el `Frontend/` ya compilado que está en el repo.

## Hard constraints

- **Never `Stop-Process` a TempPanel PID you didn't spawn** — the user runs the app live from `TempPanel\bin\publish\win-x64\`. PID-scoped control only; that dir is often file-locked, hence the separate `C:\tempanel\publish\` output.
- Installs are per-user (`PrivilegesRequired=lowest`): files land in `%LocalAppData%\Programs\TempPanel`, registry under `HKCU\...\Uninstall\{8A3F2B1C-...}_is1`.
- NuGet versions must exist: LHM `0.9.6`, WebView2 `1.0.4258.31`, xunit `2.9.0` + runner `2.8.2`. Verify via `https://api.nuget.org/v3-flatcontainer/<id>/index.json` before pinning.
- Interfaz (Svelte): el bundle compilado **se commitea** en `TempPanel/Frontend/` (así `dotnet publish` nunca necesita npm). Prohibido CDN/fuentes externas: todo local. El bundle sale como script clásico (no módulo) para que la vista previa y JSDOM puedan cargarlo igual; `vite.config.js` lo baja al final del body.
- La ventana sin marco depende de dos contratos con el nativo: franja de arrastre de **44 px** (`CaptionHeight`) y **150 px** a la derecha reservados para los botones (`win-min`, `win-max`, `win-close` en `#titlebar`). El título de la ventana WPF (`TempPanel — Monitor de hardware en tiempo real`) lo usa `App.RestoreExisting` vía `FindWindow`: no cambiarlo.
- El cristal lo decide la app nativa: añade `body.glass` o `body.no-glass`. La transparencia interna (`--panel-a`) es solo de la interfaz (preferencia local en `localStorage`, no va a `config.json`).
- `UseWPF + UseWindowsForms` both true (NotifyIcon). Missing `using System.IO;` in `Sensors/`/`Services/` breaks the build — implicit usings don't cover it here; and don't name a member `Path` (collides with `System.IO.Path`).
- Sensor honesty is a product rule: null/absent stays "No disponible"; `0`/guesses are bugs (see `TUTORIAL.md`, `LIMITACIONES.md`).
- UIA popup test (`test-popup.ps1`) needs the window to appear first — wait-loop up to ~20 s; cold single-file start is slow.
