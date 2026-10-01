using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Native;

namespace KawaiiIsland;

public partial class IslandWindow : Window
{
    private const double ShadowMargin = 40;
    private readonly ConfigService _config;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public IslandWindow(ConfigService config)
    {
        _config = config;
        InitializeComponent();
        ApplyConfig();
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Win32.MakeToolWindow(new WindowInteropHelper(this).Handle); // no Alt+Tab, never steals focus
        PositionTopCenter();
    }

    private void ApplyConfig()
    {
        var w = _config.Current.Window;
        Width = w.ExpandedWidth + ShadowMargin;
        Height = w.ExpandedHeight + ShadowMargin;
        Pill.Width = w.CollapsedWidth;
        Pill.Height = w.CollapsedHeight;
        Pill.CornerRadius = new CornerRadius(w.CollapsedHeight / 2);
        Opacity = w.Opacity;
        Mascot.Source = MascotImage(_config.Current.Appearance.Mascot, "idle");
    }

    /// <summary>Generated vector art from Assets/Mascots.xaml; unknown skins fall back to the default.</summary>
    internal static ImageSource MascotImage(string skin, string expression) =>
        (ImageSource)(Application.Current.TryFindResource($"Mascot.{skin}.{expression}")
                      ?? Application.Current.FindResource($"Mascot.{AppConfig.DefaultMascot}.{expression}"));

    // ponytail: primary-monitor top-centre only; AppBar docking + monitor/edge/alignment come in Phases 3–5.
    private void PositionTopCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top;
    }

    private void UpdateClock() => Clock.Text = DateTime.Now.ToString("t");

    public void ToggleVisible()
    {
        if (IsVisible) Hide(); else ShowIsland();
    }

    public void ShowIsland()
    {
        Show();
        Topmost = false; Topmost = true; // re-assert z-order above other topmost windows
    }
}
