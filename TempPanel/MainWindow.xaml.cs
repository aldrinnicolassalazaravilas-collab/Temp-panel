using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TempPanel.Sensors;
using TempPanel.Services;

namespace TempPanel;

/// <summary>
/// Ventana principal WPF: aloja WebView2 (motor de interfaz integrado),
/// bandeja del sistema, temporizadores de sensores (2 s) y batería (15 s).
/// Comunicación con el frontend por WebMessage: sin HTTP ni red.
/// </summary>
public partial class MainWindow : Window
{
    private HardwareMonitor _monitor;
    private readonly object _monLock = new();
    private readonly AppConfig _config;
    private readonly System.Threading.Timer _sensorTimer;
    private readonly System.Threading.Timer _batteryTimer;
    private readonly System.Threading.Timer _recycleTimer;
    private bool _everHadCpuTemp;
    private readonly BatteryAlertService _batteryService;
    private readonly BatteryAlertService _fullService;
    private readonly Notifier _notifier;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private bool _webReady;
    private bool _allowClose;
    private MiniWindow? _mini;

    // --- v2.5.0: cristal (transparencia real), suspensión de WebView2 y RAM ---
    private readonly Process _self = Process.GetCurrentProcess();
    private Microsoft.Web.WebView2.Wpf.IWebView2? _web;      // control activo (cristal o clásico)
    private FrameworkElement? _webElement;
    private CoreWebView2? _core;
    private bool _glassMode;
    private bool _webSuspended;
    private bool _webVisible = true;
    private string _page = "resumen";
    // Franja superior arrastrable (barra de la interfaz) y zona de botones.
    private const double CaptionHeight = 44;
    private const double CaptionButtonsWidth = 150;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppConfig.Load();
        _monitor = new HardwareMonitor
        {
            RefreshMs = _config.RefreshMs,
            PreferredCpuSensor = _config.PreferredCpuSensor
        };
        _batteryService = BatteryAlertService.FromConfig(_config);
        _batteryService.AlertTriggered += OnBatteryAlert;
        _fullService = new BatteryAlertService();
        _fullService.AlertTriggered += OnFullAlert;
        _fullService.ApplyOptions(FullOptions());
        _notifier = new Notifier(ShowBatteryPopup, ShowTrayBalloon, ShowFullPopup);

        // Bandeja del sistema
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "TempPanel — Monitor de hardware",
            Visible = true
        };
        try
        {
            string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            _tray.Icon = File.Exists(ico) ? new System.Drawing.Icon(ico) : System.Drawing.SystemIcons.Application;
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }
        _tray.DoubleClick += (_, __) => RestoreWindow();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir TempPanel", null, (_, __) => RestoreWindow());
        menu.Items.Add("Mini ventana", null, (_, __) => OpenMini());
        menu.Items.Add("Salir", null, (_, __) => ExitApp());
        _tray.ContextMenuStrip = menu;

        _sensorTimer = new System.Threading.Timer(_ => PushSnapshot(), null, Timeout.Infinite, Timeout.Infinite);
        _batteryTimer = new System.Threading.Timer(_ => CheckBattery(), null, Timeout.Infinite, Timeout.Infinite);
        // Si como admin nunca hubo temperaturas (p. ej. antivirus retiró el
        // driver a mitad de sesión y luego se permitió), reintenta desplegar
        // cada 10 minutos recreando el lector. Barato y honesto.
        _recycleTimer = new System.Threading.Timer(_ => MaybeRecycleMonitor(), null, Timeout.Infinite, Timeout.Infinite);

        Loaded += OnLoadedWindow;
        StateChanged += (_, __) =>
        {
            // Minimizada = fuera de vista: se suspende el motor WebView2 (ahorro de RAM).
            _ = SetWebVisibleAsync(WindowState != WindowState.Minimized && IsVisible);
        };
        Closing += OnClosingWindow;
        Closed += (_, __) => Cleanup();

        if (_config.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            Hide();
        }
        StartupManager.Apply(_config.StartWithWindows);
    }

    // ---------------- Arranque de la ventana ----------------
    private async void OnLoadedWindow(object sender, RoutedEventArgs e)
    {
        try
        {
            // Cristal acrílico de Windows 11 + esquinas redondeadas (si el
            // sistema lo soporta). Si no, la ventana queda oscura y opaca.
            bool acrylic = WindowFx.SupportsGlass() && WindowFx.ApplyAcrylic(this);
            AppLogger.Info("Cristal: " + (acrylic ? "acrílico aplicado" : "no disponible (tema oscuro sólido)"));
            if (acrylic)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(WndProcHook);
            }
        }
        catch (Exception ex) { AppLogger.Error("WindowFx: " + ex.Message); }
        await InitWebAsync();
    }

    // ---------------- WebView2 ----------------
    private async Task InitWebAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TempPanel", "WebView2",
                UpdateService.Current().ToString()); // perfil por versión: evita bloqueos de perfiles viejos
            Directory.CreateDirectory(userData);

            // v2.5.0 — menos RAM: caché de disco pequeña, sin servicios de fondo
            // que la app no usa y techo de memoria del motor JS.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-features=msSmartScreenProtection,msEdgeShoppingFeatures " +
                    "--disable-component-update --disk-cache-size=8388608 " +
                    "--js-flags=--max-old-space-size=64"
            };
            var env = await CoreWebView2Environment.CreateAsync(null, userData, options);

            // Windows 11: control con transparencia real (el acrílico de DWM
            // se ve a través de la interfaz). Si algo falla, cae al control
            // clásico opaco y la app sigue funcionando igual.
            if (WindowFx.SupportsGlass())
            {
                try
                {
                    WebGlass.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                    await WebGlass.EnsureCoreWebView2Async(env);
                    _web = WebGlass;
                    _webElement = WebGlass;
                    _glassMode = true;
                    WebGlass.Visibility = Visibility.Visible;
                    AppLogger.Info("WebView2: control con transparencia (cristal) inicializado.");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Control con transparencia no disponible: " + ex.Message);
                    try { ((IDisposable)WebGlass).Dispose(); } catch { }
                }
            }
            if (_web == null)
            {
                await Web.EnsureCoreWebView2Async(env);
                _web = Web;
                _webElement = Web;
                // Sin cristal: fondo opaco consistente (nada de ventana "fantasma").
                Root.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x0B, 0x12, 0x20));
                Web.Visibility = Visibility.Visible;
                AppLogger.Info("WebView2: control clásico (opaco) inicializado.");
            }
            _core = _web.CoreWebView2;

            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            // Arrastre nativo de la barra superior también en el control clásico
            // (en modo cristal lo resuelve el propio Windows con la franja superior).
            try { _core.Settings.IsNonClientRegionSupportEnabled = true; } catch { }

            string wwwroot = FindWwwRoot();
            if (!File.Exists(Path.Combine(wwwroot, "index.html")))
            {
                AppLogger.Error("Frontend no encontrado en: " + wwwroot);
                System.Windows.MessageBox.Show("No se encontró la interfaz en:\n" + wwwroot +
                    "\n\nReinstala TempPanel.", "TempPanel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _core.SetVirtualHostNameToFolderMapping(
                "temppanel.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
            _core.WebMessageReceived += OnWebMessage;
            _core.NavigationCompleted += (_, nav) =>
            {
                if (!nav.IsSuccess)
                {
                    AppLogger.Error($"Navegación fallida: {nav.HttpStatusCode} ({nav.WebErrorStatus})");
                    System.Windows.MessageBox.Show(
                        "No se pudo cargar la interfaz.\n\n" +
                        $"Error: {nav.WebErrorStatus} (HTTP {nav.HttpStatusCode}).\n" +
                        "Cierra la app, borra %LocalAppData%\\TempPanel\\WebView2 e inténtalo de nuevo.",
                        "TempPanel", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _webReady = true;
                _monitor.IncludeAllSensors = _page == "sensores";
                SendConfig();
                _sensorTimer.Change(0, _config.RefreshMs);
                _batteryTimer.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
                _recycleTimer.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
                if (_config.AutoCheckUpdates) _ = CheckUpdatesAsync(manual: false);
            };
            _web.Source = new Uri("https://temppanel.local/index.html");
        }
        catch (Exception ex)
        {
            AppLogger.Error("InitWebView2: " + ex);
            System.Windows.MessageBox.Show("No se pudo iniciar WebView2.\n\n" + ex.Message +
                "\n\nInstala el runtime WebView2 desde:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "TempPanel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- Cristal + ahorro de RAM (v2.5.0) ----------------

    /// <summary>Arrastre nativo de la franja superior (modo sin marco + cristal).</summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCAPTION = 2;
        if (msg == WM_NCHITTEST && _glassMode)
        {
            try
            {
                long lp = lParam.ToInt64();
                double sx = (short)(lp & 0xFFFF);
                double sy = (short)((lp >> 16) & 0xFFFF);
                var p = PointFromScreen(new System.Windows.Point(sx, sy));
                const double border = 8; // deja el borde para redimensionar (WindowChrome)
                bool inside = p.X > border && p.Y > border &&
                              p.X < ActualWidth - border && p.Y < ActualHeight - border;
                if (inside && p.Y < CaptionHeight && p.X < ActualWidth - CaptionButtonsWidth)
                {
                    handled = true;
                    return (IntPtr)HTCAPTION;
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Suspende el motor WebView2 cuando la ventana está oculta o minimizada
    /// (bandeja o mini ventana) y lo reanuda al volver. Además baja el objetivo
    /// de memoria del motor. Ahorro real de RAM sin perder lecturas: los
    /// sensores siguen muestreando igual.
    /// </summary>
    private async Task SetWebVisibleAsync(bool visible)
    {
        _webVisible = visible;
        try
        {
            if (_webElement == null || _core == null) return;
            if (visible)
            {
                if (_webSuspended)
                {
                    try { _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal; } catch { }
                    try { _core.Resume(); } catch (Exception ex) { AppLogger.Error("Resume: " + ex.Message); }
                    _webSuspended = false;
                }
                _webElement.Visibility = Visibility.Visible;
            }
            else
            {
                _webElement.Visibility = Visibility.Collapsed;
                try { _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                if (!_webSuspended)
                {
                    try { _webSuspended = await _core.TrySuspendAsync(); }
                    catch (Exception ex) { AppLogger.Error("TrySuspend: " + ex.Message); }
                }
            }
        }
        catch (Exception ex) { AppLogger.Error("SetWebVisible: " + ex.Message); }
    }

    private static string FindWwwRoot()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "Frontend"),
            Path.Combine(baseDir, "wwwroot"),
            @"C:\tempanel\TempPanel\Frontend"
        };
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "index.html"))) return Path.GetFullPath(c);
        return Path.Combine(baseDir, "Frontend");
    }

    // ---------------- Mensajes del frontend ----------------
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            if (!doc.RootElement.TryGetProperty("cmd", out var cmdEl)) return;
            string cmd = cmdEl.GetString() ?? "";
            switch (cmd)
            {
                case "getSnapshot": PushSnapshot(); break;
                case "setRefresh":
                    if (doc.RootElement.TryGetProperty("ms", out var msEl) && msEl.TryGetInt32(out int ms))
                    {
                        ms = Math.Clamp(ms, 1000, 10000);
                        _config.RefreshMs = ms; _monitor.RefreshMs = ms; _config.Save();
                        _sensorTimer.Change(0, ms);
                        SendConfig();
                    }
                    break;
                case "setThresholds":
                    if (GetDouble(doc, "cpuWarn", out double v1)) _config.CpuWarnTemp = v1;
                    if (GetDouble(doc, "cpuCrit", out double v2)) _config.CpuCritTemp = v2;
                    if (GetDouble(doc, "gpuWarn", out double v3)) _config.GpuWarnTemp = v3;
                    if (GetDouble(doc, "gpuCrit", out double v4)) _config.GpuCritTemp = v4;
                    _config.Save(); SendConfig();
                    break;
                case "setBatteryAlert":
                    ApplyBatterySettings(doc.RootElement);
                    break;
                case "testBatteryAlert":
                    _notifier.NotifyBatteryTest(_config);
                    break;
                case "setCpuSensor":
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                    {
                        _config.PreferredCpuSensor = idEl.GetString() ?? "auto";
                        _monitor.PreferredCpuSensor = _config.PreferredCpuSensor;
                        _config.Save();
                        PushSnapshot();
                    }
                    break;
                case "setPaused":
                    // La lectura continúa en el backend; la pausa solo congela
                    // gráficas e historial en el frontend (no se envía nada).
                    break;
                case "setUnit":
                    if (doc.RootElement.TryGetProperty("unit", out var uEl))
                    {
                        string u = (uEl.GetString() ?? "C").ToUpperInvariant();
                        _config.Unit = u == "F" ? "F" : "C";
                        _config.Save(); SendConfig();
                    }
                    break;
                case "setStartMinimized":
                    if (doc.RootElement.TryGetProperty("value", out var smEl))
                    {
                        _config.StartMinimized = smEl.GetBoolean();
                        _config.Save(); SendConfig();
                    }
                    break;
                case "setStartWithWindows":
                    if (doc.RootElement.TryGetProperty("value", out var swEl))
                    {
                        _config.StartWithWindows = swEl.GetBoolean();
                        _config.Save();
                        StartupManager.Apply(_config.StartWithWindows);
                        SendConfig();
                    }
                    break;
                case "resetSettings":
                    _config.ResetToDefaults();
                    _config.Save();
                    _monitor.RefreshMs = _config.RefreshMs;
                    _monitor.PreferredCpuSensor = _config.PreferredCpuSensor;
                    _batteryService.ApplyOptions(BatteryAlertOptions.FromConfig(_config));
                    _fullService.ApplyOptions(FullOptions());
                    StartupManager.Apply(false);
                    _sensorTimer.Change(0, _config.RefreshMs);
                    SendConfig();
                    PushSnapshot();
                    break;
                case "restartAdmin": RestartAsAdmin(); break;
                case "openMini": OpenMini(); break;
                case "setHvci":
                    if (doc.RootElement.TryGetProperty("enabled", out var hvEl))
                        _ = ApplyHvciAsync(hvEl.GetBoolean());
                    break;
                case "rebootNow":
                    try
                    {
                        Process.Start(new ProcessStartInfo("shutdown", "/r /t 15 /c \"Reinicio solicitado por TempPanel\"") { UseShellExecute = false, CreateNoWindow = true });
                    }
                    catch (Exception ex) { AppLogger.Error("reboot: " + ex.Message); }
                    break;
                case "openLogs":
                    try
                    {
                        string dir = Sensors.AppLogger.LogDirectory;
                        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                    }
                    catch (Exception ex) { AppLogger.Error("openLogs: " + ex.Message); }
                    break;
                case "checkUpdates": _ = CheckUpdatesAsync(manual: true); break;
                case "downloadUpdate":
                    if (doc.RootElement.TryGetProperty("url", out var urlEl) &&
                        doc.RootElement.TryGetProperty("tag", out var tagEl))
                        _ = DownloadUpdateAsync(urlEl.GetString() ?? "", tagEl.GetString() ?? "");
                    break;
                case "retryLaunch": _ = RetryLaunchAsync(); break;
                case "setMiniOptions":
                    if (doc.RootElement.TryGetProperty("series", out var serEl))
                        _config.MiniSeries = serEl.GetString() ?? _config.MiniSeries;
                    if (doc.RootElement.TryGetProperty("expanded", out var exEl))
                        _config.MiniExpanded = exEl.GetBoolean();
                    _config.Save();
                    _mini?.ApplyOptions();
                    SendConfig();
                    break;
                case "resetMiniPos": _mini?.ResetPosition(); break;
                case "defenderStatus": _ = DefenderStatusAsync(); break;
                case "defenderAdd": _ = DefenderApplyAsync(add: true); break;
                case "defenderRemove": _ = DefenderApplyAsync(add: false); break;
                case "setAutoUpdates":
                    if (doc.RootElement.TryGetProperty("value", out var auEl))
                    {
                        _config.AutoCheckUpdates = auEl.GetBoolean();
                        _config.Save();
                        SendConfig();
                    }
                    break;
                case "setFirstRunDone":
                    _config.FirstRun = false;
                    _config.Save();
                    SendConfig();
                    break;
                // --- v2.5.0 ---
                case "view":
                    // Página visible: la tabla completa de sensores solo se
                    // construye y envía cuando hace falta (menos RAM y CPU).
                    if (doc.RootElement.TryGetProperty("page", out var pgEl))
                    {
                        _page = pgEl.GetString() ?? "resumen";
                        _monitor.IncludeAllSensors = _page == "sensores";
                        if (_page == "sensores") PushSnapshot();
                    }
                    break;
                case "winMin": WindowState = WindowState.Minimized; break;
                case "winMax":
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    break;
                case "winClose": Close(); break;
            }
        }
        catch (Exception ex) { AppLogger.Error("WebMessage: " + ex.Message); }
    }

    private static bool GetDouble(JsonDocument doc, string name, out double value)
    {
        value = 0;
        return doc.RootElement.TryGetProperty(name, out var el) && el.TryGetDouble(out value);
    }

    private BatteryAlertOptions FullOptions() => new()
    {
        Enabled = _config.BatteryFullEnabled,
        ThresholdPct = 99,
        RearmPct = 95,
        Sound = _config.BatterySound,
        Mode = _config.BatteryMode == "balloon" ? BatteryAlertMode.Balloon : BatteryAlertMode.Popup
    };

    private void ApplyBatterySettings(JsonElement root)
    {
        if (root.TryGetProperty("enabled", out var en)) _config.BatteryAlertsEnabled = en.GetBoolean();
        if (root.TryGetProperty("threshold", out var th) && th.TryGetDouble(out double t))
            _config.BatteryThreshold = Math.Clamp(t, 50, 100);
        if (root.TryGetProperty("rearm", out var re) && re.TryGetDouble(out double r))
            _config.BatteryRearm = Math.Clamp(r, 10, 99);
        if (root.TryGetProperty("sound", out var so)) _config.BatterySound = so.GetBoolean();
        if (root.TryGetProperty("mode", out var mo))
        {
            string m = (mo.GetString() ?? "popup").ToLowerInvariant();
            _config.BatteryMode = m == "balloon" ? "balloon" : "popup";
        }
        if (root.TryGetProperty("full", out var fu)) _config.BatteryFullEnabled = fu.GetBoolean();
        _config.Normalize(); // garantiza reactivación < alerta
        _config.Save();
        _batteryService.ApplyOptions(BatteryAlertOptions.FromConfig(_config));
        _fullService.ApplyOptions(FullOptions());
        SendConfig();
    }

    // ---------------- Sensores ----------------
    private void PushSnapshot()
    {
        if (!_webReady) return;
        try
        {
            Sensors.Snapshot snap;
            lock (_monLock)
            {
                _monitor.IncludeAllSensors = _page == "sensores";
                snap = _monitor.GetSnapshot();
            }
            if (snap.Cpu.PackageTemp.HasValue) _everHadCpuTemp = true;
            try
            {
                // Memoria real del proceso: visible en Ayuda (transparencia total).
                _self.Refresh();
                snap.Meta.AppMemoryMb = Math.Round(_self.WorkingSet64 / 1048576.0, 1);
            }
            catch { }
            string json = JsonSerializer.Serialize(new { type = "snapshot", data = snap }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); }
                catch (Exception ex) { AppLogger.Error("PostMessage: " + ex.Message); }
                try { UpdateTray(snap); } catch { }
                try { _mini?.Update(snap); } catch { }
            });
        }
        catch (Exception ex) { AppLogger.Error("PushSnapshot: " + ex); }
    }

    private void SendConfig()
    {
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                type = "config",
                data = new
                {
                    refreshMs = _config.RefreshMs,
                    cpuWarnTemp = _config.CpuWarnTemp,
                    cpuCritTemp = _config.CpuCritTemp,
                    gpuWarnTemp = _config.GpuWarnTemp,
                    gpuCritTemp = _config.GpuCritTemp,
                    preferredCpuSensor = _config.PreferredCpuSensor,
                    unit = _config.Unit,
                    startMinimized = _config.StartMinimized,
                    startWithWindows = _config.StartWithWindows,
                    batteryEnabled = _config.BatteryAlertsEnabled,
                    batteryThreshold = _config.BatteryThreshold,
                    batteryRearm = _config.BatteryRearm,
                    batterySound = _config.BatterySound,
                    batteryMode = _config.BatteryMode,
                    batteryFull = _config.BatteryFullEnabled,
                    autoCheckUpdates = _config.AutoCheckUpdates,
                    firstRun = _config.FirstRun,
                    miniSeries = _config.MiniSeries,
                    miniExpanded = _config.MiniExpanded,
                    glass = _glassMode,
                    version = UpdateService.Current().ToString()
                }
            }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>Resumen vivo en la bandeja (máx. 63 caracteres).</summary>
    private void UpdateTray(Sensors.Snapshot snap)
    {
        try
        {
            string cpu = snap.Cpu.PackageTemp.HasValue ? $"{snap.Cpu.PackageTemp.Value:0.0}°" : "—";
            string load = snap.Cpu.TotalLoad.HasValue ? $"{snap.Cpu.TotalLoad.Value:0}%" : "—";
            string bat = snap.Battery.Present && snap.Battery.Percent.HasValue ? $"{snap.Battery.Percent.Value:0.0}%" : "—";
            string tip = $"TempPanel · CPU {cpu} · {load} · Bat {bat}";
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _tray.Text = tip;
        }
        catch { }
    }

    private void MaybeRecycleMonitor()
    {
        try
        {
            if (_everHadCpuTemp) return;
            if (!HardwareMonitor.IsRunningAsAdmin()) return;
            AppLogger.Info("Reciclando lector de sensores para reintentar despliegue del driver.");
            lock (_monLock)
            {
                try { _monitor.Dispose(); } catch { }
                _monitor = new HardwareMonitor
                {
                    RefreshMs = _config.RefreshMs,
                    PreferredCpuSensor = _config.PreferredCpuSensor
                };
            }
        }
        catch (Exception ex) { AppLogger.Error("MaybeRecycle: " + ex.Message); }
    }

    // ---------------- Batería (cada 15 s) ----------------
    private void CheckBattery()
    {
        try
        {
            // Se reutiliza la última lectura del monitor (ya usa capacidades
            // reales de root\wmi): sin duplicar consultas WMI.
            BatteryData bat;
            lock (_monLock) bat = _monitor.LastBattery;
            if (!bat.Present || bat.Percent is not double pct) return;
            if (pct < 0 || pct > 100) return;
            bool charging = bat.Charging ?? false;
            _batteryService.Evaluate(pct, charging);
            _fullService.Evaluate(pct, charging);
        }
        catch (Exception ex) { AppLogger.Error("CheckBattery: " + ex.Message); }
    }

    private void OnBatteryAlert(object? sender, double pct)
    {
        try
        {
            Dispatcher.BeginInvoke(() => _notifier.NotifyBattery(_config, pct));
        }
        catch (Exception ex) { AppLogger.Error("OnBatteryAlert: " + ex.Message); }
    }

    private void OnFullAlert(object? sender, double pct)
    {
        try
        {
            Dispatcher.BeginInvoke(() => _notifier.NotifyFull(_config, pct));
        }
        catch (Exception ex) { AppLogger.Error("OnFullAlert: " + ex.Message); }
    }

    private void ShowBatteryPopup(string title, string message, bool playSound)
    {
        // El modo popup siempre usa el mensaje del umbral configurado.
        var win = new BatteryAlertWindow(
            $"TempPanel — Batería al {_config.BatteryThreshold:0} %",
            BatteryAlertWindow.ThresholdMessage(_config.BatteryThreshold));
        win.Owner = IsVisible ? this : null;
        if (playSound) { try { System.Media.SystemSounds.Exclamation.Play(); } catch { } }
        win.ShowDialog();
    }

    private void ShowFullPopup(double pct, bool playSound)
    {
        var win = new BatteryAlertWindow(
            "TempPanel — Batería cargada",
            BatteryAlertWindow.FullMessage(pct));
        win.Owner = IsVisible ? this : null;
        if (playSound) { try { System.Media.SystemSounds.Exclamation.Play(); } catch { } }
        win.ShowDialog();
    }

    private void ShowTrayBalloon(string title, string message, bool playSound)
    {
        try
        {
            if (playSound) { try { System.Media.SystemSounds.Exclamation.Play(); } catch { } }
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = message;
            _tray.ShowBalloonTip(10000);
        }
        catch (Exception ex) { AppLogger.Error("Balloon: " + ex.Message); }
    }

    private async Task ApplyHvciAsync(bool enable)
    {
        await Task.Run(() =>
        {
            var result = HvciManager.SetEnabled(enable);
            string json = JsonSerializer.Serialize(new
            {
                type = "hvciStatus",
                data = new
                {
                    enabled = HvciManager.IsEnabled(),
                    result = result.ToString()
                }
            }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); }
                catch { }
            });
        });
    }

    // ---------------- Actualizaciones ----------------
    private async Task CheckUpdatesAsync(bool manual)
    {
        try
        {
            SendUpdateStatus("checking", null);
            var result = await UpdateService.CheckAsync();
            if (result.Info == null)
            {
                // Sin novedad, sin releases o sin red: solo se informa si fue manual.
                if (manual) SendUpdateStatus(result.NoReleases ? "noreleases" : "uptodate", null);
                return;
            }
            SendUpdateStatus("available", result.Info);
        }
        catch (Exception ex)
        {
            AppLogger.Error("CheckUpdates: " + ex.Message);
            if (manual) SendUpdateStatus("error", null);
        }
    }

    private void SendUpdateStatus(string state, UpdateInfo? info)
    {
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                type = "updateStatus",
                data = new
                {
                    state,
                    current = UpdateService.Current().ToString(),
                    latest = info?.Tag ?? "",
                    url = info?.SetupUrl ?? "",
                    notes = info?.Notes ?? ""
                }
            }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); }
                catch { }
            });
        }
        catch { }
    }

    private async Task DownloadUpdateAsync(string url, string tag)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            SendUpdateStatus("downloading", null);
            string? path = await UpdateService.DownloadAndLaunchAsync(url, tag);
            if (path == null) SendUpdateStatus("dlerror", null);
            else ExitApp(); // el instalador ya está en marcha
        }
        catch (Exception ex)
        {
            AppLogger.Error("DownloadUpdate: " + ex.Message);
            SendUpdateStatus("error", null);
        }
    }

    private async Task RetryLaunchAsync()
    {
        try
        {
            SendUpdateStatus("downloading", null);
            bool ok = await UpdateService.RetryLaunchAsync();
            if (ok) ExitApp();
            else SendUpdateStatus("dlerror", null);
        }
        catch (Exception ex)
        {
            AppLogger.Error("RetryLaunch: " + ex.Message);
            SendUpdateStatus("dlerror", null);
        }
    }

    // ---------------- Defender (antivirus) ----------------
    private async Task DefenderStatusAsync()
    {
        try
        {
            var st = await Task.Run(DefenderManager.GetStatus);
            SendDefenderStatus(st);
        }
        catch (Exception ex)
        {
            AppLogger.Error("DefenderStatus: " + ex.Message);
            SendDefenderStatus(new DefenderManager.DefenderStatus { Ok = false, Message = "Error consultando Defender: " + ex.Message });
        }
    }

    private async Task DefenderApplyAsync(bool add)
    {
        try
        {
            SendDefenderStatus(new DefenderManager.DefenderStatus
            {
                Ok = true,
                Message = add ? "Aplicando exclusión en Defender…" : "Quitando exclusión…"
            });
            var st = await Task.Run(() => add ? DefenderManager.AddExclusions() : DefenderManager.RemoveExclusions());
            SendDefenderStatus(st);
        }
        catch (Exception ex)
        {
            AppLogger.Error("DefenderApply: " + ex.Message);
            SendDefenderStatus(new DefenderManager.DefenderStatus { Ok = false, Message = "Error aplicando la exclusión: " + ex.Message });
        }
    }

    private void SendDefenderStatus(DefenderManager.DefenderStatus st)
    {
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                type = "defenderStatus",
                data = new { ok = st.Ok, excluded = st.Excluded, paths = st.Paths, message = st.Message }
            }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); }
                catch { }
            });
        }
        catch { }
    }

    // ---------------- Ventana / bandeja ----------------
    private void OnClosingWindow(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        // Minimizar a la bandeja en lugar de cerrar
        e.Cancel = true;
        Hide();
        _ = SetWebVisibleAsync(false); // libera RAM del motor de interfaz
        _tray.ShowBalloonTip(4000, "TempPanel", "TempPanel sigue ejecutándose en la bandeja del sistema.", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void RestoreWindow()
    {
        try
        {
            if (_mini != null) { try { _mini.Close(); } catch { } _mini = null; }
        }
        catch { }
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _ = SetWebVisibleAsync(true); // reanuda el motor de interfaz
    }

    private void OpenMini()
    {
        try
        {
            if (_mini == null)
            {
                _mini = new MiniWindow(_config, OnMiniConfigChanged, RestoreWindow);
                _mini.Closed += (_, __) => { _mini = null; };
                _mini.Show();
            }
            else
            {
                _mini.ApplyOptions();
                _mini.Activate();
            }
            Hide(); // la principal se esconde; la mini queda fija encima
            _ = SetWebVisibleAsync(false); // la mini es nativa: no necesita el motor grande
        }
        catch (Exception ex) { AppLogger.Error("OpenMini: " + ex.Message); }
    }

    /// <summary>La mini ventana cambió opciones (series, tamaño, posición): persistir y sincronizar la UI.</summary>
    private void OnMiniConfigChanged()
    {
        try { _config.Save(); SendConfig(); }
        catch (Exception ex) { AppLogger.Error("MiniConfig: " + ex.Message); }
    }

    private void ExitApp()
    {
        _allowClose = true;
        Close();
    }

    private void Cleanup()
    {
        try { _sensorTimer.Dispose(); } catch { }
        try { _batteryTimer.Dispose(); } catch { }
        try { _recycleTimer.Dispose(); } catch { }
        try { _monitor.Dispose(); } catch { }
        try
        {
            if (_mini != null) { try { _mini.Close(); } catch { } _mini = null; }
        }
        catch { }
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        AppLogger.Info("TempPanel cerrado correctamente.");
    }

    private void RestartAsAdmin()
    {
        try
        {
            string exe = Process.GetCurrentProcess().MainModule?.FileName
                ?? AppDomain.CurrentDomain.BaseDirectory + "TempPanel.exe";
            var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", Arguments = "--takeover" };
            Process.Start(psi);
            ExitApp();
        }
        catch (Exception ex)
        {
            AppLogger.Error("RestartAsAdmin: " + ex.Message);
            System.Windows.MessageBox.Show("No se pudo reiniciar como administrador.\n" + ex.Message,
                "TempPanel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
