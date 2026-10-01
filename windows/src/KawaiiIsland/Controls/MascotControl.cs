using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KawaiiIsland.Services;
using Native = KawaiiIsland.Services.Native;

namespace KawaiiIsland.Controls;

/// <summary>
/// The mascot face. Art comes from the generated Assets/Mascots.xaml (one DrawingImage per skin x expression).
/// Idle life: random blink every 4–7 s, a 1 px bob, slow breathing, and round eyes that follow the cursor
/// (projected on a sphere, like Coucou's Mochi). Reactions: Squish(), Wobble() and Hop().
/// </summary>
public sealed class MascotControl : Image
{
    public static readonly DependencyProperty SkinProperty = DependencyProperty.Register(
        nameof(Skin), typeof(string), typeof(MascotControl),
        new PropertyMetadata(AppConfig.DefaultMascot, (d, _) => ((MascotControl)d).Refresh()));

    public static readonly DependencyProperty ExpressionProperty = DependencyProperty.Register(
        nameof(Expression), typeof(string), typeof(MascotControl),
        new PropertyMetadata("idle", (d, _) => ((MascotControl)d).Refresh()));

    /// <summary>"" = everyday look; "code" = hoodie + glasses (coding / lock-in mode).</summary>
    public static readonly DependencyProperty OutfitProperty = DependencyProperty.Register(
        nameof(Outfit), typeof(string), typeof(MascotControl),
        new PropertyMetadata("", (d, _) => ((MascotControl)d).Refresh()));

    public string Skin { get => (string)GetValue(SkinProperty); set => SetValue(SkinProperty, value); }
    public string Outfit { get => (string)GetValue(OutfitProperty); set => SetValue(OutfitProperty, value); }
    public string Expression { get => (string)GetValue(ExpressionProperty); set => SetValue(ExpressionProperty, value); }

    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotate = new();
    private readonly TranslateTransform _bob = new();
    private readonly ScaleTransform _breath = new();
    private readonly TranslateTransform _hop = new();
    private readonly TranslateTransform _eyeShift = new(); // in 64-unit art space
    private bool _tracking; // current art has a separate eye layer
    private readonly DispatcherTimer _blinkTimer = new();
    private bool _blinking;

    public MascotControl()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { _breath, _scale, _rotate, _bob, _hop } };
        _blinkTimer.Tick += async (_, _) => { _blinkTimer.Stop(); await BlinkOnceAsync(idleOnly: true); ScheduleBlink(); };
        Loaded += (_, _) => { Refresh(); StartIdle(); EyeTracker.Add(this); };
        Unloaded += (_, _) => { _blinkTimer.Stop(); EyeTracker.Remove(this); };
    }

    /// <summary>Vector art for a skin/expression/outfit; unknown skins fall back to the default mascot.
    /// "none" → no picture; "custom" → the user's own picture (same for every expression).</summary>
    public static ImageSource? Art(string skin, string expression, string outfit = "")
    {
        if (skin == AppConfig.NoMascot) return null;
        if (skin == AppConfig.CustomMascot) return CustomArt();
        string suffix = outfit.Length > 0 ? "." + outfit : "";
        return (ImageSource)(Application.Current.TryFindResource($"Mascot.{skin}.{expression}{suffix}")
                             ?? Application.Current.FindResource($"Mascot.{AppConfig.DefaultMascot}.{expression}{suffix}"));
    }

    private static ImageSource? _custom;

    /// <summary>Loaded once into memory (file not kept open); <see cref="ReloadCustom"/> after it changes.</summary>
    private static ImageSource? CustomArt()
    {
        if (_custom is not null) return _custom;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 128;
            bmp.UriSource = new Uri(AppConfig.CustomMascotPath);
            bmp.EndInit();
            bmp.Freeze();
            return _custom = bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            return null; // unreadable picture: no mascot rather than a crash
        }
    }

    public static void ReloadCustom() => _custom = null;

    private void Refresh()
    {
        string expression = _blinking ? "blink" : Expression;
        if (Skin == AppConfig.CustomMascot && CustomArt() is { } picture) { Source = CustomFace(picture, expression); _tracking = true; return; }
        string key = $"Mascot.{Skin}.{expression}{(Outfit.Length > 0 ? "." + Outfit : "")}";
        if (Layer(key, "under") is { } under && Layer(key, "eyes") is { } eyes && Layer(key, "over") is { } over)
        {
            var group = new DrawingGroup();
            group.Children.Add(under);
            group.Children.Add(new DrawingGroup { Children = { eyes }, Transform = _eyeShift });
            group.Children.Add(over);
            Source = new DrawingImage(group);
            _tracking = true;
            return;
        }
        _tracking = false;
        Source = Art(Skin, expression, Outfit);
    }

    /// <summary>
    /// A user's picture gets every mascot feature without knowing where its eyes are: it leans toward the cursor,
    /// poses per expression (blink squash, hover grow, sleepy droop…), wears the hoodie + glasses in coding mode,
    /// and shows the shared expression marks (!, zz, anger, dizzy/happy sparkles). Squish/wobble/hop/breathing come
    /// from the control's own transforms like any skin.
    /// </summary>
    private DrawingImage CustomFace(ImageSource picture, string expression)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 64, 64)))); // fixed bounds
        bool code = Outfit == "code";
        if (code && Layer("Mascot.custom.hoodie", "under") is { } hood) group.Children.Add(hood);

        var (sx, sy) = expression switch
        {
            "wow" => (1.08, 1.08),
            "surprised" => (1.05, 1.05),
            "blink" => (1.0, 0.9),
            "sleepy" => (1.02, 0.94),
            "annoyed" => (1.05, 0.95),
            _ => (1.0, 1.0),
        };
        var pose = new TransformGroup { Children = { new ScaleTransform(sx, sy, 32, 31), _eyeShift } };
        group.Children.Add(new DrawingGroup { Children = { new ImageDrawing(picture, new Rect(code ? 9 : 6, code ? 8 : 5, code ? 46 : 52, code ? 46 : 52)) }, Transform = pose });

        if (code && Layer("Mascot.custom.hoodie", "over") is { } glasses) group.Children.Add(glasses);
        if (Layer("Mascot.custom.extra", expression) is { } marks) group.Children.Add(marks);
        return new DrawingImage(group);
    }

    /// <summary>Shared resource drawings are frozen once so every mascot can reuse them.</summary>
    private static Drawing? Layer(string key, string layer)
    {
        if (Application.Current.TryFindResource($"{key}.{layer}") is not Drawing d) return null;
        if (!d.IsFrozen && d.CanFreeze) d.Freeze();
        return d;
    }

    /// <summary>Eyes glide toward the cursor: offset grows with distance and saturates (sphere projection), eased per frame.</summary>
    internal void TrackCursor(Point cursor)
    {
        double tx = 0, ty = 0;
        if (_tracking && Motion.Enabled && PresentationSource.FromVisual(this) is not null && !double.IsNaN(cursor.X))
        {
            var c = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            double dx = cursor.X - c.X, dy = cursor.Y - c.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 0.5)
            {
                double k = 1 / Math.Sqrt(d * d + 140 * 140); // ~1 when far away, 0 on top of the mascot
                tx = 2.6 * dx * k;
                ty = 2.0 * dy * k;
            }
        }
        _eyeShift.X += (tx - _eyeShift.X) * 0.25;
        _eyeShift.Y += (ty - _eyeShift.Y) * 0.25;
    }

    private void StartIdle()
    {
        if (Motion.Enabled)
            _bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -1, TimeSpan.FromSeconds(1.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        if (Motion.Enabled)
            _breath.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.03, TimeSpan.FromSeconds(1.9))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        ScheduleBlink();
    }

    private void ScheduleBlink()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(4000, 7000));
        _blinkTimer.Start();
    }

    /// <summary>Closes the eyes for 120 ms. With idleOnly, only blinks when the face is idle (not wow/annoyed/...).</summary>
    public async Task BlinkOnceAsync(bool idleOnly = false)
    {
        if (idleOnly && Expression != "idle") return;
        _blinking = true; Refresh();
        await Task.Delay(120);
        _blinking = false; Refresh();
    }

    /// <summary>Squash-and-stretch when poked.</summary>
    public void Squish()
    {
        if (!Motion.Enabled) return;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(380, (0.3, 1.3), (0.65, 0.92), (1, 1)));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(380, (0.3, 0.7), (0.65, 1.1), (1, 1)));
    }

    /// <summary>Happy little hop (a Claude session finished).</summary>
    public void Hop()
    {
        if (Motion.Enabled) _hop.BeginAnimation(TranslateTransform.YProperty, Keys(420, (0.4, -5), (1, 0)));
    }

    /// <summary>Dizzy wobble (±14°) until turned off; off eases back upright (200 ms).</summary>
    public void Wobble(bool on)
    {
        if (!on || !Motion.Enabled)
        {
            _rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, Motion.Ms(200)) { EasingFunction = Motion.Smooth });
            return;
        }
        var a = Keys(450, (0.25, -14), (0.75, 14), (1, 0));
        a.RepeatBehavior = RepeatBehavior.Forever;
        _rotate.BeginAnimation(RotateTransform.AngleProperty, a);
    }

    private static DoubleAnimationUsingKeyFrames Keys(int ms, params (double at, double value)[] keys)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        foreach (var (at, value) in keys)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromPercent(at), new SineEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }
}

/// <summary>One ~30 fps timer for every visible mascot: polls the cursor (anywhere on screen) and nudges their eyes.</summary>
internal static class EyeTracker
{
    private static readonly List<MascotControl> Live = [];
    private static readonly DispatcherTimer Timer = new(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render,
        (_, _) => { var p = Native.Win32.CursorPos(); foreach (var m in Live) if (m.IsVisible) m.TrackCursor(p); },
        Dispatcher.CurrentDispatcher) { IsEnabled = false };

    public static void Add(MascotControl m) { if (!Live.Contains(m)) Live.Add(m); Timer.IsEnabled = true; }
    public static void Remove(MascotControl m) { Live.Remove(m); Timer.IsEnabled = Live.Count > 0; }
}

/// <summary>Respects Windows "Animation effects" (Settings → Accessibility → Visual effects).</summary>
internal static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    public static Duration Ms(int ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 0));
    public static TimeSpan Delay(int ms) => TimeSpan.FromMilliseconds(Enabled ? ms : 0);

    /// <summary>Apple-style ease-out: quick start, long soft settle, never overshoots (no bounce).</summary>
    public static IEasingFunction Smooth => new QuarticEase { EasingMode = EasingMode.EaseOut };
}

/// <summary>Detects a burst of N clicks within a time window (3 clicks in 900 ms → dizzy).</summary>
internal sealed class ClickBurst(int count = 3, int windowMs = 900)
{
    private readonly Queue<long> _clicks = new();

    /// <returns>true when this click completes a burst (the burst then resets).</returns>
    public bool Register(long nowMs)
    {
        while (_clicks.Count > 0 && nowMs - _clicks.Peek() > windowMs) _clicks.Dequeue();
        _clicks.Enqueue(nowMs);
        if (_clicks.Count < count) return false;
        _clicks.Clear();
        return true;
    }
}
