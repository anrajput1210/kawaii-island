using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using KawaiiIsland.Controls;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Native;

namespace KawaiiIsland;

public partial class IslandWindow : Window
{
    private const double ShadowMargin = 40;

    /// <summary>Animatable stand-in for Pill.CornerRadius (CornerRadius itself has no animation type).
    /// Defaults to 0 so the first real value always fires the callback.</summary>
    public static readonly DependencyProperty PillRadiusProperty = DependencyProperty.Register(
        nameof(PillRadius), typeof(double), typeof(IslandWindow),
        new PropertyMetadata(0.0, (d, e) => ((IslandWindow)d).Pill.CornerRadius = new CornerRadius((double)e.NewValue)));

    public double PillRadius { get => (double)GetValue(PillRadiusProperty); set => SetValue(PillRadiusProperty, value); }

    private readonly ConfigService _config;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _autoCollapse = new();
    private readonly DispatcherTimer _moodTimer = new();
    private readonly ClickBurst _burst = new();
    private bool _expanded;
    private string? _mood; // null = normal; "wow" (hover), "annoyed" (poked), "dizzy" (3 quick pokes)

    private WindowConfig Win => _config.Current.Window;
    private MascotControl[] Mascots => [MascotSmall, MascotLarge];

    public IslandWindow(ConfigService config)
    {
        _config = config;
        InitializeComponent();
        ApplyConfig();

        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        _autoCollapse.Tick += (_, _) => { _autoCollapse.Stop(); if (!Pill.IsMouseOver) SetExpanded(false); };
        _moodTimer.Tick += (_, _) => EndMood();

        Pill.MouseEnter += (_, _) => OnHover(true);
        Pill.MouseLeave += (_, _) => OnHover(false);
        Pill.MouseLeftButtonDown += OnPillPressed;
        Pill.MouseLeftButtonUp += (_, _) => { if (Win.Locked) SetExpanded(!_expanded); };
        Pill.SizeChanged += (_, _) => SizeOutline();
        foreach (var m in Mascots) m.MouseLeftButtonUp += OnMascotPoked;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.MakeToolWindow(_hwnd); // no Alt+Tab, never steals focus
        PlaceIsland();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; // static event: unsubscribe or leak
        base.OnClosed(e);
    }

    private void ApplyConfig()
    {
        Width = Win.ExpandedWidth + ShadowMargin;
        Height = Win.ExpandedHeight + ShadowMargin;
        Pill.Width = Win.CollapsedWidth;
        Pill.Height = Win.CollapsedHeight;
        PillRadius = Win.CollapsedHeight / 2;
        ExpandedPanel.Width = Win.ExpandedWidth;
        ExpandedPanel.Height = Win.ExpandedHeight;
        Opacity = Win.Opacity;
        _autoCollapse.Interval = TimeSpan.FromSeconds(_config.Current.Behavior.AutoCollapseSeconds);
        foreach (var m in Mascots) m.Skin = _config.Current.Appearance.Mascot;
        Outline.Visibility = Win.Locked ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------- placement (physical pixels via Win32; see Services/Native/Monitors.cs) ----------------

    private const double PillTop = 6, SnapThreshold = 24, SnapGap = 6;
    private nint _hwnd;
    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Collapsed pill footprint inside a window rect (physical px).</summary>
    private Rect PillRect(Rect window)
    {
        double s = Dpi, w = Win.CollapsedWidth * s, h = Win.CollapsedHeight * s;
        return new Rect(window.X + (window.Width - w) / 2, window.Y + PillTop * s, w, h);
    }

    /// <summary>Window top-left that puts the pill's top-left at <paramref name="pill"/>.</summary>
    private Point WindowFor(Point pill, Rect window, double scale) =>
        new(pill.X - (window.Width - Win.CollapsedWidth * scale) / 2, pill.Y - PillTop * scale);

    /// <summary>
    /// Docked (or never placed): top-centre of the saved/primary monitor — Phase 4 turns this into a real
    /// AppBar reservation. Free-floating: the saved per-monitor offset, clamped on-screen; missing monitor → primary.
    /// ponytail: assumes the window's current DPI matches the target monitor; mixed-DPI moves settle after WM_DPICHANGED.
    /// </summary>
    private void PlaceIsland()
    {
        var window = Monitors.WindowRect(_hwnd);
        var mon = Win.MonitorId.Length > 0 ? Monitors.ByDevice(Win.MonitorId) : Monitors.Primary();
        if (Win.AppBarEnabled || Win.MonitorId.Length == 0)
        {
            Monitors.MoveWindow(_hwnd, mon.Work.X + (mon.Work.Width - window.Width) / 2, mon.Work.Y);
            return;
        }
        var saved = new Rect(mon.Work.X + Win.X * mon.Scale, mon.Work.Y + Win.Y * mon.Scale,
                             Win.CollapsedWidth * mon.Scale, Win.CollapsedHeight * mon.Scale);
        var pos = WindowFor(Placement.ClampInto(saved, mon.Work), window, mon.Scale);
        Monitors.MoveWindow(_hwnd, pos.X, pos.Y);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(PlaceIsland);

    /// <summary>Unlocked: press-and-drag moves the island (DragMove); a press without movement is still a click.</summary>
    private void OnPillPressed(object sender, MouseButtonEventArgs e)
    {
        if (Win.Locked || e.OriginalSource is MascotControl) return;
        e.Handled = true;
        var before = Monitors.WindowRect(_hwnd);
        if (_expanded) SetExpanded(false);
        DragMove(); // modal until the button is released
        var after = Monitors.WindowRect(_hwnd);
        if (Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y) < 4) { SetExpanded(!_expanded); return; }
        SnapAndSave(after);
    }

    private void SnapAndSave(Rect window)
    {
        var pill = PillRect(window);
        var mon = Monitors.For(pill);
        var target = Placement.Snap(pill, mon.Work, SnapThreshold * mon.Scale, SnapGap * mon.Scale);
        AnimateWindow(window.TopLeft, WindowFor(target, window, Dpi));
        SavePosition(mon, target);
    }

    private void SavePosition(MonitorInfo mon, Point pill)
    {
        Win.MonitorId = mon.Device;
        Win.X = Math.Round((pill.X - mon.Work.X) / mon.Scale, 1);
        Win.Y = Math.Round((pill.Y - mon.Work.Y) / mon.Scale, 1);
        _config.SaveSoon();
    }

    /// <summary>200 ms ease-out slide between two physical positions (instant if animations are off).</summary>
    private void AnimateWindow(Point from, Point to)
    {
        if (!Motion.Enabled) { Monitors.MoveWindow(_hwnd, to.X, to.Y); return; }
        long start = Environment.TickCount64;
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - start) / 200.0), k = 1 - Math.Pow(1 - t, 3);
            Monitors.MoveWindow(_hwnd, from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * k);
            if (t >= 1) CompositionTarget.Rendering -= tick;
        };
        CompositionTarget.Rendering += tick;
    }

    public bool Locked => Win.Locked;

    /// <summary>Unlocking switches to free-floating (AppBar off) and remembers the current spot; locking keeps it.</summary>
    public void SetLocked(bool locked)
    {
        Win.Locked = locked;
        if (!locked)
        {
            Win.AppBarEnabled = false;
            var pill = PillRect(Monitors.WindowRect(_hwnd));
            SavePosition(Monitors.For(pill), pill.TopLeft);
        }
        Outline.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _config.SaveSoon();
    }

    private void SizeOutline()
    {
        Outline.Width = Pill.ActualWidth + 10;
        Outline.Height = Pill.ActualHeight + 10;
        Outline.RadiusX = Outline.RadiusY = PillRadius + 5;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock.Text = BigClock.Text = now.ToString("t");
        DateText.Text = now.ToString("dddd, MMMM d");
    }

    // ---------------- expand / collapse ----------------

    public void SetExpanded(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        var d = Motion.Ms(320);
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(expand ? Win.ExpandedWidth : Win.CollapsedWidth, d) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(expand ? Win.ExpandedHeight : Win.CollapsedHeight, d) { EasingFunction = ease });
        BeginAnimation(PillRadiusProperty, new DoubleAnimation(expand ? Win.CornerRadius : Win.CollapsedHeight / 2, d) { EasingFunction = ease });

        if (expand)
        {
            Fade(CollapsedPanel, 0, 120);
            ExpandedPanel.Visibility = Visibility.Visible;
            ExpandedPanel.BeginAnimation(OpacityProperty, null);
            ExpandedPanel.Opacity = 1;
            StaggerIn(ExpandedItems);
            ArmAutoCollapse();
        }
        else
        {
            _autoCollapse.Stop();
            Fade(ExpandedPanel, 0, 120, () => { if (!_expanded) ExpandedPanel.Visibility = Visibility.Collapsed; });
            Fade(CollapsedPanel, 1, 180, delayMs: 120);
        }
    }

    public void ToggleExpanded() => SetExpanded(!_expanded);

    private void ArmAutoCollapse()
    {
        _autoCollapse.Stop();
        if (_expanded && !Pill.IsMouseOver) _autoCollapse.Start();
    }

    /// <summary>Children fade + slide up 6 px, one after another (60 ms stagger).</summary>
    private static void StaggerIn(Panel panel)
    {
        int i = 0;
        foreach (UIElement child in panel.Children)
        {
            var slide = new TranslateTransform(0, 6);
            child.RenderTransform = slide;
            child.Opacity = 0;
            var begin = Motion.Delay(80 + i++ * 60);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Ms(180)) { BeginTime = begin, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, Motion.Ms(180)) { BeginTime = begin, EasingFunction = ease });
        }
    }

    private static void Fade(UIElement el, double to, int ms, Action? done = null, int delayMs = 0)
    {
        var a = new DoubleAnimation(to, Motion.Ms(ms)) { BeginTime = Motion.Delay(delayMs) };
        if (done != null) a.Completed += (_, _) => done();
        el.BeginAnimation(OpacityProperty, a);
    }

    // ---------------- hover ----------------

    private async void OnHover(bool over)
    {
        var d = Motion.Ms(150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(over ? 1.04 : 1, d) { EasingFunction = ease });
        HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(over ? 1.04 : 1, d) { EasingFunction = ease });
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, new ColorAnimation(over ? Accent() : Colors.Black, d));
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(over ? 32 : 24, d));
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(over ? 0.6 : 0.45, d));

        if (over)
        {
            _autoCollapse.Stop();
            if (_mood != null) return;                        // don't interrupt annoyed/dizzy
            await Task.WhenAll(Mascots.Select(m => m.BlinkOnceAsync())); // blink...
            if (Pill.IsMouseOver && _mood == null) SetMood("wow");      // ...then the eyes grow
        }
        else
        {
            if (_mood == "wow") SetMood(null);
            ArmAutoCollapse();
        }
    }

    private Color Accent()
    {
        try { return (Color)ColorConverter.ConvertFromString(_config.Current.Appearance.Accent); }
        catch (FormatException) { return Color.FromRgb(0xFF, 0x8F, 0xB1); }
    }

    // ---------------- mascot reactions ----------------

    private void OnMascotPoked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // poking the mascot doesn't expand/collapse the island
        _moodTimer.Stop();
        if (_burst.Register(Environment.TickCount64))
        {
            SetMood("dizzy");
            foreach (var m in Mascots) m.Wobble(true);
            _moodTimer.Interval = TimeSpan.FromSeconds(3);
        }
        else
        {
            if (_mood == "dizzy") return; // stay dizzy until it wears off
            SetMood("annoyed");
            foreach (var m in Mascots) m.Squish();
            _moodTimer.Interval = TimeSpan.FromSeconds(1.2);
        }
        _moodTimer.Start();
    }

    private void EndMood()
    {
        _moodTimer.Stop();
        foreach (var m in Mascots) m.Wobble(false);
        SetMood(Pill.IsMouseOver ? "wow" : null);
    }

    private void SetMood(string? mood)
    {
        _mood = mood;
        foreach (var m in Mascots) m.Expression = mood ?? "idle";
    }

    // ---------------- visibility ----------------

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
