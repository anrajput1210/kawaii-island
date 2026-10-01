using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KawaiiIsland.Controls;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Native;
using Microsoft.Win32;

namespace KawaiiIsland;

/// <summary>
/// Apple-style live activities (feature ideas from the MIT-licensed Windhawk mod "Dynamic Island for Windows"):
/// brief alerts for volume, charging/low battery, Bluetooth and Caps/Num Lock; a countdown timer; the mic/camera
/// privacy dot; and the "minimal" detached circle that shows a second activity next to the pill.
/// </summary>
public partial class IslandWindow
{
    private static readonly Color TimerOrange = Color.FromRgb(0xFF, 0x9F, 0x0A), ChargeGreen = Color.FromRgb(0x30, 0xD1, 0x58),
                                  LowRed = Color.FromRgb(0xFF, 0x45, 0x3A), BtBlue = Color.FromRgb(0x4D, 0xA3, 0xFF);

    private sealed record Hud(string Glyph, Color Tint, string Text, string Value, double? Level, DateTimeOffset Until);

    private readonly DispatcherTimer _liveTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SystemVolume _volume = new();
    private readonly CountdownTimer _timer = new();
    private readonly BluetoothWatcher _bt = new();
    private Hud? _hud;
    private (int Percent, bool Muted)? _lastVolume;
    private (int Percent, bool Charging)? _lastPower;
    private bool? _caps, _num;
    private int _privacyCountdown;
    private bool _revealedForHud; // the island was auto-hidden: it slid out just to show an alert

    public bool TimerActive => _timer.Active;
    public bool TimerRunning => _timer.Running;

    private void InitLive()
    {
        _lastVolume = _volume.Read();
        _lastPower = Win32.Battery();
        _caps = Keyboard.IsKeyToggled(Key.CapsLock);
        _num = Keyboard.IsKeyToggled(Key.NumLock);
        _liveTick.Tick += (_, _) => LiveTick();
        _liveTick.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _bt.Changed += (name, connected) => Dispatcher.BeginInvoke(() => OnBluetooth(name, connected));
        _bt.Start();
        TimerPauseButton.Click += (_, _) => ToggleTimer();
        TimerCancelButton.Click += (_, _) => CancelTimer();
        TabTimer.Click += (_, _) => Pick(View.Timer);
        Pill.MouseWheel += OnPillWheel;
        ProgressTrack.Cursor = Cursors.Hand;
        ProgressTrack.MouseLeftButtonUp += OnSeek;
    }

    private void StopLive()
    {
        _liveTick.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged; // static event: unsubscribe or leak
        _bt.Dispose();
    }

    // ---------------- sources ----------------

    private void LiveTick()
    {
        var now = DateTimeOffset.Now;
        var w = Widgets;

        if (_volume.Read() is { } vol)
        {
            if (_lastVolume is { } before && (Math.Abs(before.Percent - vol.Percent) >= 1 || before.Muted != vol.Muted) && w.LiveVolume)
                ShowHud(VolumeGlyph(vol.Percent, vol.Muted), Colors.White, "", vol.Muted ? "Muted" : $"{vol.Percent}%",
                        vol.Muted ? 0 : vol.Percent / 100.0, TimeSpan.FromSeconds(1.8));
            _lastVolume = vol;
        }

        bool caps = Keyboard.IsKeyToggled(Key.CapsLock), num = Keyboard.IsKeyToggled(Key.NumLock);
        if (w.LiveKeys && _caps is { } c && c != caps) ShowHud("", caps ? ChargeGreen : Colors.White, "Caps Lock", caps ? "On" : "Off", null, TimeSpan.FromSeconds(1.6));
        if (w.LiveKeys && _num is { } n && n != num) ShowHud("", num ? ChargeGreen : Colors.White, "Num Lock", num ? "On" : "Off", null, TimeSpan.FromSeconds(1.6));
        _caps = caps;
        _num = num;

        if (_timer.Active) RenderTimer(now);
        if (_timer.CheckFinished(now))
        {
            ShowHud("", TimerOrange, "Timer", "Done", null, TimeSpan.FromSeconds(6));
            foreach (var m in Mascots) m.Hop();
            System.Media.SystemSounds.Asterisk.Play(); // an event that needs attention (guidelines: alert sparingly)
            RenderCompact();
            RenderExpanded();
        }

        if (--_privacyCountdown <= 0) // every 2 s
        {
            _privacyCountdown = 8;
            var (mic, cam) = w.LivePrivacy ? PrivacyMonitor.Read() : (false, false);
            PrivacyDot.Fill = new SolidColorBrush(cam ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xFF, 0x9F, 0x0A));
            PrivacyDot.Visibility = Vis(mic || cam);
            CheckUpcoming(now);
        }

        // The open island stays open only while music plays; otherwise it closes on its own.
        bool playing = _media is { Playing: true };
        if (_expanded && playing) _autoCollapse.Stop();
        else if (_expanded && !_autoCollapse.IsEnabled && !Pill.IsMouseOver && _approval is null) ArmAutoCollapse();

        if (_hud is { } h && now >= h.Until)
        {
            _hud = null;
            ExpandedHud.Visibility = Visibility.Collapsed;
            RenderCompact();
            if (_revealedForHud) { _revealedForHud = false; HideSoon(); } // slide back away
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.StatusChange) Dispatcher.BeginInvoke(CheckBattery);
    }

    /// <summary>Also called from the minute device refresh, so slow drains still trip the 20 % / 10 % alerts.</summary>
    private void CheckBattery()
    {
        var now = Win32.Battery();
        var kind = BatteryAlerts.Check(_lastPower, now);
        _lastPower = now;
        if (!Widgets.LiveBattery || now is not { } b) return;
        switch (kind)
        {
            case BatteryAlerts.Kind.Charging:
                ShowHud("", ChargeGreen, "Charging", $"{b.Percent}%", b.Percent / 100.0, TimeSpan.FromSeconds(3));
                break;
            case BatteryAlerts.Kind.Unplugged:
                ShowHud(BatteryGlyph(b.Percent, false), Colors.White, "On battery", $"{b.Percent}%", b.Percent / 100.0, TimeSpan.FromSeconds(2.5));
                break;
            case BatteryAlerts.Kind.Low:
                ShowHud(BatteryGlyph(b.Percent, false), LowRed, "Low battery", $"{b.Percent}%", b.Percent / 100.0, TimeSpan.FromSeconds(5));
                SetBaseExpression("surprised");
                break;
        }
    }

    private async void OnBluetooth(string name, bool connected)
    {
        if (!Widgets.LiveBluetooth) return;
        ShowHud(BluetoothWatcher.Glyph(name), connected ? BtBlue : Colors.White, name, connected ? "Connected" : "Disconnected", null, TimeSpan.FromSeconds(3.5));
        if (!connected) return;
        await Task.Delay(TimeSpan.FromSeconds(2)); // Windows fills in the battery a moment after connecting
        var battery = (await BluetoothBatteries.ReadAsync()).FirstOrDefault(b => b.Name == name);
        if (battery is not null && _hud?.Text == name)
            ShowHud(_hud.Glyph, BtBlue, name, $"{battery.Percent}%", battery.Percent / 100.0, TimeSpan.FromSeconds(3));
    }

    private static string VolumeGlyph(int percent, bool muted) =>
        muted || percent == 0 ? "" : percent < 34 ? "" : percent < 67 ? "" : "";

    // ---------------- alert line ----------------

    private void ShowHud(string glyph, Color tint, string text, string value, double? level, TimeSpan duration)
    {
        _hud = new Hud(glyph, tint, text, value, level, DateTimeOffset.Now + duration);
        // Every alert is seen: an auto-hidden island slides out for it; an open island shows it as a corner chip.
        if (_hidden && IsVisible) { _revealedForHud = true; SlidePill(hide: false); }
        ExpandedHud.Visibility = Vis(_expanded);
        XHudGlyph.Text = glyph;
        XHudGlyph.Foreground = new SolidColorBrush(tint);
        XHudTrack.Visibility = Vis(level is not null);
        XHudFill.Background = new SolidColorBrush(tint);
        XHudFill.Width = 90 * Math.Clamp(level ?? 0, 0, 1);
        XHudText.Text = level is null && text.Length > 0 ? $"{text} · {value}" : value;
        HudGlyph.Text = glyph;
        HudGlyph.Foreground = new SolidColorBrush(tint);
        HudText.Text = text;
        HudValue.Text = value;
        HudText.Visibility = Vis(level is null);
        HudTrack.Visibility = Vis(level is not null);
        HudFill.Background = new SolidColorBrush(tint);
        if (level is { } l)
        {
            void Size() => HudFill.Width = HudTrack.ActualWidth * Math.Clamp(l, 0, 1);
            if (HudTrack.ActualWidth > 0) Size(); else Dispatcher.BeginInvoke(Size, DispatcherPriority.Loaded);
        }
        RenderCompact();
    }

    // ---------------- timer ----------------

    public void StartTimer(int minutes)
    {
        _timer.Start(TimeSpan.FromMinutes(minutes), DateTimeOffset.Now);
        _picked = null;
        RenderTimer(DateTimeOffset.Now);
        RenderCompact();
        RenderExpanded();
    }

    public void ToggleTimer()
    {
        _timer.TogglePause(DateTimeOffset.Now);
        RenderTimer(DateTimeOffset.Now);
    }

    public void CancelTimer()
    {
        _timer.Cancel();
        RenderCompact();
        RenderExpanded();
    }

    private void RenderTimer(DateTimeOffset now)
    {
        var left = _timer.Remaining(now);
        TimerLine.Text = TimerBig.Text = CountdownTimer.Format(left);
        TimerPauseButton.Content = _timer.Running ? "Pause" : "Resume";
        double fraction = _timer.Total.TotalSeconds > 0 ? left.TotalSeconds / _timer.Total.TotalSeconds : 0;
        TimerArc.Data = BubbleArc.Data = Arc(10, 8.75, fraction);
    }

    /// <summary>Clockwise arc from 12 o'clock covering <paramref name="fraction"/> of a circle.</summary>
    private static Geometry Arc(double center, double radius, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        double angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
                                           new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    // ---------------- detached circle (Apple "minimal") ----------------

    /// <summary>Shows the second live activity in a small circle beside the resting pill.</summary>
    private void RenderBubble(string? kind, double pillWidth, double pillHeight)
    {
        bool show = kind is not null && !_expanded && !SideDocked && !_hidden;
        if (show)
        {
            BubbleRing.Visibility = Vis(kind == "timer");
            BubbleEq.Visibility = Vis(kind == "music");
            BubbleDot.Visibility = Vis(kind == "code");
            BubbleDot.Fill = new SolidColorBrush(CodeColor(_tracker.Active?.State));
            Color edge = kind switch { "timer" => TimerOrange, "music" => MusicTint, _ => CodeColor(_tracker.Active?.State) };
            Bubble.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, edge.R, edge.G, edge.B));
            const double gap = 8;
            Bubble.Width = Bubble.Height = pillHeight;
            Bubble.CornerRadius = new CornerRadius(pillHeight / 2);
            Bubble.HorizontalAlignment = Pill.HorizontalAlignment;
            Bubble.VerticalAlignment = Pill.VerticalAlignment;
            Bubble.Margin = Pill.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => new Thickness(Pill.Margin.Left + pillWidth + gap, Pill.Margin.Top, 0, Pill.Margin.Bottom),
                HorizontalAlignment.Right => new Thickness(0, Pill.Margin.Top, Pill.Margin.Right + pillWidth + gap, Pill.Margin.Bottom),
                _ => Pill.Margin,
            };
            BubbleShift.X = Pill.HorizontalAlignment == HorizontalAlignment.Center ? pillWidth / 2 + gap + pillHeight / 2 : 0;
            Bubble.Visibility = Visibility.Visible;
        }
        Bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, Motion.Ms(show ? 260 : 120))
        {
            BeginTime = Motion.Delay(show ? 200 : 0), // after the pill has settled, like iOS
            EasingFunction = Motion.Smooth,
        });
    }

    // ---------------- interactions ----------------

    /// <summary>Mouse wheel over the open island moves between tabs (from the Windhawk mod).</summary>
    private void OnPillWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_expanded) return;
        var tabs = new[] { (TabMusic, View.Music), (TabCode, View.Code), (TabAlerts, View.Alerts), (TabMail, View.Mail), (TabApps, View.Apps),
                           (TabTimer, View.Timer), (TabCalendar, View.Calendar), (TabSystem, View.System) } // same order as on screen
            .Where(t => t.Item1.Visibility == Visibility.Visible).ToList();
        if (tabs.Count < 2) return;
        int i = tabs.FindIndex(t => t.Item2 == _view);
        int next = Math.Clamp(i + (e.Delta < 0 ? 1 : -1), 0, tabs.Count - 1);
        if (next != i) Pick(tabs[next].Item2);
        e.Handled = true;
    }

    /// <summary>Click the progress bar to jump there.</summary>
    private void OnSeek(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_media is not { Duration.TotalSeconds: > 0 } m || ProgressTrack.ActualWidth <= 0) return;
        double fraction = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
        _mediaService?.Seek(TimeSpan.FromSeconds(m.Duration.TotalSeconds * fraction));
    }
}
