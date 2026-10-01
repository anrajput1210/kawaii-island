using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using System.IO;
using KawaiiIsland.Services;
using KawaiiIsland.Services.ClaudeCode;

namespace KawaiiIsland;

public partial class App : Application
{
    private Mutex? _single;
    private bool _ownsMutex;
    private EventWaitHandle? _wake;
    private TaskbarIcon? _tray;
    private IslandWindow? _island;

    public ConfigService Config { get; private set; } = null!;

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
            _wake.Set(); // first instance brings itself forward (opens Settings once Phase 5 lands)
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, _) => RunCleanup();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunCleanup();
        DispatcherUnhandledException += (_, _) => RunCleanup();

        base.OnStartup(e);
        Config = new ConfigService();
        _island = new IslandWindow(Config);
        _island.Show();
        _tray = BuildTray();

        ThreadPool.RegisterWaitForSingleObject(_wake, (_, _) => Dispatcher.Invoke(() => _island.ShowIsland()), null, -1, false);
    }

    private TaskbarIcon BuildTray()
    {
        var menu = new ContextMenu();
        var island = _island!;
        menu.Items.Add(MenuItem("Show / hide", island.ToggleVisible));
        menu.Items.Add(new Separator());

        // Checkable items flip IsChecked themselves before Click fires.
        var appBar = new MenuItem { Header = "Reserve workspace (AppBar)", IsCheckable = true };
        appBar.Click += (_, _) => island.SetAppBar(appBar.IsChecked);
        var unlock = new MenuItem { Header = "Unlock to drag", IsCheckable = true };
        unlock.Click += (_, _) => island.SetLocked(!unlock.IsChecked);
        menu.Items.Add(appBar);
        menu.Items.Add(unlock);

        var collapse = new MenuItem { Header = "Collapse after" };
        foreach (var (label, seconds) in new[] { ("4 seconds", 4), ("10 seconds", 10), ("15 seconds", 15), ("30 seconds", 30), ("Never", 0) })
        {
            var item = new MenuItem { Header = label, Tag = seconds, IsCheckable = true };
            item.Click += (_, _) => island.SetAutoCollapse(seconds);
            collapse.Items.Add(item);
        }
        menu.Items.Add(collapse);
        menu.Items.Add(new Separator());
        var code = new MenuItem { Header = "Code mode (Claude Code)", IsCheckable = true };
        code.Click += (_, _) => SetCodeMode(code.IsChecked);
        menu.Items.Add(code);

        menu.Opened += (_, _) => // reflect current state every time the menu opens
        {
            code.IsChecked = island.CodeMode;
            appBar.IsChecked = island.AppBarEnabled;
            unlock.IsChecked = !island.Locked;
            foreach (MenuItem item in collapse.Items) item.IsChecked = (int)item.Tag == island.AutoCollapseSeconds;
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Quit", Shutdown));

        var tray = new TaskbarIcon
        {
            ToolTipText = "Kawaii Island",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.ico")),
            ContextMenu = menu,
        };
        tray.TrayLeftMouseUp += (_, _) => _island!.ToggleVisible();
        return tray;
    }

    /// <summary>
    /// Turning Code mode on edits ~/.claude/settings.json, so it is always an explicit, confirmed user action.
    /// Off removes exactly our entries again.
    /// </summary>
    private void SetCodeMode(bool on)
    {
        var code = Config.Current.Modules.Code;
        const string title = "Kawaii Island · Code mode";
        if (on)
        {
            var answer = MessageBox.Show(
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
                code.Enabled = true;
                Config.SaveSoon();
                MessageBox.Show(
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
    }

    private static MenuItem MenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RunCleanup();
        Config?.Dispose();
        _tray?.Dispose();
        if (_ownsMutex) _single?.ReleaseMutex();
        base.OnExit(e);
    }
}
