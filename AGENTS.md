# AGENTS.md — TempPanel

Windows-only repo (WPF .NET 8 app + xUnit tests + Inno Setup installer). Shell tool runs **PowerShell 5.1**, not bash.

## Shell gotchas (will bite you)

- No `&&` / `||` chaining; no `?:` ternary; one statement per line or `;`.
- Non-ASCII chars (em-dash, `ó`) in `.ps1` files break parsing unless saved **UTF-8 with BOM**. Prefer ASCII-only scripts or wildcard matching (`-like "TempPanel*Prueba*"`).
- `dotnet` is NOT on PATH in fresh shells. Use the full path every time:
  `C:\Program` Files\dotnet\dotnet.exe <cmd>` (backtick-escaped space).
- Inno Setup lives per-user: `C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe`.

## Layout

- `TempPanel/` — WPF app. Entry: `App.xaml(.cs)` (handles `--selftest`, `--test-battery-alert`); UI: `MainWindow.xaml(.cs)` + WebView2; `Sensors/` (LHM/HWiNFO/WMI reading, never invent values — null = unavailable); `Services/` (battery alert, notifier, startup); `Frontend/` (offline HTML/CSS/JS, no CDN); `InnoSetup/setup.iss`.
- `TempPanel.Tests/` — xUnit: `BatteryTests.cs`, `HwinfoTests.cs` (synthetic SHM buffer).
- Outputs: publish → `C:\tempanel\publish\win-x64\`; installer → `C:\tempanel\dist\TempPanel-Setup-x64.exe`.
- Root `check-*.js` / `test-*.ps1` are throwaway verification harnesses run with `node` / `powershell -ExecutionPolicy Bypass -File`.

## Commands

```powershell
C:\Program` Files\dotnet\dotnet.exe test C:\tempanel\TempPanel.sln -c Release   # build + 20 tests
node --check C:\tempanel\TempPanel\Frontend\app.js; node C:\tempanel\check-ids.js  # frontend sanity
C:\Program` Files\dotnet\dotnet.exe publish C:\tempanel\TempPanel\TempPanel.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o C:\tempanel\publish\win-x64
& 'C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe' C:\tempanel\TempPanel\InnoSetup\setup.iss
C:\tempanel\publish\win-x64\TempPanel.exe --selftest   # writes %TEMP%\temppanel-selftest.json, no GUI
```

## Hard constraints

- **Never `Stop-Process` a TempPanel PID you didn't spawn** — the user runs the app live from `TempPanel\bin\publish\win-x64\`. PID-scoped control only; that dir is often file-locked, hence the separate `C:\tempanel\publish\` output.
- Installs are per-user (`PrivilegesRequired=lowest`): files land in `%LocalAppData%\Programs\TempPanel`, registry under `HKCU\...\Uninstall\{8A3F2B1C-...}_is1`.
- NuGet versions must exist: LHM `0.9.5`, WebView2 `1.0.2903.40`, xunit `2.9.0` + runner `2.8.2`. Verify via `https://api.nuget.org/v3-flatcontainer/<id>/index.json` before pinning.
- `UseWPF + UseWindowsForms` both true (NotifyIcon). Missing `using System.IO;` in `Sensors/`/`Services/` breaks the build — implicit usings don't cover it here; and don't name a member `Path` (collides with `System.IO.Path`).
- Sensor honesty is a product rule: null/absent stays "No disponible"; `0`/guesses are bugs (see `TUTORIAL.md`, `LIMITACIONES.md`).
- UIA popup test (`test-popup.ps1`) needs the window to appear first — wait-loop up to ~20 s; cold single-file start is slow.
