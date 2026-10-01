using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using KawaiiIsland.Services;
using KawaiiIsland.Services.ClaudeCode;
using KawaiiIsland.Services.Native;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

public partial class App : Application
{
    private Mutex? _single;
    private bool _ownsMutex;
    private EventWaitHandle? _wake;
    private TaskbarIcon? _tray;
    private IslandWindow? _island;
    private SettingsWindow? _settings;

    public ConfigService Config { get; private set; } = null!;
    /// <summary>File log in &lt;settings folder&gt;\logs (spec §7).</summary>
    public static ILogger Log { get; private set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    internal IslandWindow Island => _island!;

    /// <summary>Runs on normal exit AND on crash, so OS-level state (e.g. the AppBar reservation) is always released.</summary>
    public static event Action? Cleanup;
    private static int _cleanedUp;
    private static void RunCleanup()
    {
        if (Interlocked.Exchange(ref _cleanedUp, 1) == 0) Cleanup?.Invoke();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Used by the uninstaller: remove our Claude Code hooks so nothing points at a missing app.
        if (e.Args.Contains("--uninstall-hooks", StringComparer.OrdinalIgnoreCase))
        {
            try { ClaudeSettings.Apply(enable: false, port: 0); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            Shutdown();
            return;
        }

        _single = new Mutex(true, @"Local\KawaiiIsland.Single", out _ownsMutex);
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KawaiiIsland.Wake");
        if (!_ownsMutex)
        {
            _wake.Set(); // the running instance opens Settings (spec §6)
            Shutdown();
            return;
        }

        // --data <folder>: run with a separate settings folder (demos, screenshots, portable use).
        int data = Array.FindIndex(e.Args, a => a.Equals("--data", StringComparison.OrdinalIgnoreCase));
        Config = new ConfigService(data >= 0 && data + 1 < e.Args.Length ? e.Args[data + 1] : null);
        Log = FileLoggerProvider.Create(Path.Combine(Config.Directory, "logs"));
        Log.LogInformation("Kawaii Island {Version} starting", typeof(App).Assembly.GetName().Version);

        AppDomain.CurrentDomain.UnhandledException += (_, a) => { Log.LogCritical(a.ExceptionObject as Exception, "Unhandled exception"); RunCleanup(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunCleanup();
        DispatcherUnhandledException += (_, a) => { Log.LogCritical(a.Exception, "Unhandled UI exception"); RunCleanup(); };

        base.OnStartup(e);
        SyncStartWithWindows(explicitToggle: false);
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _island = new IslandWindow(Config);
        _island.Show();
        _tray = BuildTray();

        ThreadPool.RegisterWaitForSingleObject(_wake, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);
    }

    // ---------------- start with Windows ----------------

    /// <summary>Keeps the Run entry in line with the setting. Dev builds only register on an explicit toggle.</summary>
    internal void SyncStartWithWindows(bool explicitToggle)
    {
        string exe = Environment.ProcessPath ?? "";
        bool on = Config.Current.Behavior.StartWithWindows;
        if (on && !explicitToggle && StartupRegistration.IsDevBuild(exe)) return;
        try { StartupRegistration.Apply(on, exe); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.LogWarning(ex, "Couldn't update the Run key");
        }
    }

    // ---------------- theme ----------------

    /// <summary>Swaps the shared brushes; everything bound with DynamicResource updates live.</summary>
    internal void ApplyTheme()
    {
        var a = Config.Current.Appearance;
        bool light = a.Theme == "light" || (a.Theme == "auto" && WindowsUsesLightTheme());
        SetBrush("IslandBackground", light ? "#FFFFFF" : "#000000");
        SetBrush("IslandText", light ? "#1C1C1E" : "#FFFFFF");
        SetBrush("IslandMuted", light ? "#6E6E73" : "#8E8E93");
        SetBrush("IslandTrack", light ? "#E0DCE6" : "#3A3A3C");
        SetBrush("SettingsBackground", light ? "#FFFFFF" : "#1C1C21");
        SetBrush("SettingsPanel", light ? "#F5F2F7" : "#26262D");
        SetBrush("SettingsLine", light ? "#E5E1EA" : "#34343E");
        SetBrush("Accent", a.Accent == "auto" ? WindowsAccent() : a.Accent, fallback: "#FF375F");
    }

    private void SetBrush(string key, string hex, string fallback = "#000000")
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { color = (Color)ColorConverter.ConvertFromString(fallback); }
        Resources[key] = new SolidColorBrush(color);
    }

    /// <summary>Windows' accent colour (follows the wallpaper when "Automatic" is on in Personalization).</summary>
    internal static string WindowsAccent() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr
            ? $"#{abgr & 0xFF:X2}{(abgr >> 8) & 0xFF:X2}{(abgr >> 16) & 0xFF:X2}"
            : "#FF375F";

    private static bool WindowsUsesLightTheme() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int v && v == 1;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle
            && (Config.Current.Appearance.Theme == "auto" || Config.Current.Appearance.Accent == "auto"))
            Dispatcher.BeginInvoke(ApplyTheme);
    }

    // ---------------- menus (tray + right-click on the island share one builder) ----------------

    private TaskbarIcon BuildTray()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillMenu(menu, forIsland: false); // rebuilt on every open: always current
        var tray = new TaskbarIcon
        {
            ToolTipText = "Kawaii Island",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.ico")),
            ContextMenu = menu,
        };
        tray.TrayLeftMouseUp += (_, _) => _island!.ToggleVisible();
        return tray;
    }

    internal ContextMenu IslandMenu()
    {
        var menu = new ContextMenu();
        FillMenu(menu, forIsland: true);
        return menu;
    }

    private void FillMenu(ContextMenu menu, bool forIsland)
    {
        var island = _island!;
        var w = Config.Current.Window;
        bool vertical = Placement.ParseEdge(w.DockEdge) is Edge.Left or Edge.Right;
        menu.Items.Clear();

        menu.Items.Add(forIsland ? Item(island.IsExpanded ? "Collapse" : "Expand", island.ToggleExpanded)
                                 : Item("Show / hide", island.ToggleVisible));
        menu.Items.Add(new Separator());
        menu.Items.Add(Check("Reserve workspace (AppBar)", island.AppBarEnabled, island.SetAppBar));
        menu.Items.Add(Check("Unlock to drag", !island.Locked, on => island.SetLocked(!on)));
        menu.Items.Add(Choice("Edge", [("Top", "Top"), ("Bottom", "Bottom"), ("Left", "Left"), ("Right", "Right")],
                              w.DockEdge, v => { w.DockEdge = v; SettingsChanged(); }));
        menu.Items.Add(Choice("Alignment", AlignmentChoices(vertical), w.Alignment, v => { w.Alignment = v; SettingsChanged(); }));
        menu.Items.Add(Choice("Monitor", MonitorChoices(), w.MonitorId, v => { w.MonitorId = v; SettingsChanged(); }));
        menu.Items.Add(Check("Auto-hide in fullscreen", island.AutoHideFullscreen, on => island.SetAutoHide(on, island.AutoHideAlways)));
        menu.Items.Add(Check("Auto-hide always", island.AutoHideAlways, on => island.SetAutoHide(island.AutoHideFullscreen, on)));
        menu.Items.Add(new Separator());
        string[] mascots = [.. AppConfig.Mascots, AppConfig.NoMascot];
        menu.Items.Add(Choice("Mascot", mascots.Select(m => (MascotName(m), m)).ToArray(),
                              Config.Current.Appearance.Mascot, v => { Config.Current.Appearance.Mascot = v; SettingsChanged(); }));
        menu.Items.Add(Choice("Collapse after", CollapseChoices, island.AutoCollapseSeconds.ToString(), v => island.SetAutoCollapse(int.Parse(v))));
        menu.Items.Add(Check("Coding mode", island.CodeMode, SetCodeMode));
        var timer = new MenuItem { Header = "Timer" };
        foreach (int minutes in new[] { 1, 5, 10, 25, 50 })
            timer.Items.Add(Item($"{minutes} min", () => island.StartTimer(minutes)));
        if (island.TimerActive)
        {
            timer.Items.Add(new Separator());
            timer.Items.Add(Item(island.TimerRunning ? "Pause" : "Resume", island.ToggleTimer));
            timer.Items.Add(Item("Cancel", island.CancelTimer));
        }
        menu.Items.Add(timer);
        menu.Items.Add(Check("Do not disturb", island.Dnd, on => { island.SetDnd(on); _settings?.Reload(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", ShowSettings));
        menu.Items.Add(Item("Quit", Shutdown));
    }

    internal static readonly (string Label, string Value)[] CollapseChoices =
        [("4 seconds", "4"), ("10 seconds", "10"), ("15 seconds", "15"), ("30 seconds", "30")];

    /// <summary>Values stay Left/Center/Right; side docks just label them Top/Middle/Bottom.</summary>
    internal static (string Label, string Value)[] AlignmentChoices(bool vertical) => vertical
        ? [("Top", "Left"), ("Middle", "Center"), ("Bottom", "Right")]
        : [("Left", "Left"), ("Center", "Center"), ("Right", "Right")];

    internal static (string Label, string Value)[] MonitorChoices() =>
        Monitors.All().Select((m, i) => ($"Display {i + 1} · {m.Bounds.Width}×{m.Bounds.Height}{(m.Primary ? " (primary)" : "")}", m.Device)).ToArray();

    internal static string MascotName(string key) => key switch
    {
        "kiko" => "Kiko · anime girl", "miso" => "Miso · cat", "bun" => "Bun · bunny", "bolt" => "Bolt · robot", "ribbit" => "Ribbit · frog",
        AppConfig.NoMascot => "No mascot", _ => key,
    };

    /// <summary>Persist (debounced 500 ms) and re-apply to the island (debounced 150 ms, re-docks the AppBar).</summary>
    /// <summary>Settings window shows state changed elsewhere (e.g. an app muted from the island).</summary>
    internal void RefreshSettings() => _settings?.Reload();

    internal void SettingsChanged()
    {
        Config.SaveSoon();
        _island!.ApplySettingsSoon();
        _settings?.Reload();
    }

    private static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuItem Check(string header, bool isChecked, Action<bool> set)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => set(item.IsChecked); // IsChecked already flipped by the click
        return item;
    }

    private static MenuItem Choice(string header, (string Label, string Value)[] options, string current, Action<string> pick)
    {
        var parent = new MenuItem { Header = header };
        foreach (var (label, value) in options)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => pick(value);
            parent.Items.Add(item);
        }
        return parent;
    }

    /// <summary>Opens Settings in front of everything and hides the island until Settings closes.</summary>
    internal void ShowSettings()
    {
        if (_settings is not { IsLoaded: true })
        {
            bool islandWasVisible = _island!.IsVisible;
            _island.Hide();
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) =>
            {
                _settings = null;
                if (islandWasVisible) _island.ShowIsland();
            };
            _settings.Show();
        }
        // Windows blocks focus-stealing from background processes (tray, second launch): briefly topmost wins.
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Topmost = true;
        _settings.Activate();
        _settings.Topmost = false;
    }

    // ---------------- coding mode + AI agents ----------------

    /// <summary>Coding mode = the local listener (any agent can report to it) + lock-in look. Edits no one's config.</summary>
    internal void SetCodeMode(bool on)
    {
        var code = Config.Current.Modules.Code;
        if (on && _island!.StartCodeMode() is { } error)
        {
            MessageBox.Show(error, "Kawaii Island · Coding mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!on) _island!.StopCodeMode();
        code.Enabled = on;
        Config.SaveSoon();
        _settings?.Reload();
    }

    /// <summary>
    /// Connects Claude Code: adds our hooks (and a status line if there's none) to ~/.claude/settings.json. Explicit
    /// and confirmed the first time; off removes exactly our entries. Turns coding mode on so events have a listener.
    /// </summary>
    internal void SetClaudeHooks(bool on)
    {
        var code = Config.Current.Modules.Code;
        const string title = "Kawaii Island · Claude Code";
        try
        {
            if (on)
            {
                bool first = !code.Consented;
                if (first && MessageBox.Show(
                        "Kawaii Island will show your Claude Code sessions: what Claude is doing, context used, plan usage, " +
                        "and Allow / Deny for permission prompts.\n\n" +
                        $"It adds Kawaii Island hooks (and a status line, if you don't already have one) to:\n{ClaudeSettings.SettingsPath}\n\n" +
                        "A backup is saved next to it. Nothing leaves this PC. Turning this off removes them again.\n\nConnect Claude Code?",
                        title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                bool statusLine = ClaudeSettings.Apply(enable: true, code.Port);
                code.ClaudeHooks = code.Consented = true;
                if (!_island!.CodeMode) SetCodeMode(true);
                if (first) MessageBox.Show(
                    "Claude Code is connected. Restart any open Claude Code sessions: hooks load when a session starts." +
                    (statusLine ? "" : "\n\nYou already use a custom status line, so 5-hour/weekly usage can't be shown. Activity and context still work."),
                    title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ClaudeSettings.Apply(enable: false, code.Port);
                code.ClaudeHooks = false;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.LogWarning(ex, "Couldn't update Claude Code settings");
            MessageBox.Show("Couldn't update Claude Code settings:\n" + ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Config.SaveSoon();
        _settings?.Reload();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RunCleanup();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Config?.Dispose();
        _tray?.Dispose();
        if (_ownsMutex) _single?.ReleaseMutex();
        base.OnExit(e);
    }
}
