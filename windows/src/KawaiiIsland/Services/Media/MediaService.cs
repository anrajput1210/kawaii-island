using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace KawaiiIsland.Services.Media;

/// <summary>What the island shows for the selected media session. Immutable; a new one is published on every change.</summary>
public sealed record MediaSnapshot(
    string Title, string Artist, string Album, bool Playing,
    TimeSpan Position, TimeSpan Duration, DateTimeOffset Updated,
    bool CanPrevious, bool CanPlayPause, bool CanNext,
    ImageSource? Art, Color? Tint)
{
    public TimeSpan PositionAt(DateTimeOffset now) => MediaMath.Interpolate(Position, Updated, now, Playing, Duration);
}

/// <summary>
/// Windows media controls (SMTC, spec §3.3): anything that shows up in the Windows volume flyout — Spotify, Apple Music,
/// browser tabs, VLC — works without a login. Follows the system's current session (the one Windows' own media
/// flyout shows). Everything runs on the UI dispatcher (WinRT awaits don't block it); SMTC events are marshalled there.
/// </summary>
internal sealed class MediaService : IDisposable
{
    private readonly Dispatcher _ui;
    private SessionManager? _manager;
    private Session? _session;
    private (string Title, string Artist, string Album) _props = ("", "", "");
    private ImageSource? _art;
    private Color? _tint;
    private bool _disposed;

    public event Action<MediaSnapshot?>? Changed;

    public MediaService(Dispatcher ui) => _ui = ui;

    public async Task StartAsync()
    {
        var manager = await SessionManager.RequestAsync();
        if (_disposed) return;
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        SelectSession();
    }

    public void Dispose()
    {
        _disposed = true;
        Hook(_session, false);
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
        _manager = null;
        _session = null;
    }

    // ---------------- controls ----------------

    public void PlayPause() => Send(s => s.TryTogglePlayPauseAsync());
    public void Next() => Send(s => s.TrySkipNextAsync());
    public void Previous() => Send(s => s.TrySkipPreviousAsync());
    public void Seek(TimeSpan position) => Send(s => s.TryChangePlaybackPositionAsync(position.Ticks));


    private async void Send(Func<Session, IAsyncOperation<bool>> command)
    {
        if (_session is not { } s) return;
        try { await command(s); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { } // app went away mid-click
    }

    // ---------------- session tracking ----------------

    private void OnSessionsChanged(SessionManager sender, SessionsChangedEventArgs args) => Post(SelectSession);
    private void OnCurrentSessionChanged(SessionManager sender, CurrentSessionChangedEventArgs args) => Post(SelectSession);
    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => Post(() => _ = LoadPropertiesAsync(sender));
    private void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => Post(Publish);
    private void OnTimelinePropertiesChanged(Session sender, TimelinePropertiesChangedEventArgs args) => Post(Publish);

    private void Post(Action action) => _ui.BeginInvoke(() => { if (!_disposed) action(); });

    private void SelectSession()
    {
        if (_manager is null) return;
        var next = _manager.GetCurrentSession() ?? _manager.GetSessions().FirstOrDefault();

        if (!ReferenceEquals(next, _session))
        {
            Hook(_session, false);
            _session = next;
            Hook(_session, true);
            _props = ("", "", "");
            _art = null;
            _tint = null;
            _ = LoadPropertiesAsync(next);
        }
        Publish();
    }

    private void Hook(Session? s, bool on)
    {
        if (s is null) return;
        if (on)
        {
            s.MediaPropertiesChanged += OnMediaPropertiesChanged;
            s.PlaybackInfoChanged += OnPlaybackInfoChanged;
            s.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
        else
        {
            s.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            s.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            s.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
    }

    private async Task LoadPropertiesAsync(Session? s)
    {
        if (s is null) return;
        // A misbehaving media app (closed mid-call, odd thumbnail) must never take the island down.
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(s, _session) || p is null) return;
            var props = (p.Title ?? "", p.Artist ?? "", p.AlbumTitle ?? "");
            if (props != _props || _art is null)
            {
                var (art, tint) = await LoadArtAsync(p.Thumbnail);
                if (!ReferenceEquals(s, _session)) return;
                _art = art;
                _tint = tint;
            }
            _props = props;
            Publish();
        }
        catch (Exception) { }
    }

    /// <summary>Album art at 160 px for display plus its tint from a 32 px copy.</summary>
    private static async Task<(ImageSource?, Color?)> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return (null, null);
        var bytes = await WinRtImage.ReadAsync(thumbnail);
        var art = WinRtImage.Decode(bytes, 160);
        var small = new FormatConvertedBitmap(WinRtImage.Decode(bytes, 32), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[small.PixelWidth * small.PixelHeight * 4];
        small.CopyPixels(pixels, small.PixelWidth * 4, 0);
        return (art, MediaMath.DominantColor(pixels));
    }

    private void Publish()
    {
        MediaSnapshot? snap = null;
        if (_session is { } s && _props.Title.Length > 0)
        {
            try
            {
                var playback = s.GetPlaybackInfo();
                var timeline = s.GetTimelineProperties();
                var c = playback.Controls;
                snap = new MediaSnapshot(
                    _props.Title, _props.Artist, _props.Album,
                    playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    timeline.Position - timeline.StartTime, timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime,
                    c.IsPreviousEnabled, c.IsPlayPauseToggleEnabled, c.IsNextEnabled,
                    _art, _tint);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        }
        Changed?.Invoke(snap);
    }

}
