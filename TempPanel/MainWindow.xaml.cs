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
    // v3.0.0 — arranque rápido: el lector LHM se crea en segundo plano DESPUÉS
    // de mostrar la ventana (su apertura tarda ~1 s y bloqueaba el primer pintado).
    private HardwareMonitor? _monitor;
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

    /// <summary>
    /// El control WebView2 con transparencia (WebView2CompositionControl) necesita
    /// el ensamblado de proyecciones del SDK de Windows. Se comprueba una sola vez:
    /// si no está, se usa el control clásico en lugar de dejar la ventana en negro.
    /// </summary>
    private static readonly bool WindowsSdkProjections = CheckWindowsSdkProjections();

    private static bool CheckWindowsSdkProjections()
    {
        try { System.Reflection.Assembly.Load("Microsoft.Windows.SDK.NET"); return true; }
        catch { return false; }
    }
    private Microsoft.Web.WebView2.Wpf.IWebView2? _web;      // control activo (cristal o clásico)
    private FrameworkElement? _webElement;
    private CoreWebView2? _core;
    private bool _glassMode;
    private readonly bool _uiCheck;
    /// <summary>--uicheck: la interfaz ya cargó (lo consulta el modo de prueba).</summary>
    public bool UiReady { get; private set; }
    /// <summary>--uicheck: el lector de sensores terminó de abrirse.</summary>
    public bool SensorsReady { get; private set; }
    /// <summary>--uicheck: se está usando el control con transparencia.</summary>
    public bool GlassActive => _glassMode;
    private bool _webSuspended;
    private bool _webVisible = true;
    private string _page = "resumen";
    // Franja superior arrastrable (barra de la interfaz) y zona de botones.
    private const double CaptionHeight = 44;
    private const double CaptionButtonsWidth = 150;
    // Contrato con la interfaz (app.css): botones de 38x30, separación 4 px y
    // margen derecho 8 px. Sirven para devolver HTMAXBUTTON (Snap Layouts Win11).
    private const double CaptionButtonW = 38;
    private const double CaptionButtonH = 30;
    private const double CaptionButtonGap = 4;
    private const double CaptionRightPad = 8;

    // --- v3.0.0: integración con Windows y rendimiento adaptativo ---
    private readonly System.Windows.Threading.DispatcherTimer _perfTimer;
    private IntPtr _hwnd;
    private string _osName = "Windows";
    private int _osBuild;
    private bool _osWin11;
    private string _osAccent = "#4f8cff";
    private bool _hotkeyRegistered;
    private string _perfMode = PerformanceGovernor.Mode.Normal;
    private int _effectiveMs = 2000;
    private bool _webSuspendedByPerf;
    private bool _sessionLocked;
    private string _health = "ok";
    private bool _hoverMax;
    private Sensors.Snapshot? _lastSnap;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private const int HotkeyId = 0xB10;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private const int WM_NCMOUSELEAVE = 0x02A3;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTMAXBUTTON = 9;

    public MainWindow() : this(false) { }

    /// <param name="uiCheck">Modo de verificación (--uicheck): perfil de WebView2
    /// aparte, sin bandeja y sin diálogos modales, para poder probar la ventana
    /// desde la línea de comandos sin tocar la app que el usuario tenga abierta.</param>
    public MainWindow(bool uiCheck)
    {
        InitializeComponent();
        _uiCheck = uiCheck;

        // El control con transparencia (WebView2CompositionControl) necesita las
        // proyecciones del SDK de Windows (Microsoft.Windows.SDK.NET). Si faltan,
        // el control revienta al cargarse (FileNotFoundException) y la ventana se
        // queda en negro: se saca del árbol ANTES de que eso ocurra y la app usa
        // el control clásico (opaco pero visible). Nunca una ventana vacía.
        if (!WindowsSdkProjections)
        {
            Root.Children.Remove(WebGlass);
            AppLogger.Info("Cristal: faltan las proyecciones del SDK de Windows; se usará el control clásico.");
        }

        _config = AppConfig.Load();
        var os = SystemInfo.WindowsVersion();
        _osName = os.Name;
        _osBuild = os.Build;
        _osWin11 = os.Win11;
        try { _osAccent = SystemInfo.AccentHex(); } catch { }
        _effectiveMs = _config.RefreshMs;
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
            Visible = !uiCheck
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
        menu.Items.Add("Sensores", null, (_, __) => NavigateTo("sensores"));
        menu.Items.Add("Configuración", null, (_, __) => NavigateTo("config"));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, __) => ExitApp());
        _tray.ContextMenuStrip = menu;

        _sensorTimer = new System.Threading.Timer(_ => PushSnapshot(), null, Timeout.Infinite, Timeout.Infinite);
        _batteryTimer = new System.Threading.Timer(_ => CheckBattery(), null, Timeout.Infinite, Timeout.Infinite);
        // Si como admin nunca hubo temperaturas (p. ej. antivirus retiró el
        // driver a mitad de sesión y luego se permitió), reintenta desplegar
        // cada 10 minutos recreando el lector. Barato y honesto.
        _recycleTimer = new System.Threading.Timer(_ => MaybeRecycleMonitor(), null, Timeout.Infinite, Timeout.Infinite);

        // v3.0.0 — gobernador de rendimiento: cada 3 s revisa si hay una app a
        // pantalla completa, ahorro de energía o cambios de foco. Barato y sin
        // tocar hardware.
        _perfTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _perfTimer.Tick += (_, __) => ApplyPerformance();

        Loaded += OnLoadedWindow;
        StateChanged += (_, __) => ApplyPerformance();
        Activated += (_, __) => Dispatcher.BeginInvoke(() => ApplyPerformance());
        Deactivated += (_, __) => Dispatcher.BeginInvoke(() => ApplyPerformance());
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
            _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            // Cristal acrílico de Windows 11 + esquinas redondeadas (si el
            // sistema lo soporta). Si no, la ventana queda oscura y opaca.
            bool acrylic = WindowFx.SupportsGlass() && WindowFx.ApplyAcrylic(this);
            AppLogger.Info("Cristal: " + (acrylic ? "acrílico aplicado" : "no disponible (tema oscuro sólido)"));

            // El hook se instala SIEMPRE (v3.0.0): además del arrastre nativo,
            // gestiona el atajo global, Snap Layouts y el hover del maximizar.
            if (_hwnd != IntPtr.Zero)
                System.Windows.Interop.HwndSource.FromHwnd(_hwnd)?.AddHook(WndProcHook);

            AppLogger.Info($"Ventana mostrada en {(DateTime.UtcNow - _startedAt).TotalMilliseconds:0} ms · {_osName} (build {_osBuild}).");

            // Arranque rápido: el lector de sensores se abre DESPUÉS de que la
            // interfaz esté lista (ver NavigationCompleted), para no competir
            // con el arranque de WebView2.
            RegisterHotkey();
            SubscribeSystemEvents();
            _perfTimer.Start();
            ApplyPerformance(force: true);
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
                UpdateService.Current().ToString() + (_uiCheck ? "-uicheck" : "")); // perfil por versión: evita bloqueos de perfiles viejos
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
            if (WindowFx.SupportsGlass() && WindowsSdkProjections)
            {
                try
                {
                    // El control con composición necesita estar visible y con tamaño
                    // para crear su superficie; si no, la inicialización no termina.
                    WebGlass.Visibility = Visibility.Visible;
                    WebGlass.DefaultBackgroundColor = System.Drawing.Color.Transparent;
                    UpdateLayout();
                    // Con tope de tiempo: si el control no responde, se pasa al
                    // clásico en vez de dejar la ventana en negro para siempre.
                    var init = WebGlass.EnsureCoreWebView2Async(env);
                    if (await Task.WhenAny(init, Task.Delay(20000)) != init)
                        throw new TimeoutException("el control con transparencia no respondió en 20 s");
                    await init;
                    _web = WebGlass;
                    _webElement = WebGlass;
                    _glassMode = true;
                    AppLogger.Info("WebView2: control con transparencia (cristal) inicializado.");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Control con transparencia no disponible: " + ex.Message);
                    try { WebGlass.Visibility = Visibility.Collapsed; } catch { }
                    try { Root.Children.Remove(WebGlass); } catch { }
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
                    try { LoadingLayer.Visibility = Visibility.Collapsed; } catch { }
                    if (!_uiCheck)
                        System.Windows.MessageBox.Show(
                            "No se pudo cargar la interfaz.\n\n" +
                            $"Error: {nav.WebErrorStatus} (HTTP {nav.HttpStatusCode}).\n" +
                            "Cierra la app, borra %LocalAppData%\\TempPanel\\WebView2 e inténtalo de nuevo.",
                            "TempPanel", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _webReady = true;
                UiReady = true;
                try { LoadingLayer.Visibility = Visibility.Collapsed; } catch { }
                lock (_monLock) { if (_monitor != null) _monitor.IncludeAllSensors = _page == "sensores"; }
                SendConfig();
                // v3.0.0: el intervalo real lo decide el gobernador (foco,
                // pantalla completa, ahorro de energía). Forzar deja el motor
                // reanudado y el temporizador con el intervalo efectivo.
                ApplyPerformance(force: true);
                // Con la interfaz ya en pantalla, se abre el lector de sensores
                // en segundo plano (driver + enumeración de hardware ~1 s).
                _ = InitMonitorAsync();
                _batteryTimer.Change(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
                _recycleTimer.Change(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
                if (_config.AutoCheckUpdates) _ = CheckUpdatesAsync(manual: false);
                AppLogger.Info($"Interfaz lista en {(DateTime.UtcNow - _startedAt).TotalMilliseconds:0} ms.");
            };
            _web.Source = new Uri("https://temppanel.local/index.html");
        }
        catch (Exception ex)
        {
            AppLogger.Error("InitWebView2: " + ex);
            try
            {
                // Sin interfaz: se retira la pantalla de carga y se deja un
                // fondo sólido legible (nunca una ventana vacía o negra).
                LoadingLayer.Visibility = Visibility.Collapsed;
                if (Root.Background == null)
                    Root.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x0B, 0x12, 0x20));
            }
            catch { }
            if (!_uiCheck)
                System.Windows.MessageBox.Show("No se pudo iniciar WebView2.\n\n" + ex.Message +
                    "\n\nInstala el runtime WebView2 desde:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                    "TempPanel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------- Cristal + integración con Windows (v3.0.0) ----------------

    /// <summary>
    /// Arrastre nativo de la franja superior, Snap Layouts de Windows 11
    /// (HTMAXBUTTON sobre el botón maximizar), hover del botón y atajo global.
    /// </summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCAPTION = 2;
        try
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                ToggleFromHotkey();
                return IntPtr.Zero;
            }
            if (msg == WM_NCLBUTTONDOWN && _glassMode && wParam.ToInt32() == HTMAXBUTTON)
            {
                // El clic lo gestiona la app (el sistema lo reserva para Snap
                // Layouts y no llega al HTML): alterna maximizar/restaurar.
                handled = true;
                ToggleMaximize();
                return IntPtr.Zero;
            }
            if (msg == WM_NCMOUSEMOVE)
            {
                SetMaxHover(_glassMode && wParam.ToInt32() == HTMAXBUTTON);
            }
            else if (msg == WM_NCMOUSELEAVE)
            {
                SetMaxHover(false);
            }

            if (msg == WM_NCHITTEST && _glassMode)
            {
                long lp = lParam.ToInt64();
                double sx = (short)(lp & 0xFFFF);
                double sy = (short)((lp >> 16) & 0xFFFF);
                var p = PointFromScreen(new System.Windows.Point(sx, sy));
                const double border = 8; // deja el borde para redimensionar (WindowChrome)
                bool inside = p.X > border && p.Y > border &&
                              p.X < ActualWidth - border && p.Y < ActualHeight - border;
                if (inside && IsMaxButton(p))
                {
                    handled = true;
                    return (IntPtr)HTMAXBUTTON;
                }
                if (inside && p.Y < CaptionHeight && p.X < ActualWidth - CaptionButtonsWidth)
                {
                    handled = true;
                    return (IntPtr)HTCAPTION;
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    /// <summary>Zona del botón maximizar (mismo contrato que app.css).</summary>
    private bool IsMaxButton(System.Windows.Point p)
    {
        double top = (CaptionHeight - CaptionButtonH) / 2;
        if (p.Y < top || p.Y >= top + CaptionButtonH) return false;
        double closeLeft = ActualWidth - CaptionRightPad - CaptionButtonW;
        double maxLeft = closeLeft - CaptionButtonGap - CaptionButtonW;
        return p.X >= maxLeft && p.X < closeLeft - CaptionButtonGap;
    }

    private void SetMaxHover(bool on)
    {
        if (on == _hoverMax) return;
        _hoverMax = on;
        PostUi(new { type = "hover", data = new { id = on ? "win-max" : null } });
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        ApplyPerformance();
    }

    private void PostUi(object payload)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); } catch { }
            });
        }
        catch { }
    }

    // ---------------- Arranque rápido: lector en segundo plano ----------------

    private async Task InitMonitorAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var mon = await Task.Run(() => new HardwareMonitor
            {
                RefreshMs = _config.RefreshMs,
                PreferredCpuSensor = _config.PreferredCpuSensor
            });
            lock (_monLock) { _monitor = mon; }
            SensorsReady = true;
            AppLogger.Info($"Sensores listos en {sw.ElapsedMilliseconds} ms.");
            if (_webReady) PushSnapshot();
        }
        catch (Exception ex) { AppLogger.Error("InitMonitor: " + ex.Message); }
    }

    // ---------------- Rendimiento adaptativo ----------------

    /// <summary>
    /// Ajusta el intervalo de los sensores y suspende/reanuda WebView2 según
    /// visibilidad, foco, pantalla completa y ahorro de energía. Se llama por
    /// eventos (foco, minimizar, bandeja, mini) y cada 3 s por si cambia el
    /// primer plano. La lógica pura vive en <see cref="PerformanceGovernor"/>.
    /// </summary>
    private void ApplyPerformance(bool force = false)
    {
        if (_allowClose) return;
        try
        {
            bool mainVisible = IsVisible && WindowState != WindowState.Minimized && !_sessionLocked;
            bool miniActive = _mini != null && !_sessionLocked;
            bool gameFullscreen = mainVisible && _hwnd != IntPtr.Zero && SystemInfo.ForegroundIsFullscreen(_hwnd);
            bool saver = SystemInfo.PowerSaverActive();

            PerformanceGovernor.Decision d;
            if (miniActive)
            {
                // La mini ventana es nativa y ligera: mantiene el ritmo elegido
                // aunque la ventana principal esté oculta, porque es lo que el
                // usuario está mirando. El motor WebView2 sí sigue suspendido.
                d = new PerformanceGovernor.Decision(
                    Math.Clamp(_config.RefreshMs, PerformanceGovernor.MinMs, 10000),
                    PerformanceGovernor.Mode.Normal, false);
            }
            else
            {
                d = PerformanceGovernor.Evaluate(
                    _config.RefreshMs, mainVisible, IsActive, gameFullscreen, saver, _config.AdaptivePerf);
            }

            // El motor de interfaz solo se necesita si la ventana principal se ve.
            bool suspendWeb = d.SuspendWeb || !mainVisible;

            bool modeChanged = d.ModeName != _perfMode;
            bool intervalChanged = d.EffectiveMs != _effectiveMs;
            bool suspendChanged = suspendWeb != _webSuspendedByPerf;

            _perfMode = d.ModeName;
            if (intervalChanged || force)
            {
                _effectiveMs = d.EffectiveMs;
                try { _sensorTimer.Change(0, _effectiveMs); } catch { }
            }
            if (suspendChanged || force)
            {
                _webSuspendedByPerf = suspendWeb;
                // Nunca suspender durante la inicialización de WebView2: el
                // control necesita estar visible para crear su superficie. Al
                // terminar la navegación se aplica el estado decidido.
                if (_webReady) _ = SetWebVisibleAsync(!suspendWeb);
            }
            if (modeChanged || intervalChanged || suspendChanged)
            {
                AppLogger.Info($"Rendimiento: {_perfMode} · cada {_effectiveMs} ms · WebView2 {(_webSuspendedByPerf ? "suspendida" : "activa")}");
            }
            if (modeChanged || force) SendConfig();
        }
        catch (Exception ex) { AppLogger.Error("ApplyPerformance: " + ex.Message); }
    }

    // ---------------- Atajo global Ctrl+Alt+T ----------------

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void RegisterHotkey()
    {
        if (_uiCheck || _hwnd == IntPtr.Zero || _hotkeyRegistered) return;
        if (!_config.HotkeyEnabled) return;
        try
        {
            const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, VK_T = 0x54;
            _hotkeyRegistered = RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_T);
            AppLogger.Info(_hotkeyRegistered
                ? "Atajo global Ctrl+Alt+T registrado."
                : "Atajo global Ctrl+Alt+T no disponible (lo usa otra app).");
        }
        catch (Exception ex) { AppLogger.Error("RegisterHotKey: " + ex.Message); }
    }

    private void UnregisterHotkey()
    {
        if (!_hotkeyRegistered || _hwnd == IntPtr.Zero) return;
        try { UnregisterHotKey(_hwnd, HotkeyId); } catch { }
        _hotkeyRegistered = false;
    }

    private void ToggleFromHotkey()
    {
        if (IsVisible && WindowState != WindowState.Minimized && IsActive)
        {
            Hide();
        }
        else
        {
            RestoreWindow();
        }
        ApplyPerformance();
    }

    // ---------------- Eventos del sistema ----------------

    private void SubscribeSystemEvents()
    {
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch (Exception ex) { AppLogger.Error("SystemEvents: " + ex.Message); }
    }

    private void UnsubscribeSystemEvents()
    {
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch { }
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        AppLogger.Info("Sistema reanudado: refrescando sensores.");
        Dispatcher.BeginInvoke(() =>
        {
            ApplyPerformance(force: true);
            PushSnapshot();
        });
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        bool locked = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock;
        if (locked == _sessionLocked) return;
        _sessionLocked = locked;
        AppLogger.Info(locked ? "Sesión bloqueada: actividad reducida." : "Sesión desbloqueada: actividad normal.");
        Dispatcher.BeginInvoke(() => ApplyPerformance());
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        // Tema, color de acento y alto contraste: se reenvían a la interfaz.
        Dispatcher.BeginInvoke(() =>
        {
            try { _osAccent = SystemInfo.AccentHex(); } catch { }
            SendConfig();
            UpdateTaskbar();
        });
    }

    // ---------------- Barra de tareas ----------------

    private static System.Windows.Media.ImageSource MakeBadge(System.Windows.Media.Color color)
    {
        var g = new System.Windows.Media.DrawingGroup();
        var fill = new System.Windows.Media.SolidColorBrush(color);
        fill.Freeze();
        g.Children.Add(new System.Windows.Media.GeometryDrawing(fill, null,
            new System.Windows.Media.EllipseGeometry(new System.Windows.Point(8, 8), 8, 8)));
        var white = System.Windows.Media.Brushes.White;
        g.Children.Add(new System.Windows.Media.GeometryDrawing(white, null,
            new System.Windows.Media.RectangleGeometry(new Rect(7, 3.4, 2, 6.2))));
        g.Children.Add(new System.Windows.Media.GeometryDrawing(white, null,
            new System.Windows.Media.EllipseGeometry(new System.Windows.Point(8, 12.6), 1.15, 1.15)));
        var img = new System.Windows.Media.DrawingImage(g);
        img.Freeze();
        return img;
    }

    private readonly System.Windows.Media.ImageSource _badgeWarn =
        MakeBadge(System.Windows.Media.Color.FromRgb(0xfb, 0xbf, 0x24));
    private readonly System.Windows.Media.ImageSource _badgeCrit =
        MakeBadge(System.Windows.Media.Color.FromRgb(0xf8, 0x71, 0x71));

    /// <summary>
    /// Distintivo de alerta + progreso de carga en el icono de la barra de
    /// tareas. La salud la calcula la interfaz con histéresis y se recibe por
    /// mensaje ("health"): el nativo no duplica esa lógica.
    /// </summary>
    private void UpdateTaskbar()
    {
        try
        {
            if (_uiCheck) return;
            TaskInfo.Overlay = _config.TaskbarBadge && _health != "ok"
                ? (_health == "crit" ? _badgeCrit : _badgeWarn)
                : null;
            var bat = _lastSnap?.Battery;
            if (bat is { Present: true, Charging: true, Percent: double pct })
            {
                TaskInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                TaskInfo.ProgressValue = Math.Clamp(pct / 100.0, 0, 1);
            }
            else
            {
                TaskInfo.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            }
            TaskInfo.Description = _tray.Text;
        }
        catch { }
    }

    private void NavigateTo(string page)
    {
        try
        {
            RestoreWindow();
            _page = page;
            SendConfig();
            PushSnapshot();
            PostUi(new { type = "navigate", data = new { page } });
        }
        catch (Exception ex) { AppLogger.Error("NavigateTo: " + ex.Message); }
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
                        _config.RefreshMs = ms; _config.Save();
                        lock (_monLock) { if (_monitor != null) _monitor.RefreshMs = ms; }
                        ApplyPerformance(force: true); // el intervalo real lo decide el gobernador
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
                        lock (_monLock) { if (_monitor != null) _monitor.PreferredCpuSensor = _config.PreferredCpuSensor; }
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
                    lock (_monLock)
                    {
                        if (_monitor != null)
                        {
                            _monitor.RefreshMs = _config.RefreshMs;
                            _monitor.PreferredCpuSensor = _config.PreferredCpuSensor;
                        }
                    }
                    _batteryService.ApplyOptions(BatteryAlertOptions.FromConfig(_config));
                    _fullService.ApplyOptions(FullOptions());
                    StartupManager.Apply(false);
                    ApplyPerformance(force: true);
                    SendConfig();
                    PushSnapshot();
                    break;
                case "setHotkey":
                    if (doc.RootElement.TryGetProperty("value", out var hkEl))
                    {
                        _config.HotkeyEnabled = hkEl.GetBoolean();
                        _config.Save();
                        UnregisterHotkey();
                        RegisterHotkey();
                        SendConfig();
                    }
                    break;
                case "setTaskbarBadge":
                    if (doc.RootElement.TryGetProperty("value", out var tbbEl))
                    {
                        _config.TaskbarBadge = tbbEl.GetBoolean();
                        _config.Save();
                        UpdateTaskbar();
                        SendConfig();
                    }
                    break;
                case "setAdaptivePerf":
                    if (doc.RootElement.TryGetProperty("value", out var apEl))
                    {
                        _config.AdaptivePerf = apEl.GetBoolean();
                        _config.Save();
                        ApplyPerformance(force: true);
                        SendConfig();
                    }
                    break;
                case "health":
                    {
                        string cls = doc.RootElement.TryGetProperty("cls", out var clsEl)
                            ? (clsEl.GetString() ?? "ok") : "ok";
                        if (cls != "ok" && cls != "warn" && cls != "crit") cls = "ok";
                        if (cls != _health) { _health = cls; UpdateTaskbar(); }
                    }
                    break;
                case "openWindowsSettings":
                    if (doc.RootElement.TryGetProperty("key", out var wsEl))
                        SystemInfo.OpenWindowsSettings(wsEl.GetString() ?? "");
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
                        lock (_monLock) { if (_monitor != null) _monitor.IncludeAllSensors = _page == "sensores"; }
                        if (_page == "sensores") PushSnapshot();
                    }
                    break;
                case "winMin": WindowState = WindowState.Minimized; break;
                case "winMax": ToggleMaximize(); break;
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
                if (_monitor == null) return; // aún abriendo el lector (arranque rápido)
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
            _lastSnap = snap;
            string json = JsonSerializer.Serialize(new { type = "snapshot", data = snap }, _jsonOpts);
            Dispatcher.BeginInvoke(() =>
            {
                try { _core?.PostWebMessageAsJson(json); }
                catch (Exception ex) { AppLogger.Error("PostMessage: " + ex.Message); }
                try { UpdateTray(snap); } catch { }
                try { UpdateTaskbar(); } catch { }
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
                    version = UpdateService.Current().ToString(),
                    // v3.0.0 — integración con Windows y rendimiento adaptativo.
                    adaptivePerf = _config.AdaptivePerf,
                    hotkeyEnabled = _config.HotkeyEnabled,
                    taskbarBadge = _config.TaskbarBadge,
                    effectiveMs = _effectiveMs,
                    perf = _perfMode,
                    os = new
                    {
                        name = _osName,
                        build = _osBuild,
                        win11 = _osWin11,
                        accent = _osAccent,
                        theme = SystemInfo.IsLightTheme() ? "light" : "dark",
                        highContrast = SystemInfo.HighContrast,
                        powerSaver = SystemInfo.PowerSaverActive(),
                        hotkey = _hotkeyRegistered,
                        glass = _glassMode
                    }
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
                try { _monitor?.Dispose(); } catch { }
                _monitor = new HardwareMonitor
                {
                    RefreshMs = _config.RefreshMs,
                    PreferredCpuSensor = _config.PreferredCpuSensor,
                    IncludeAllSensors = _page == "sensores"
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
            lock (_monLock)
            {
                if (_monitor == null) return; // aún abriendo el lector
                bat = _monitor.LastBattery;
            }
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
        ApplyPerformance(); // visible=false: suspende el motor y baja el intervalo
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
        ApplyPerformance(); // reanuda el motor de interfaz
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
            ApplyPerformance(); // la mini es nativa: no necesita el motor grande
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
        try { _perfTimer.Stop(); } catch { }
        UnregisterHotkey();
        UnsubscribeSystemEvents();
        try { _sensorTimer.Dispose(); } catch { }
        try { _batteryTimer.Dispose(); } catch { }
        try { _recycleTimer.Dispose(); } catch { }
        try { lock (_monLock) { _monitor?.Dispose(); _monitor = null; } } catch { }
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
