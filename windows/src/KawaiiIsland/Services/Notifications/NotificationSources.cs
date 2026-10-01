using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace KawaiiIsland.Services.Notifications;

/// <summary>Where mirrored notifications come from (spec §3.2): the real Windows listener, or a mock for demos/tests.</summary>
internal interface INotificationSource : IDisposable
{
    event Action<IslandNotification>? Arrived;

    /// <returns>null when running, otherwise a friendly explanation of why notifications can't be read.</returns>
    Task<string?> StartAsync();
}

/// <summary>
/// Windows toasts via UserNotificationListener. The app is unpackaged; recent Windows 11 builds grant it access
/// like any desktop app (Settings → Privacy &amp; security → Notifications). Older builds need package identity
/// (MSIX or a sparse package, see README) and report that through StartAsync.
/// The change event is unreliable for desktop apps, so this polls every 2 s and only reports toasts it hasn't seen.
/// </summary>
internal sealed class ToastListenerSource : INotificationSource
{
    public const string AccessHelp =
        "Windows isn't letting Kawaii Island read notifications. Turn on Settings → Privacy & security → Notifications → " +
        "\"Let apps access your notifications\".";

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private UserNotificationListener? _listener;
    private HashSet<uint> _known = [];
    private bool _polling;

    public event Action<IslandNotification>? Arrived;

    public async Task<string?> StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            if (await _listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed) return AccessHelp;
            // Only toasts that arrive from now on; what's already in Action Center isn't news.
            _known = (await _listener.GetNotificationsAsync(NotificationKinds.Toast)).Select(n => n.Id).ToHashSet();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return "This version of Windows only shares notifications with installed (MSIX) apps. " +
                   "The island still works; notification mirroring needs the installer build.";
        }
        _poll.Tick += OnPoll;
        _poll.Start();
        return null;
    }

    public void Dispose()
    {
        _poll.Stop();
        _poll.Tick -= OnPoll;
    }

    private async void OnPoll(object? sender, EventArgs e)
    {
        if (_polling || _listener is null) return;
        _polling = true;
        // A broken toast or a revoked permission must never take the island down; the next poll retries.
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in current.Where(n => !_known.Contains(n.Id)))
                if (await ToIslandAsync(n) is { } mirrored) Arrived?.Invoke(mirrored);
            _known = current.Select(n => n.Id).ToHashSet();
        }
        catch (Exception) { }
        finally { _polling = false; }
    }

    private static async Task<IslandNotification?> ToIslandAsync(UserNotification n)
    {
        var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
                     .Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (texts is not { Count: > 0 }) return null;
        var display = n.AppInfo?.DisplayInfo;
        System.Windows.Media.ImageSource? icon = null;
        try
        {
            if (display?.GetLogo(new Windows.Foundation.Size(64, 64)) is { } logo)
                icon = WinRtImage.Decode(await WinRtImage.ReadAsync(logo), 64);
        }
        catch (Exception) { } // no logo: the island draws a letter avatar
        return new IslandNotification(n.Id, display?.DisplayName ?? "Notification", texts[0], NotificationFeed.Body(texts.Skip(1)), icon, n.CreationTime);
    }
}

/// <summary>Fake toasts every 20 s (config: modules.notifications.source = "mock"). For demos and testing without real apps.</summary>
internal sealed class MockNotificationSource : INotificationSource
{
    private static readonly (string App, string Title, string Body)[] Samples =
    [
        ("Chat", "@mika", "are you on tonight? we're doing the raid at 9, bring snacks"),
        ("Calendar", "Standup in 10 minutes", "Room 2 · Teams"),
        ("Mail", "Hana Park", "Lunch on Friday?"),
    ];

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private uint _next;

    public event Action<IslandNotification>? Arrived;

    public Task<string?> StartAsync()
    {
        _timer.Tick += OnTick;
        _timer.Start();
        return Task.FromResult<string?>(null);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Interval = TimeSpan.FromSeconds(20);
        var (app, title, body) = Samples[_next % Samples.Length];
        Arrived?.Invoke(new IslandNotification(++_next, app, title, body, null, DateTimeOffset.Now));
    }
}
