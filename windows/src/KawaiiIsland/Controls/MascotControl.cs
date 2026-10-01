using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KawaiiIsland.Services;

namespace KawaiiIsland.Controls;

/// <summary>
/// The mascot face. Art comes from the generated Assets/Mascots.xaml (one DrawingImage per skin x expression).
/// Idle life: random blink every 4–7 s + a 1 px bob. Reactions: Squish() and Wobble().
/// </summary>
public sealed class MascotControl : Image
{
    public static readonly DependencyProperty SkinProperty = DependencyProperty.Register(
        nameof(Skin), typeof(string), typeof(MascotControl),
        new PropertyMetadata(AppConfig.DefaultMascot, (d, _) => ((MascotControl)d).Refresh()));

    public static readonly DependencyProperty ExpressionProperty = DependencyProperty.Register(
        nameof(Expression), typeof(string), typeof(MascotControl),
        new PropertyMetadata("idle", (d, _) => ((MascotControl)d).Refresh()));

    public string Skin { get => (string)GetValue(SkinProperty); set => SetValue(SkinProperty, value); }
    public string Expression { get => (string)GetValue(ExpressionProperty); set => SetValue(ExpressionProperty, value); }

    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotate = new();
    private readonly TranslateTransform _bob = new();
    private readonly DispatcherTimer _blinkTimer = new();
    private bool _blinking;

    public MascotControl()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { _scale, _rotate, _bob } };
        _blinkTimer.Tick += async (_, _) => { _blinkTimer.Stop(); await BlinkOnceAsync(idleOnly: true); ScheduleBlink(); };
        Loaded += (_, _) => { Refresh(); StartIdle(); };
        Unloaded += (_, _) => _blinkTimer.Stop();
    }

    /// <summary>Vector art for a skin/expression; unknown skins fall back to the default mascot.</summary>
    public static ImageSource Art(string skin, string expression) =>
        (ImageSource)(Application.Current.TryFindResource($"Mascot.{skin}.{expression}")
                      ?? Application.Current.FindResource($"Mascot.{AppConfig.DefaultMascot}.{expression}"));

    private void Refresh() => Source = Art(Skin, _blinking ? "blink" : Expression);

    private void StartIdle()
    {
        if (Motion.Enabled)
            _bob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -1, TimeSpan.FromSeconds(1.2))
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

    /// <summary>Dizzy wobble (±14°) until turned off.</summary>
    public void Wobble(bool on)
    {
        if (!on || !Motion.Enabled) { _rotate.BeginAnimation(RotateTransform.AngleProperty, null); return; }
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

/// <summary>Respects Windows "Animation effects" (Settings → Accessibility → Visual effects).</summary>
internal static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;
    public static Duration Ms(int ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 0));
    public static TimeSpan Delay(int ms) => TimeSpan.FromMilliseconds(Enabled ? ms : 0);
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
