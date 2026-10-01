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
        menu.Items.Add(MenuItem("Show / hide", () => _island!.ToggleVisible()));
        var unlock = new MenuItem { Header = "Unlock to drag", IsCheckable = true, IsChecked = !_island!.Locked };
        unlock.Click += (_, _) => _island.SetLocked(!unlock.IsChecked); // IsChecked already flipped by the click
        menu.Items.Add(unlock);
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
