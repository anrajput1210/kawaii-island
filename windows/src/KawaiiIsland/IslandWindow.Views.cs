using System.Windows;

namespace KawaiiIsland;

/// <summary>
/// Which content the island shows. Compact pill priority: Claude working › new notification › music playing › mascot + clock.
/// Expanded: Home (clock + greeting), Music, Code or Alerts; tabs appear only when more than one module has something to show.
/// </summary>
public partial class IslandWindow
{
    private enum View { Home, Music, Code, Alerts }

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
        bool flash = !_codeBusy && _flash is not null;
        bool music = !_codeBusy && !flash && _media is { Playing: true };
        SetCompact(_codeBusy || flash || music);
        CodeLinePanel.Visibility = Vis(_codeBusy);
        NotifLinePanel.Visibility = Vis(flash);
        MusicLinePanel.Visibility = Eq.Visibility = Vis(music);
        MascotSmall.Visibility = Vis(!music);
        MascotSmall.HorizontalAlignment = flash ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Clock.Visibility = Vis(!music && !flash);
    }

    private void RenderExpanded()
    {
        bool code = CodeMode, music = _media is not null, alerts = _feed.Items.Count > 0;
        _view = _picked switch
        {
            View.Code when code => View.Code,
            View.Music when music => View.Music,
            View.Alerts when alerts => View.Alerts,
            _ when code && _codeBusy => View.Code,
            _ when _flash is not null => View.Alerts,
            _ when music && _media!.Playing => View.Music,
            _ => code ? View.Code : music ? View.Music : alerts ? View.Alerts : View.Home,
        };
        bool tabs = (code ? 1 : 0) + (music ? 1 : 0) + (alerts ? 1 : 0) > 1;
        bool header = _view is View.Music or View.Alerts;
        TabRow.Visibility = Vis(tabs || header);
        Tabs.Visibility = Vis(tabs);
        TabMusic.Visibility = Vis(music);
        TabCode.Visibility = Vis(code);
        TabAlerts.Visibility = Vis(alerts);
        TabMusic.IsChecked = _view == View.Music;
        TabCode.IsChecked = _view == View.Code;
        TabAlerts.IsChecked = _view == View.Alerts;
        MascotTiny.Visibility = SmallClock.Visibility = Vis(header);
        MainRow.Visibility = Vis(!header);
        ClockBlock.Visibility = Greeting.Visibility = Vis(_view == View.Home);
        CodeHeader.Visibility = CodeMeters.Visibility = CodeFooter.Visibility = Vis(_view == View.Code);
        MusicPanel.Visibility = Vis(_view == View.Music);
        AlertsPanel.Visibility = Vis(_view == View.Alerts);
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
