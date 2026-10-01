using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using KawaiiIsland.Services;

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

        menu.Opened += (_, _) => // reflect current state every time the menu opens
        {
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
