using System.Windows;
using System.Windows.Media;

namespace KawaiiIsland;

/// <summary>
/// Which content the island shows. Compact pill priority: Claude working › new notification › music playing › mascot + clock.
/// Expanded: Home (clock + greeting), Music, Code or Alerts; tabs appear only when more than one module has something to show.
/// </summary>
public partial class IslandWindow
{
    private enum View { Home, Music, Code, Alerts, Mail, Apps }

    private View _view = View.Home;
    private View? _picked;   // tab the user chose; null = automatic
    private bool _codeBusy;  // Code mode owns the compact pill while Claude is working (set by RenderCode)

    private void InitViews()
    {
        TabMusic.Click += (_, _) => Pick(View.Music);
        TabCode.Click += (_, _) => Pick(View.Code);
        TabAlerts.Click += (_, _) => Pick(View.Alerts);
    }

    private void Pick(View view)
    {
        _picked = view;
        RenderExpanded();
        TickMusic();
    }

    private void RenderCompact()
    {
        bool dropping = _dropText is not null; // file dragged over: mascot box + "Feed me!" wins
        bool code = _codeBusy && !dropping;
        bool flash = !code && !dropping && _flash is not null;
        bool music = !code && !flash && !dropping && _media is { Playing: true };
        bool minimal = SideDocked; // 44 px circle: one live thing (EQ while music plays, else the mascot)
        SetCompact(!minimal && (code || flash || music));
        CollapsedPanel.Margin = new Thickness(minimal ? 0 : 11, 0, minimal ? 0 : 11, 0);
        CodeLinePanel.Visibility = Vis(code && !minimal);
        NotifLinePanel.Visibility = Vis(flash && !minimal);
        MusicLinePanel.Visibility = Vis(music && !minimal);
        Eq.Visibility = Vis(music);
        Eq.HorizontalAlignment = minimal ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        MascotSmall.Visibility = Vis(!music && (HasMascot || dropping));
        MascotSmall.HorizontalAlignment = minimal ? HorizontalAlignment.Center : flash ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Clock.Visibility = Vis(!minimal && !music && !flash);
        MailBadge.Visibility = Vis(!minimal && !music && !flash && !code && !dropping && Unread > 0);

        // Key line takes the tint of the active content (design guidelines: "Shell").
        Color? tint = code ? CodeColor(_tracker.Active?.State)
                    : flash ? AvatarColor(_flash!.App)
                    : music ? MusicTint
                    : Unread > 0 ? MailTint
                    : CodeMode ? Accent()
                    : null;
        Pill.BorderBrush = new SolidColorBrush(tint is { } t ? Color.FromArgb(0xB0, t.R, t.G, t.B) : Color.FromArgb(0x26, 0x80, 0x80, 0x80));
    }

    private static readonly Color MusicTint = Color.FromRgb(0x30, 0xD1, 0x58);

    private void RenderExpanded()
    {
        bool code = CodeMode, music = _media is not null, alerts = _feed.Items.Count > 0, mail = _mail is not null, apps = AppsOn;
        _view = _picked switch
        {
            View.Code when code => View.Code,
            View.Music when music => View.Music,
            View.Alerts when alerts => View.Alerts,
            View.Mail when mail => View.Mail,
            View.Apps when apps => View.Apps,
            _ when code && _codeBusy => View.Code,
            _ when _flash is not null => View.Alerts,
            _ when music && _media!.Playing => View.Music,
            _ => code ? View.Code : music ? View.Music : Unread > 0 ? View.Mail : alerts ? View.Alerts : View.Home,
        };
        bool tabs = (code ? 1 : 0) + (music ? 1 : 0) + (alerts ? 1 : 0) + (mail ? 1 : 0) + (apps ? 1 : 0) > 1;
        bool header = _view is View.Music or View.Alerts or View.Mail or View.Apps;
        TabRow.Visibility = Vis(tabs || header);
        Tabs.Visibility = Vis(tabs);
        TabMusic.Visibility = Vis(music);
        TabCode.Visibility = Vis(code);
        TabAlerts.Visibility = Vis(alerts);
        TabMusic.IsChecked = _view == View.Music;
        TabCode.IsChecked = _view == View.Code;
        TabAlerts.IsChecked = _view == View.Alerts;
        TabMail.Visibility = Vis(mail);
        TabMail.IsChecked = _view == View.Mail;
        TabApps.Visibility = Vis(apps);
        TabApps.IsChecked = _view == View.Apps;
        AppsPanel.Visibility = Vis(_view == View.Apps || (_view == View.Home && apps)); // Home: clock + your apps
        MailPanel.Visibility = Vis(_view == View.Mail);
        MascotTiny.Visibility = SmallClock.Visibility = Vis(header);
        MainRow.Visibility = Vis(!header);
        ClockBlock.Visibility = Vis(_view == View.Home);
        Greeting.Visibility = Vis(_view == View.Home && !apps);
        CodeHeader.Visibility = CodeMeters.Visibility = CodeFooter.Visibility = Vis(_view == View.Code);
        MusicPanel.Visibility = Vis(_view == View.Music);
        AlertsPanel.Visibility = Vis(_view == View.Alerts);
        FitExpanded();
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
