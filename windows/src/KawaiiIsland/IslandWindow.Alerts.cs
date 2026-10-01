using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KawaiiIsland.Services;
using KawaiiIsland.Services.ClaudeCode;
using KawaiiIsland.Services.Notifications;

namespace KawaiiIsland;

/// <summary>
/// Notifications module (spec §3.2): new Windows toasts flash in the compact pill (~4 s) and open the island for 3 s
/// unless Do Not Disturb is on. Expanded "Alerts" shows one at a time with ‹ › through the last 20, Mute app and Clear.
/// Everything stays in memory; nothing is written to disk except the muted-app names.
/// </summary>
public partial class IslandWindow
{
    private static readonly string[] AvatarColors = ["#7B6CF6", "#4DA3FF", "#30D158", "#FF9F0A", "#FF8FB1", "#BF7BFF"];

    private readonly NotificationFeed _feed;
    private readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private INotificationSource? _notifications;
    private IslandNotification? _flash; // the toast currently flashing in the compact pill
    private int _alertIndex;            // 0 = newest

    private NotificationsConfig Notif => _config.Current.Modules.Notifications;
    public bool NotificationsEnabled => Notif.Enabled;
    public bool Dnd => Notif.Dnd;

    /// <summary>Why notifications can't be read (no permission, old Windows), or null.</summary>
    public string? NotificationProblem { get; private set; }

    private IslandNotification? CurrentAlert => _feed.Items.Count == 0 ? null : _feed.Items[Math.Clamp(_alertIndex, 0, _feed.Items.Count - 1)];

    private void InitAlerts()
    {
        _flashTimer.Tick += (_, _) => EndFlash();
        AlertPrev.Click += (_, _) => { _alertIndex--; RenderAlerts(); };
        AlertNext.Click += (_, _) => { _alertIndex++; RenderAlerts(); };
        MuteAppButton.Click += (_, _) => { if (CurrentAlert is { } n) MuteApp(n.App); };
        ClearAlertsButton.Click += (_, _) => { _feed.Clear(); EndFlash(); RenderExpanded(); };
        if (Notif.Enabled) _ = StartNotificationsAsync();
    }

    public async Task SetNotificationsEnabled(bool on)
    {
        Notif.Enabled = on;
        _config.SaveSoon();
        if (on) await StartNotificationsAsync();
        else { StopNotifications(); NotificationProblem = null; }
    }

    public void SetDnd(bool on)
    {
        Notif.Dnd = on;
        _config.SaveSoon();
    }

    public void UnmuteApp(string app)
    {
        Notif.Muted.RemoveAll(m => string.Equals(m, app, StringComparison.OrdinalIgnoreCase));
        _config.SaveSoon();
    }

    private async Task StartNotificationsAsync()
    {
        if (_notifications is not null) return;
        INotificationSource source = Notif.Source == "mock" ? new MockNotificationSource() : new ToastListenerSource();
        _notifications = source;
        source.Arrived += OnNotification;
        string? problem = await source.StartAsync();
        if (!ReferenceEquals(_notifications, source)) { source.Dispose(); return; } // turned off while starting
        NotificationProblem = problem;
        if (problem is not null) StopNotifications();
    }

    private void StopNotifications()
    {
        _notifications?.Dispose();
        _notifications = null;
    }

    private void OnNotification(IslandNotification n)
    {
        if (!_feed.Add(n)) return;
        _alertIndex = 0;
        _flash = n;
        _flashTimer.Stop();
        _flashTimer.Start();
        SetBaseExpression("surprised");
        RenderAlerts();
        RenderCompact();
        if (!Notif.Dnd && !_expanded && !_autoHiding && !Pill.IsMouseOver)
        {
            _picked = null; // show Alerts
            SetExpanded(true);
            _peekTimer.Stop();
            _peekTimer.Start(); // collapses after 3 s unless hovered
        }
        RenderExpanded();
    }

    private void EndFlash()
    {
        _flashTimer.Stop();
        if (_flash is null) return;
        _flash = null;
        if (_expanded && _view == View.Alerts) _picked = View.Alerts; // don't yank the view away while it's being read
        if (_baseExpression == "surprised" && _lastState != CodeState.NeedsYou) SetBaseExpression(IdleExpression);
        RenderCompact();
        RenderExpanded();
    }

    private void MuteApp(string app)
    {
        if (!Notif.Muted.Contains(app, StringComparer.OrdinalIgnoreCase)) Notif.Muted.Add(app);
        _config.SaveSoon();
        _feed.RemoveApp(app);
        if (string.Equals(_flash?.App, app, StringComparison.OrdinalIgnoreCase)) EndFlash();
        RenderAlerts();
        RenderExpanded();
        ((App)Application.Current).RefreshSettings();
    }

    private void RenderAlerts()
    {
        if (CurrentAlert is not { } n) return;
        _alertIndex = Math.Clamp(_alertIndex, 0, _feed.Items.Count - 1);
        SetIcon(AlertIcon, AlertIconBg, AlertLetter, n);
        AlertTitle.Text = n.Title;
        AlertBody.Text = n.Body;
        MuteAppButton.Content = "Mute " + n.App;
        AlertPos.Text = $"{_alertIndex + 1} / {_feed.Items.Count}";
        AlertPrev.IsEnabled = _alertIndex > 0;
        AlertNext.IsEnabled = _alertIndex < _feed.Items.Count - 1;
        TickAlerts();

        if (_flash is { } f)
        {
            SetIcon(NotifIcon, NotifIconBg, NotifLetter, f);
            NotifLineTitle.Text = f.Title;
            NotifLineBody.Text = "  " + f.Body.Replace('\n', ' ');
        }
    }

    /// <summary>"Chat · 5m": refreshed by the clock every second.</summary>
    private void TickAlerts()
    {
        if (CurrentAlert is { } n) AlertMeta.Text = $"{n.App} · {NotificationFeed.Ago(DateTimeOffset.Now - n.Time)}";
    }

    /// <summary>App logo when Windows has one, otherwise a coloured letter avatar.</summary>
    private static void SetIcon(Border icon, Border background, TextBlock letter, IslandNotification n)
    {
        icon.Background = n.Icon is { } img ? new ImageBrush(img) { Stretch = Stretch.Uniform } : null;
        letter.Text = n.Icon is null && n.App.Length > 0 ? n.App[..1].ToUpperInvariant() : "";
        background.Background = n.Icon is not null ? Brushes.Transparent
            : (Brush)new BrushConverter().ConvertFromString(AvatarColors[n.App.Sum(c => c) % AvatarColors.Length])!;
    }
}
