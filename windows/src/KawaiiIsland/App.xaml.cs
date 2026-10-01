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

        AppDomain.CurrentDomain.UnhandledException += (_, _) => RunCleanup();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunCleanup();
        DispatcherUnhandledException += (_, _) => RunCleanup();

        base.OnStartup(e);
        Config = new ConfigService();
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _island = new IslandWindow(Config);
        _island.Show();
        _tray = BuildTray();

        ThreadPool.RegisterWaitForSingleObject(_wake, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);
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
        SetBrush("Accent", a.Accent, fallback: "#FF8FB1");
    }

    private void SetBrush(string key, string hex, string fallback = "#000000")
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { color = (Color)ColorConverter.ConvertFromString(fallback); }
        Resources[key] = new SolidColorBrush(color);
    }

    private static bool WindowsUsesLightTheme() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int v && v == 1;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && Config.Current.Appearance.Theme == "auto")
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
        string[] mascots = [.. AppConfig.Mascots, .. File.Exists(AppConfig.CustomMascotPath) ? [AppConfig.CustomMascot] : Array.Empty<string>(), AppConfig.NoMascot];
        menu.Items.Add(Choice("Mascot", mascots.Select(m => (MascotName(m), m)).ToArray(),
                              Config.Current.Appearance.Mascot, v => { Config.Current.Appearance.Mascot = v; SettingsChanged(); }));
        menu.Items.Add(Choice("Collapse after", CollapseChoices, island.AutoCollapseSeconds.ToString(), v => island.SetAutoCollapse(int.Parse(v))));
        menu.Items.Add(Check("Code mode (Claude Code)", island.CodeMode, SetCodeMode));
        menu.Items.Add(Check("Do not disturb", island.Dnd, on => { island.SetDnd(on); _settings?.Reload(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings…", ShowSettings));
        menu.Items.Add(Item("Quit", Shutdown));
    }

    internal static readonly (string Label, string Value)[] CollapseChoices =
        [("4 seconds", "4"), ("10 seconds", "10"), ("15 seconds", "15"), ("30 seconds", "30"), ("Never", "0")];

    /// <summary>Values stay Left/Center/Right; side docks just label them Top/Middle/Bottom.</summary>
    internal static (string Label, string Value)[] AlignmentChoices(bool vertical) => vertical
        ? [("Top", "Left"), ("Middle", "Center"), ("Bottom", "Right")]
        : [("Left", "Left"), ("Center", "Center"), ("Right", "Right")];

    internal static (string Label, string Value)[] MonitorChoices() =>
        Monitors.All().Select((m, i) => ($"Display {i + 1} · {m.Bounds.Width}×{m.Bounds.Height}{(m.Primary ? " (primary)" : "")}", m.Device)).ToArray();

    internal static string MascotName(string key) => key switch
    {
        "kiko" => "Kiko · anime girl", "miso" => "Miso · cat", "bun" => "Bun · bunny", "bolt" => "Bolt · robot", "ribbit" => "Ribbit · frog",
        AppConfig.NoMascot => "No mascot", AppConfig.CustomMascot => "Your own picture", _ => key,
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

    internal void ShowSettings()
    {
        if (_settings is { IsLoaded: true }) { _settings.Activate(); return; }
        _settings = new SettingsWindow(this);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    // ---------------- Code mode ----------------

    /// <summary>
    /// Turning Code mode on edits ~/.claude/settings.json, so it is always an explicit, confirmed user action.
    /// Off removes exactly our entries again.
    /// </summary>
    internal void SetCodeMode(bool on)
    {
        var code = Config.Current.Modules.Code;
        const string title = "Kawaii Island · Code mode";
        if (on)
        {
            bool first = !code.Consented;
            var answer = !first ? MessageBoxResult.Yes : MessageBox.Show(
                "Code mode shows your Claude Code sessions on the island: what Claude is doing, context used, " +
                "and your 5-hour and weekly plan usage.\n\n" +
                $"It adds Kawaii Island hooks (and a status line, if you don't already have one) to:\n{ClaudeSettings.SettingsPath}\n\n" +
                "A backup is saved next to it. Nothing leaves this PC. Turning Code mode off removes them again.\n\nTurn on Code mode?",
                title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            if (_island!.StartCodeMode() is { } error) { MessageBox.Show(error, title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            try
            {
                bool statusLine = ClaudeSettings.Apply(enable: true, code.Port);
                code.Enabled = code.Consented = true;
                Config.SaveSoon();
                if (first) MessageBox.Show(
                    "Code mode is on. Restart any open Claude Code sessions: hooks load when a session starts." +
                    (statusLine ? "" : "\n\nYou already use a custom status line, so 5-hour/weekly usage can't be shown. Activity and context still work."),
                    title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _island.StopCodeMode();
                MessageBox.Show("Couldn't update Claude Code settings:\n" + ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            try { ClaudeSettings.Apply(enable: false, code.Port); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                MessageBox.Show("Couldn't remove the hooks from Claude Code settings:\n" + ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            _island!.StopCodeMode();
            code.Enabled = false;
            Config.SaveSoon();
        }
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
