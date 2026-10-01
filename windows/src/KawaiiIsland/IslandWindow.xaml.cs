using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using KawaiiIsland.Controls;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Native;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

public partial class IslandWindow : Window
{
    private const double ShadowMargin = 40, Gap = 6, SnapThreshold = 24;

    /// <summary>Animatable stand-in for Pill.CornerRadius (CornerRadius itself has no animation type).
    /// Defaults to 0 so the first real value always fires the callback.</summary>
    public static readonly DependencyProperty PillRadiusProperty = DependencyProperty.Register(
        nameof(PillRadius), typeof(double), typeof(IslandWindow),
        new PropertyMetadata(0.0, (d, e) =>
        {
            var w = (IslandWindow)d;
            w.Pill.CornerRadius = new CornerRadius((double)e.NewValue);
            w.SizeOutline();
        }));

    public double PillRadius { get => (double)GetValue(PillRadiusProperty); set => SetValue(PillRadiusProperty, value); }

    private readonly ConfigService _config;
    private readonly AppBarService _appBar = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _autoCollapse = new();
    private readonly DispatcherTimer _moodTimer = new();
    private readonly ClickBurst _burst = new();
    private readonly DispatcherTimer _applyTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private nint _hwnd;
    private bool _expanded;
    private string? _mood; // null = normal; "wow" (hover), "annoyed" (poked), "dizzy" (3 quick pokes)
    private string _baseExpression = "idle"; // what the face returns to when no mood is active (Code mode changes it)
    private bool _compact; // 280x40 "compact-active" size (spec §2) while something is going on

    private WindowConfig Win => _config.Current.Window;
    private bool HasMascot => _config.Current.Appearance.Mascot != AppConfig.NoMascot;
    private MascotControl[] Mascots => [MascotSmall, MascotLarge, MascotTiny];

    public IslandWindow(ConfigService config)
    {
        _config = config;
        _feed = new(() => _config.Current.Modules.Notifications.Muted);
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
        Pill.MouseRightButtonUp += (_, e) =>
        {
            if (IsInTile(e.OriginalSource as DependencyObject)) return; // app tiles have their own menu
            e.Handled = true;
            var menu = ((App)Application.Current).IslandMenu();
            menu.PlacementTarget = Pill;
            menu.Closed += (_, _) => HideSoon();
            _menu = menu;
            menu.IsOpen = true;
        };
        foreach (var m in Mascots) m.MouseLeftButtonUp += OnMascotPoked;
        _applyTimer.Tick += (_, _) => { _applyTimer.Stop(); ApplySettingsNow(); };

        _appBar.Docked += OnDocked;
        // Hidden (Settings open, tray "hide"): give the reserved strip back; showing again re-reserves it.
        IsVisibleChanged += (_, _) => { if (_hwnd == 0) return; if (IsVisible) PlaceIsland(); else _appBar.Undock(); };
        App.Cleanup += _appBar.Dispose; // crash or exit: never leave a reserved strip behind
        InitCodeMode();
        InitAutoHide();
        InitMusic();
        InitViews();
        InitAlerts();
        InitMail();
        InitShortcuts();
        InitWidgets();
        InitLive();
        _sleepTimer.Tick += (_, _) => UpdateSleepy();
        _sleepTimer.Start();
    }

    // ---------------- global hotkey (spec §7) + sleepy mascot ----------------

    private const int HotkeyId = 0x4B49; // "KI"
    private readonly DispatcherTimer _sleepTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>(Re)registers Behavior.Hotkey. Returns why it couldn't (bad text, taken by another app) or null.</summary>
    public string? HotkeyProblem { get; private set; }

    public string? ApplyHotkey() => HotkeyProblem = RegisterHotkey();

    private string? RegisterHotkey()
    {
        if (_hwnd == 0) return null;
        Win32.UnregisterHotKey(_hwnd, HotkeyId);
        string text = _config.Current.Behavior.Hotkey;
        if (text.Length == 0) return null;
        if (!HotkeyText.TryParse(text, out uint mods, out uint vk)) return $"\"{text}\" isn't a valid shortcut (e.g. Ctrl+Alt+I).";
        return Win32.RegisterHotKey(_hwnd, HotkeyId, mods | Win32.MOD_NOREPEAT, vk) ? null : $"{text} is already used by another app.";
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY || wParam != HotkeyId) return 0;
        handled = true;
        if (!IsVisible) { ShowIsland(); SetExpanded(true); }
        else ToggleExpanded();
        return 0;
    }

    /// <summary>Away for 5+ minutes with nothing going on: the mascot dozes off (spec §8 "sleepy"); any input wakes it.</summary>
    private void UpdateSleepy()
    {
        bool away = Win32.IdleTime() >= TimeSpan.FromMinutes(5);
        if (away && _baseExpression == "idle") SetBaseExpression("sleepy");
        else if (!away && _baseExpression == "sleepy") SetBaseExpression(IdleExpression);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.MakeToolWindow(_hwnd); // no Alt+Tab, never steals focus
        HwndSource.FromHwnd(_hwnd)?.AddHook(OnWindowMessage);
        if (ApplyHotkey() is { } problem) App.Log.LogWarning("Hotkey: {Problem}", problem);
        PlaceIsland();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; // static event: unsubscribe or leak
        Win32.UnregisterHotKey(_hwnd, HotkeyId);
        _appBar.Dispose();
        StopCodeMode();
        _mediaService?.Dispose();
        StopNotifications();
        _mail?.Dispose();
        StopLive();
        base.OnClosed(e);
    }

    private void ApplyConfig()
    {
        Width = Win.ExpandedWidth + ShadowMargin;
        Height = Win.ExpandedHeight + ShadowMargin;
        var (w, h) = RestSize;
        Pill.Width = w;
        Pill.Height = h;
        PillRadius = h / 2;
        ExpandedPanel.Width = Win.ExpandedWidth;
        ExpandedPanel.Height = double.NaN; // height follows content (see ExpandedHeightNow)
        Opacity = Win.Opacity;
        foreach (var m in Mascots) m.Skin = _config.Current.Appearance.Mascot;
        PeekMascot.Skin = _config.Current.Appearance.Mascot;
        MascotLarge.Visibility = PeekMascot.Visibility = Vis(HasMascot);
        MascotTiny.Width = HasMascot ? 22 : 0;
        Outline.Visibility = Win.Locked ? Visibility.Collapsed : Visibility.Visible;
        ApplyAnchor();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        BigClock.Text = now.ToString("t");
        if (CodeLinePanel.Visibility != Visibility.Visible && _dropText is null) Clock.Text = BigClock.Text; // Code mode shows usage there
        DateText.Text = now.ToString("dddd, MMMM d");
        SmallClock.Text = BigClock.Text;
        TickMusic();
        TickAlerts();
        TickCodeFooter();
    }

    // ---------------- placement (physical pixels via Win32; see Services/Native) ----------------

    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    private Edge DockEdge => Placement.ParseEdge(Win.DockEdge);

    /// <summary>
    /// The pill hugs the docked edge so it expands away from it (bottom dock grows upward, right dock grows left…).
    /// Free-floating islands grow down from their top-centre.
    /// </summary>
    private (HorizontalAlignment H, VerticalAlignment V) Anchor()
    {
        if (!Win.AppBarEnabled) return (HorizontalAlignment.Center, VerticalAlignment.Top);
        // Alignment values are shared by all edges: Left≡Top (start), Right≡Bottom (end), anything else = centre.
        string a = Win.Alignment.ToLowerInvariant();
        bool start = a is "left" or "top", end = a is "right" or "bottom";
        var h = DockEdge switch
        {
            Edge.Left => HorizontalAlignment.Left,
            Edge.Right => HorizontalAlignment.Right,
            _ => start ? HorizontalAlignment.Left : end ? HorizontalAlignment.Right : HorizontalAlignment.Center,
        };
        var v = DockEdge switch
        {
            Edge.Top => VerticalAlignment.Top,
            Edge.Bottom => VerticalAlignment.Bottom,
            _ => start ? VerticalAlignment.Top : end ? VerticalAlignment.Bottom : VerticalAlignment.Center,
        };
        return (h, v);
    }

    private void ApplyAnchor()
    {
        var (h, v) = Anchor();
        Pill.HorizontalAlignment = Outline.HorizontalAlignment = h;
        Pill.VerticalAlignment = Outline.VerticalAlignment = v;
        Pill.Margin = new Thickness(h == HorizontalAlignment.Left ? Gap : 0, v == VerticalAlignment.Top ? Gap : 0,
                                    h == HorizontalAlignment.Right ? Gap : 0, v == VerticalAlignment.Bottom ? Gap : 0);
        Outline.Margin = new Thickness(Pill.Margin.Left - 5, Pill.Margin.Top - 5, Pill.Margin.Right - 5, Pill.Margin.Bottom - 5);
        Pill.RenderTransformOrigin = new Point(h == HorizontalAlignment.Left ? 0 : h == HorizontalAlignment.Right ? 1 : 0.5,
                                               v == VerticalAlignment.Top ? 0 : v == VerticalAlignment.Bottom ? 1 : 0.5);
        ExpandedPanel.VerticalAlignment = v == VerticalAlignment.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        AnchorEdgeParts();
    }

    /// <summary>Collapsed pill footprint (physical px) inside a window placed at <paramref name="window"/>.</summary>
    private Rect PillRect(Rect window, double scale)
    {
        var (h, v) = Anchor();
        var size = new Size(RestSize.W * scale, RestSize.H * scale);
        var off = Placement.PillOffset(window.Size, size, h, v, Gap * scale);
        return new Rect(window.X + off.X, window.Y + off.Y, size.Width, size.Height);
    }

    /// <summary>Window top-left that puts the collapsed pill's top-left at <paramref name="pill"/>.</summary>
    private Point WindowFor(Point pill, Size window, double scale)
    {
        var (h, v) = Anchor();
        var off = Placement.PillOffset(window, new Size(RestSize.W * scale, RestSize.H * scale), h, v, Gap * scale);
        return new Point(pill.X - off.X, pill.Y - off.Y);
    }

    /// <summary>
    /// AppBar mode: reserve a strip on the saved/primary monitor (OnDocked then places the pill inside it).
    /// Free-floating: release any reservation and restore the saved per-monitor offset, clamped on-screen.
    /// ponytail: assumes the window's current DPI matches the target monitor; mixed-DPI moves settle after WM_DPICHANGED.
    /// </summary>
    private void PlaceIsland()
    {
        var mon = Win.MonitorId.Length > 0 ? Monitors.ByDevice(Win.MonitorId) : Monitors.Primary();
        if (Win.AppBarEnabled)
        {
            bool horizontal = DockEdge is Edge.Top or Edge.Bottom;
            double thickness = ((horizontal ? RestSize.H : RestSize.W) + 2 * Gap) * mon.Scale;
            if (_autoHiding) // auto-hidden: same spot on the bare monitor edge, nothing reserved
            {
                _appBar.Undock();
                OnDocked(Placement.Strip(mon.Bounds, DockEdge, thickness), mon);
                return;
            }
            _appBar.Dock(mon, DockEdge, thickness);
            return;
        }

        _appBar.Undock();
        var window = Monitors.WindowRect(_hwnd);
        if (Win.MonitorId.Length == 0) // never placed: top-centre
        {
            Monitors.MoveWindow(_hwnd, mon.Work.X + (mon.Work.Width - window.Width) / 2, mon.Work.Y);
            return;
        }
        var saved = new Rect(mon.Work.X + Win.X * mon.Scale, mon.Work.Y + Win.Y * mon.Scale,
                             RestSize.W * mon.Scale, RestSize.H * mon.Scale);
        var pos = WindowFor(Placement.ClampInto(saved, mon.Work), window.Size, mon.Scale);
        Monitors.MoveWindow(_hwnd, pos.X, pos.Y);
    }

    /// <summary>Strip reserved (or moved by the shell): put the pill inside it per edge + alignment.</summary>
    private void OnDocked(Rect strip, MonitorInfo mon)
    {
        var pillSize = new Size(RestSize.W * mon.Scale, RestSize.H * mon.Scale);
        var pill = Placement.PillInStrip(strip, DockEdge, Win.Alignment, pillSize, Gap * mon.Scale);
        var pos = WindowFor(pill, Monitors.WindowRect(_hwnd).Size, mon.Scale);
        Monitors.MoveWindow(_hwnd, pos.X, pos.Y);
        Win.MonitorId = mon.Device;
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
        double s = Dpi;
        var pill = PillRect(window, s);
        var mon = Monitors.For(pill);
        var target = Placement.Snap(pill, mon.Work, SnapThreshold * mon.Scale, Gap * mon.Scale);
        AnimateWindow(window.TopLeft, WindowFor(target, window.Size, s));
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
    public bool AppBarEnabled => Win.AppBarEnabled;
    public int AutoCollapseSeconds => _config.Current.Behavior.AutoCollapseSeconds;

    /// <summary>Unlocking switches to free-floating (AppBar off) where the island is now; locking keeps it there.</summary>
    public void SetLocked(bool locked)
    {
        if (!locked && Win.AppBarEnabled) SetAppBar(false);
        Win.Locked = locked;
        Outline.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _config.SaveSoon();
    }

    /// <summary>On: lock and reserve the strip on the island's current monitor. Off: release it and float in place.</summary>
    public void SetAppBar(bool on)
    {
        if (on == Win.AppBarEnabled) return;
        var pill = PillRect(Monitors.WindowRect(_hwnd), Dpi);   // measured with the current anchor
        var mon = Monitors.For(pill);
        Win.AppBarEnabled = on;
        if (on) { Win.Locked = true; Outline.Visibility = Visibility.Collapsed; Win.MonitorId = mon.Device; }
        else SavePosition(mon, pill.TopLeft);
        ApplyAnchor();
        PlaceIsland();
        _config.SaveSoon();
    }

    /// <summary>0 = never auto-collapse.</summary>
    public void SetAutoCollapse(int seconds)
    {
        _config.Current.Behavior.AutoCollapseSeconds = seconds;
        _config.SaveSoon();
        ArmAutoCollapse();
    }

    private void SizeOutline()
    {
        Outline.Width = Pill.ActualWidth + 10;
        Outline.Height = Pill.ActualHeight + 10;
        Outline.RadiusX = Outline.RadiusY = PillRadius + 5;
    }

    // ---------------- expand / collapse ----------------

    public void SetExpanded(bool expand)
    {
        if (expand && _hidden) SlidePill(hide: false); // auto-hidden: opening reveals it (peek click, Code mode)
        if (_expanded == expand) return;
        _expanded = expand;

        if (expand) AnimatePill(Win.ExpandedWidth, ExpandedHeightNow(), Win.CornerRadius);
        else { var (w, h) = CollapsedSize(); AnimatePill(w, h, h / 2); }

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
            HideSoon();
            Fade(ExpandedPanel, 0, 120, () => { if (!_expanded) ExpandedPanel.Visibility = Visibility.Collapsed; });
            Fade(CollapsedPanel, 1, 180, delayMs: 120);
        }
    }

    public void ToggleExpanded() => SetExpanded(!_expanded);
    public bool IsExpanded => _expanded;

    /// <summary>Settings changed: re-apply sizes/skin/anchor and re-dock, debounced 150 ms (spec §5) so slider drags stay smooth.</summary>
    public void ApplySettingsSoon()
    {
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void ApplySettingsNow()
    {
        // Drop held animation values so the new sizes take effect, and settle in the resting state.
        Pill.BeginAnimation(WidthProperty, null);
        Pill.BeginAnimation(HeightProperty, null);
        BeginAnimation(PillRadiusProperty, null);
        CollapsedPanel.BeginAnimation(OpacityProperty, null);
        ExpandedPanel.BeginAnimation(OpacityProperty, null);
        _expanded = false;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapsedPanel.Opacity = 1;

        ApplyConfig();
        RenderCompact(); // side dock ⇄ top/bottom changes what the resting pill shows
        Pill.BeginAnimation(WidthProperty, null);
        Pill.BeginAnimation(HeightProperty, null);
        BeginAnimation(PillRadiusProperty, null);
        var (w, h) = CollapsedSize();
        Pill.Width = w; Pill.Height = h; PillRadius = h / 2;
        PlaceIsland();
    }

    /// <summary>Width, height and corner radius move together, smooth ease-out with no overshoot (400 ms).</summary>
    private void AnimatePill(double width, double height, double radius)
    {
        var ease = Motion.Smooth;
        var d = Motion.Ms(400);
        Pill.BeginAnimation(WidthProperty, new DoubleAnimation(width, d) { EasingFunction = ease });
        Pill.BeginAnimation(HeightProperty, new DoubleAnimation(height, d) { EasingFunction = ease });
        BeginAnimation(PillRadiusProperty, new DoubleAnimation(radius, d) { EasingFunction = ease });
    }

    /// <summary>Design guidelines "dynamic height": the expanded island wraps its content (84 px minimum),
    /// up to the Expanded height from Settings.</summary>
    private double ExpandedHeightNow()
    {
        ExpandedItems.Measure(new Size(Win.ExpandedWidth, double.PositiveInfinity));
        return Math.Clamp(Math.Ceiling(ExpandedItems.DesiredSize.Height), 84, Win.ExpandedHeight);
    }

    /// <summary>Content changed while open (new mail, tab switch…): grow or shrink to fit.</summary>
    private void FitExpanded()
    {
        if (!_expanded) return;
        double h = ExpandedHeightNow();
        if (Math.Abs(Pill.Height - h) > 1) AnimatePill(Win.ExpandedWidth, h, Win.CornerRadius);
    }

    private (double W, double H) CollapsedSize() => _compact ? (280, 40) : RestSize;

    /// <summary>Docked to the left/right edge: the resting island is a minimal 44 px circle (design guidelines'
    /// "minimal" state), so the reserved side strip is 56 px instead of the full pill width.</summary>
    private bool SideDocked => Win.AppBarEnabled && DockEdge is Edge.Left or Edge.Right;

    private (double W, double H) RestSize => SideDocked ? (MinimalSize, MinimalSize)
        : (Math.Max(120, Win.CollapsedWidth + PillWidgetWidth), Win.CollapsedHeight);
    private const double MinimalSize = 44;

    /// <summary>Switches the resting size between collapsed (180x36) and compact-active (280x40).</summary>
    private void SetCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        if (!_expanded) { var (w, h) = CollapsedSize(); AnimatePill(w, h, h / 2); }
    }

    private void ArmAutoCollapse()
    {
        _autoCollapse.Stop();
        int seconds = _config.Current.Behavior.AutoCollapseSeconds;
        if (!_expanded || seconds == 0 || Pill.IsMouseOver || _approval is not null) return;
        _autoCollapse.Interval = TimeSpan.FromSeconds(seconds);
        _autoCollapse.Start();
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
        Shadow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(over ? GlowColor() : Colors.Black, d));
        Shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(over ? 32 : 24, d));
        Shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(over ? 0.6 : 0.45, d));

        if (over)
        {
            _autoCollapse.Stop();
            if (_mood != null) return;                                   // don't interrupt annoyed/dizzy
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
        bool burst = _burst.Register(Environment.TickCount64);
        if (!burst && _mood == "dizzy") return; // stay dizzy; the running timer still brings it back to normal
        _moodTimer.Stop();
        if (burst)
        {
            SetMood("dizzy");
            foreach (var m in Mascots) m.Wobble(true);
            _moodTimer.Interval = TimeSpan.FromSeconds(3);
        }
        else
        {
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
        foreach (var m in Mascots) m.Expression = mood ?? _baseExpression;
    }

    private void SetBaseExpression(string expression)
    {
        _baseExpression = expression;
        if (_mood is null) SetMood(null);
    }

    private static bool IsInTile(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is Button { Tag: ShortcutItem }) return true;
        return false;
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
