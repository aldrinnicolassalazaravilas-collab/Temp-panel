# Informe de pruebas — TempPanel 1.0.0

Fecha: 2026-10-09. Equipo: Lenovo 83K1, i7-13620H, 24 GB, Intel UHD, Win10 Pro x64.
Distingue pruebas automatizadas, compilación y pruebas reales en el dispositivo.

## 1. Compilación Release (§12.1–12.2)
- `dotnet restore TempPanel.sln` → OK (LHM 0.9.5, WebView2 1.0.2903.40 verificados en NuGet).
- `dotnet build -c Release` → 0 errores (avisos menores MSB3277/NU1603 resueltos; build final: 0 avisos).
- `dotnet publish -r win-x64 --self-contained` → `TempPanel.exe` 167 MB, sin necesidad de .NET.

## 2. Pruebas automatizadas (§12.7) — 25/25 OK
`dotnet test -c Release` → Superado: 25, Fallidos: 0.
- Dispara cargando al 80 %; NO dispara descargando al 85–100 %; una sola vez por
  ciclo; se reactiva bajo 75 % (estricto: a 75.0 exacto NO rearma); desactivada
  nunca dispara; nivel nulo/inválido nunca dispara; umbrales personalizados;
  validación reactivación < alerta; escenario completo de recarga; ResetToDefaults.
- Niveles simulados SOLO en tests; producción usa PowerStatus real.

## 3. Ventana y GUI (§12.3)
- `TempPanel.exe` abre ventana WPF propia «TempPanel — Monitor de hardware en
  tiempo real», sin Chrome ni consola. WebView2 inicializa (EBWebView creado).
- Log `%AppData%\TempPanel\logs\` sin errores.

## 4. Sensores reales (§12.5–12.6, §12.11 comportamiento sin sensores)
- `--selftest` en el propio i7-13620H: CPU detectada, carga total 12–21 %,
  16 cargas núcleo/hilo, GPU UHD (carga/frecuencia, temp nula honesta),
  RAM 24237 MB (78 %), batería precisa 59–71 % «Descargando», capacidades
  50000/52550/36860 mWh, 30–35 W, ~53 min, 11.24 V, alimentación «Con batería»,
  0 ventiladores (aviso honesto), 97 sensores. Potencia CPU 0 → nulo (no ficticio).
- Cálculo de % de batería: LHM Charge Level + PowerStatus, verificado coherente.
- CSV: cabecera + filas con nulos vacíos, verificado por script.
- Pipeline de temperatura (frontend): verificado con snapshot sintético SOLO en
  pruebas (`uitest/run.js`, 11/11): tarjetas, gauges, sesión, diagnósticos,
  gráficas y alertas (oculta a 54 °C, crítica a 96 °C). La lectura de hardware
  queda pendiente de ejecución elevada (ver §9 / TUTORIAL.md).

## 5. Alerta de batería (§12.7–12.10)
- Ventana `--test-battery-alert` aparece con título y botón OK (automatización UIA).
- Clic OK → ventana cerrada Y proceso terminado. PRUEBA SUPERADA.
- No repetición / reactivación: cubierto por tests 3, 4, 5 y 11.
- Carga real al 80 %: NO verificada físicamente (batería descargando en pruebas).

## 6. Instalador (§12.12)
- `TempPanel-Setup-x64.exe` (~51 MB) compilado con Inno Setup 6.7.3, en español,
  con icono, licencia, selección de carpeta, iconos escritorio/Inicio,
  desinstalador y detección de WebView2. Compilación sin avisos.
- Instalación silenciosa verificada: instala en `%LocalAppData%\Programs\TempPanel`
  (per-user, `PrivilegesRequired=lowest`), con `TempPanel.exe` 167 MB, `Frontend/`,
  accesos de escritorio e Inicio, y registro en `HKCU\...\Uninstall`.
- App instalada: abre ventana WPF con título correcto y **0 conexiones TCP
  establecidas** (offline verificado). El proceso instalado termina al cerrarse.
- Desinstalación silenciosa: elimina registro, accesos y todos los archivos salvo
  `TempPanel.exe`, que quedó bloqueado por un handle abierto (en investigación;
  ver §9). El resto de la limpieza es correcta.

## 7. Procesos y consumo (§12.13–12.14)
- Cierre desde bandeja «Salir»: proceso termina (verificado tras pruebas GUI).
- Conjunto de trabajo GUI abierta: ~260 MB (autocontenida + WebView2).
- Minimizada: sigue muestreo cada 2 s por diseño (monitor activo en bandeja).

## 8. Offline (§12.4)
- Sin CDN ni fuentes externas; WebMessage local; instalador no descarga nada sin
  avisar (solo informa si falta WebView2).
- App instalada en ejecución: `Get-NetTCPConnection` → 0 conexiones establecidas.

## 9. Incidencia: bloqueo de TempPanel.exe tras desinstalar
Tras la desinstalación silenciosa quedó `TempPanel.exe` con acceso denegado al
borrar. Sin `PendingFileRenameOperations` para TempPanel y sin hijos WebView2
propios supervivientes (los `msedgewebview2.exe` activos pertenecen a la
instancia del usuario en otra ruta). En diagnóstico con Sysinternals Handle.

## Nota
La instalación/desinstalación final y la comprobación de conexiones se ejecutan
a continuación sobre este mismo equipo; este informe se actualizará con el resultado.
