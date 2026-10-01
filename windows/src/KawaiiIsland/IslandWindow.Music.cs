using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KawaiiIsland.Controls;
using KawaiiIsland.Services.Media;

namespace KawaiiIsland;

/// <summary>
/// Music module (spec §3.3). Compact: album art + title + equalizer while something plays. Expanded: art, title,
/// progress (interpolated locally) and controls. Also owns which expanded view is shown
/// (Home / Music / Code); tabs appear only when more than one module has something to show.
/// </summary>
public partial class IslandWindow
{
    private enum View { Home, Music, Code }

    private static readonly double[] EqPeriods = [0.42, 0.55, 0.37, 0.5, 0.46];

    private MediaService? _mediaService;
    private MediaSnapshot? _media;
    private View _view = View.Home;
    private View? _picked;   // tab the user chose; null = automatic
    private bool _codeBusy;  // Code mode owns the compact pill while Claude is working (set by RenderCode)
    private bool? _eqPlaying;
    private Rectangle[] _eqBars = [];

    public bool MusicEnabled => _config.Current.Modules.Music.Enabled;

    /// <summary>Resting face: happy while music plays (spec §2), otherwise idle.</summary>
    private string IdleExpression => _media?.Playing == true ? "happy" : "idle";

    private void InitMusic()
    {
        _eqBars = [.. BuildEq(Eq), .. BuildEq(BigEq)];
        PrevButton.Click += (_, _) => _mediaService?.Previous();
        PlayButton.Click += (_, _) => _mediaService?.PlayPause();
        NextButton.Click += (_, _) => _mediaService?.Next();
        TabMusic.Click += (_, _) => { _picked = View.Music; RenderExpanded(); TickMusic(); };
        TabCode.Click += (_, _) => { _picked = View.Code; RenderExpanded(); };
        ProgressTrack.SizeChanged += (_, _) => TickMusic();
        OnMediaChanged(null);
        if (MusicEnabled) StartMusic();
    }

    public void SetMusicEnabled(bool on)
    {
        _config.Current.Modules.Music.Enabled = on;
        _config.SaveSoon();
        if (on) StartMusic(); else StopMusic();
    }

    private async void StartMusic()
    {
        if (_mediaService is not null) return;
        var service = new MediaService(Dispatcher);
        service.Changed += OnMediaChanged;
        _mediaService = service;
        try { await service.StartAsync(); }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            StopMusic(); // media controls unavailable (e.g. Windows N without the Media Feature Pack): island works without music
        }
    }

    private void StopMusic()
    {
        _mediaService?.Dispose();
        _mediaService = null;
        OnMediaChanged(null);
    }

    private void OnMediaChanged(MediaSnapshot? m)
    {
        bool wasPlaying = _media?.Playing == true;
        _media = m;
        if (m is not null)
        {
            var tint = m.Tint ?? Accent();
            var tintBrush = new SolidColorBrush(tint);
            foreach (var bar in _eqBars) bar.Fill = tintBrush;
            ArtPlaceholder.Background = new LinearGradientBrush(tint, Color.Multiply(tint, 0.55f) with { A = 255 }, 45);
            var art = m.Art is { } img ? new ImageBrush(img) { Stretch = Stretch.UniformToFill } : null;
            BigArt.Background = art;
            MiniArt.Background = (Brush?)art ?? ArtPlaceholder.Background;

            MusicLine.Text = SongTitle.Text = m.Title;
            SongArtist.Text = string.Join(" — ", new[] { m.Artist, m.Album }.Where(s => s.Length > 0));
            PlayButton.Content = m.Playing ? "" : ""; // pause : play
            PrevButton.IsEnabled = m.CanPrevious;
            PlayButton.IsEnabled = m.CanPlayPause;
            NextButton.IsEnabled = m.CanNext;
        }
        AnimateEq(m?.Playing == true);
        if (wasPlaying != (m?.Playing == true) && _baseExpression is "idle" or "happy") SetBaseExpression(IdleExpression);
        RenderCompact();
        RenderExpanded();
        TickMusic();
    }

    /// <summary>Playhead, times and progress bar; called every second by the clock.</summary>
    private void TickMusic()
    {
        if (_media is not { } m || MusicPanel.Visibility != Visibility.Visible) return;
        var pos = m.PositionAt(DateTimeOffset.Now);
        bool timed = m.Duration > TimeSpan.Zero;
        PosText.Text = timed ? MediaMath.Format(pos) : "";
        RemText.Text = timed ? "-" + MediaMath.Format(m.Duration - pos) : "";
        ProgressFill.Width = timed ? ProgressTrack.ActualWidth * (pos / m.Duration) : 0;
    }

    // ---------------- which view is showing ----------------

    /// <summary>Compact pill: Code mode while Claude works, else music while it plays, else mascot + clock.</summary>
    private void RenderCompact()
    {
        bool music = !_codeBusy && _media is { Playing: true };
        SetCompact(_codeBusy || music);
        CodeLinePanel.Visibility = Vis(_codeBusy);
        MusicLinePanel.Visibility = Eq.Visibility = Vis(music);
        MascotSmall.Visibility = Clock.Visibility = Vis(!music);
    }

    private void RenderExpanded()
    {
        bool code = CodeMode, music = _media is not null;
        _view = _picked switch
        {
            View.Code when code => View.Code,
            View.Music when music => View.Music,
            _ when code && _codeBusy => View.Code,
            _ when music && _media!.Playing => View.Music,
            _ => code ? View.Code : music ? View.Music : View.Home,
        };
        bool tabs = code && music;
        TabRow.Visibility = Vis(tabs || _view == View.Music);
        Tabs.Visibility = Vis(tabs);
        TabMusic.IsChecked = _view == View.Music;
        TabCode.IsChecked = _view == View.Code;
        MascotTiny.Visibility = SmallClock.Visibility = Vis(_view == View.Music);
        MainRow.Visibility = Vis(_view != View.Music);
        ClockBlock.Visibility = Greeting.Visibility = Vis(_view == View.Home);
        CodeHeader.Visibility = CodeMeters.Visibility = CodeFooter.Visibility = Vis(_view == View.Code);
        MusicPanel.Visibility = Vis(_view == View.Music);
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Hover glow follows the album art while the music pill is showing.</summary>
    private Color GlowColor() => MusicLinePanel.Visibility == Visibility.Visible && _media?.Tint is { } t ? t : Accent();

    // ---------------- equalizer ----------------

    private static IEnumerable<Rectangle> BuildEq(Panel host)
    {
        for (int i = 0; i < EqPeriods.Length; i++)
        {
            var bar = new Rectangle { Width = 3, Height = 6, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0) };
            host.Children.Add(bar);
            yield return bar;
        }
    }

    /// <summary>Bars bounce at slightly different speeds while playing; they rest low when paused.</summary>
    private void AnimateEq(bool playing)
    {
        if (_eqPlaying == playing) return; // timeline updates must not restart the bounce
        _eqPlaying = playing;
        for (int i = 0; i < _eqBars.Length; i++)
        {
            var bar = _eqBars[i];
            if (playing && Motion.Enabled)
                bar.BeginAnimation(HeightProperty, new DoubleAnimation(4, 16, TimeSpan.FromSeconds(EqPeriods[i % EqPeriods.Length]))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            else
            {
                bar.BeginAnimation(HeightProperty, null);
                bar.Height = playing ? 10 : 5;
            }
        }
    }
}
