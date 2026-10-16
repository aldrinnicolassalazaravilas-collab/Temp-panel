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

        Loaded += async (_, __) => await InitWebAsync();
        Closing += OnClosingWindow;
        Closed += (_, __) => Cleanup();

        if (_config.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            Hide();
        }
        StartupManager.Apply(_config.StartWithWindows);
    }

    // ---------------- WebView2 ----------------
    private async Task InitWebAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TempPanel", "WebView2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            string wwwroot = FindWwwRoot();
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "temppanel.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);
            Web.CoreWebView2.WebMessageReceived += OnWebMessage;
            Web.NavigationCompleted += (_, __) =>
            {
                _webReady = true;
                SendConfig();
                _sensorTimer.Change(0, _config.RefreshMs);
                _batteryTimer.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
                _recycleTimer.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
                if (_config.AutoCheckUpdates) _ = CheckUpdatesAsync(manual: false);
            };
            Web.Source = new Uri("https://temppanel.local/index.html");
        }
        catch (Exception ex)
        {
            AppLogger.Error("InitWebView2: " + ex);
            System.Windows.MessageBox.Show("No se pudo iniciar WebView2.\n\n" + ex.Message +
                "\n\nInstala el runtime WebView2 desde:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "TempPanel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
                case "setAutoUpdates":
                    if (doc.RootElement.TryGetProperty("value", out var auEl))
                    {
                        _config.AutoCheckUpdates = auEl.GetBoolean();
                        _config.Save();
                        SendConfig();
                    }
                    break;
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
            lock (_monLock) snap = _monitor.GetSnapshot();
            if (snap.Cpu.PackageTemp.HasValue) _everHadCpuTemp = true;
            string json = JsonSerializer.Serialize(new { type = "snapshot", data = snap }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { Web.CoreWebView2?.PostWebMessageAsJson(json); }
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
                    version = UpdateService.Current().ToString()
                }
            }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { Web.CoreWebView2?.PostWebMessageAsJson(json); }
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
            string bat = snap.Battery.Present && snap.Battery.Percent.HasValue ? $"{snap.Battery.Percent.Value:0}%" : "—";
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
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool none = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) != 0;
            if (none) return; // sin batería: jamás alertar
            double pct = Math.Round(ps.BatteryLifePercent * 100, 1);
            if (pct < 0 || pct > 100) return;
            bool charging = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) != 0;
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
                try { Web.CoreWebView2?.PostWebMessageAsJson(json); }
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

    // ---------------- Ventana / bandeja ----------------
    private void OnClosingWindow(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        // Minimizar a la bandeja en lugar de cerrar
        e.Cancel = true;
        Hide();
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
    }

    private void OpenMini()
    {
        try
        {
            if (_mini == null)
            {
                _mini = new MiniWindow(RestoreWindow);
                _mini.Closed += (_, __) => { _mini = null; };
                _mini.Show();
            }
            else
            {
                _mini.Activate();
            }
            Hide(); // la principal se esconde; la mini queda fija encima
        }
        catch (Exception ex) { AppLogger.Error("OpenMini: " + ex.Message); }
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
