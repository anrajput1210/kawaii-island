using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KawaiiIsland.Controls;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Native;

namespace KawaiiIsland;

/// <summary>
/// Auto-hide (spec §5): in fullscreen (or always, if chosen) the pill slides off its docked edge and the AppBar
/// reservation is released. A 3 px strip stays on the edge: hovering it makes the mascot peek out and say hi,
/// clicking the mascot opens the island, and the island hides again 800 ms after the pointer leaves.
/// </summary>
public partial class IslandWindow
{
    private const double PeekSize = 46, PeekTuck = 14; // the peeking mascot keeps 14 px tucked past the edge

    private readonly DispatcherTimer _fullscreenPoll = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _hideSoon = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _peekRetract = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _autoHiding; // auto-hide is in effect (always on, or a fullscreen app is up on our monitor)
    private bool _hidden;     // the pill is currently slid off the edge
    private ContextMenu? _menu;

    /// <summary>Free-floating islands hide upward.
    /// ponytail: a floating island parked mid-screen also hides upward; add nearest-edge if people float it low.</summary>
    private Edge HideEdge => Win.AppBarEnabled ? DockEdge : Edge.Top;

    public bool AutoHideFullscreen => Win.AutoHideFullscreen;
    public bool AutoHideAlways => Win.AutoHideAlways;

    private void InitAutoHide()
    {
        _fullscreenPoll.Tick += (_, _) => UpdateAutoHide();
        _fullscreenPoll.Start();
        _hideSoon.Tick += (_, _) => TryHide();
        _peekRetract.Tick += (_, _) => ShowPeek(false);

        Pill.MouseEnter += (_, _) => _hideSoon.Stop();
        Pill.MouseLeave += (_, _) => HideSoon();
        EdgeStrip.MouseEnter += (_, _) => ShowPeek(true);
        EdgeStrip.MouseLeave += (_, _) => _peekRetract.Start();
        Peek.MouseEnter += (_, _) => _peekRetract.Stop();
        Peek.MouseLeave += (_, _) => _peekRetract.Start();
        Peek.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetExpanded(true); };
    }

    public void SetAutoHide(bool fullscreen, bool always)
    {
        Win.AutoHideFullscreen = fullscreen;
        Win.AutoHideAlways = always;
        _config.SaveSoon();
        UpdateAutoHide();
    }

    private void UpdateAutoHide()
    {
        if (_hwnd == 0) return;
        bool on = Win.AutoHideAlways
                  || (Win.AutoHideFullscreen && FullscreenWatcher.IsActive(Monitors.For(Monitors.WindowRect(_hwnd))));
        if (on == _autoHiding) return;
        _autoHiding = on;
        if (Win.AppBarEnabled) PlaceIsland(); // releases the reserved strip while auto-hiding, re-reserves after
        if (on) TryHide();
        else SlidePill(hide: false);
    }

    private void HideSoon()
    {
        if (!_autoHiding || _hidden) return;
        _hideSoon.Stop();
        _hideSoon.Start();
    }

    /// <summary>Hides unless the pointer is on the island or its menu is open (leaving re-arms HideSoon).</summary>
    private void TryHide()
    {
        _hideSoon.Stop();
        if (!_autoHiding || _hidden || Pill.IsMouseOver || _menu?.IsOpen == true) return;
        SlidePill(hide: true);
        SetExpanded(false);
    }

    /// <summary>Slide + fade the pill off its edge (250 ms) or back. The window clips it, so nothing needs to move.</summary>
    private void SlidePill(bool hide)
    {
        _hidden = hide;
        _hideSoon.Stop();
        bool across = HideEdge is Edge.Top or Edge.Bottom;
        var (x, y) = hide ? Toward(HideEdge, (across ? Pill.ActualHeight : Pill.ActualWidth) + Gap + 4) : (0, 0);
        var d = Motion.Ms(250);
        var ease = new CubicEase { EasingMode = hide ? EasingMode.EaseIn : EasingMode.EaseOut };
        HideShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, d) { EasingFunction = ease });
        HideShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease });
        Pill.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, d));
        Outline.BeginAnimation(OpacityProperty, new DoubleAnimation(hide ? 0 : 1, d));
        Pill.IsHitTestVisible = !hide;
        EdgeStrip.Visibility = Peek.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;
        ShowPeek(false);
    }

    private void ShowPeek(bool show)
    {
        _peekRetract.Stop();
        show &= _hidden;
        var (x, y) = Toward(HideEdge, show ? PeekTuck + Gap : PeekSize + Gap + 2);
        var d = Motion.Ms(show ? 380 : 220);
        IEasingFunction ease = show ? Motion.Smooth : new CubicEase { EasingMode = EasingMode.EaseIn };
        PeekShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, d) { EasingFunction = ease });
        PeekShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease });
        PeekBubble.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, Motion.Ms(200)) { BeginTime = Motion.Delay(show ? 180 : 0) });
        Peek.IsHitTestVisible = show;
    }

    /// <summary>Offset of <paramref name="distance"/> toward (past) the given edge.</summary>
    private static (double X, double Y) Toward(Edge edge, double distance) => edge switch
    {
        Edge.Top => (0, -distance),
        Edge.Bottom => (0, distance),
        Edge.Left => (-distance, 0),
        _ => (distance, 0),
    };

    /// <summary>Hover strip along the hide edge; the peeking mascot sits where the pill does, its bubble facing inward.</summary>
    private void AnchorEdgeParts()
    {
        var edge = HideEdge;
        bool across = edge is Edge.Top or Edge.Bottom;
        EdgeStrip.HorizontalAlignment = edge switch { Edge.Left => HorizontalAlignment.Left, Edge.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch };
        EdgeStrip.VerticalAlignment = edge switch { Edge.Top => VerticalAlignment.Top, Edge.Bottom => VerticalAlignment.Bottom, _ => VerticalAlignment.Stretch };
        EdgeStrip.Width = across ? double.NaN : 3;
        EdgeStrip.Height = across ? 3 : double.NaN;

        Peek.HorizontalAlignment = Pill.HorizontalAlignment;
        Peek.VerticalAlignment = Pill.VerticalAlignment;
        Peek.Margin = Pill.Margin;
        Peek.Children.Clear();
        if (edge == Edge.Right) { Peek.Children.Add(PeekBubble); Peek.Children.Add(PeekMascot); }
        else { Peek.Children.Add(PeekMascot); Peek.Children.Add(PeekBubble); }
        PeekBubble.VerticalAlignment = edge switch { Edge.Top => VerticalAlignment.Bottom, Edge.Bottom => VerticalAlignment.Top, _ => VerticalAlignment.Center };

        if (_hidden) SlidePill(hide: true); // edge may have changed: re-aim
        else ShowPeek(false);
    }
}
