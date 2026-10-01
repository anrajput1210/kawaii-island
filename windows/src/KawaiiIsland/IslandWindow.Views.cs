using System.Windows;
using System.Windows.Media;

namespace KawaiiIsland;

/// <summary>
/// Which content the island shows. Compact pill priority: Claude working › new notification › music playing › mascot + clock.
/// Expanded: Home (clock + greeting), Music, Code or Alerts; tabs appear only when more than one module has something to show.
/// </summary>
public partial class IslandWindow
{
    private enum View { Home, Music, Code, Alerts, Apps, Timer, Calendar, System }

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
        bool hud = !dropping && _hud is not null; // brief live alert (volume, charging, Bluetooth, Caps Lock)
        bool code = _codeBusy && !dropping && !hud;
        bool flash = !code && !dropping && !hud && _flash is not null;
        bool timer = !code && !flash && !dropping && !hud && _timer.Active;
        bool music = !code && !flash && !dropping && !hud && !timer && _media is { Playing: true };
        bool minimal = SideDocked; // 44 px circle: one live thing (EQ while music plays, else the mascot)
        SetCompact(!minimal && (code || flash || music || hud || timer));
        HudPanel.Visibility = Vis(hud && !minimal);
        TimerLinePanel.Visibility = Vis(timer && !minimal);

        // Apple "minimal": a second ongoing activity gets the detached circle beside the pill.
        var ongoing = new List<string>();
        if (_codeBusy) ongoing.Add("code");
        if (_timer.Active) ongoing.Add("timer");
        if (_media is { Playing: true }) ongoing.Add("music");
        bool primaryOngoing = code || timer || music;
        string? second = primaryOngoing ? ongoing.Skip(1).FirstOrDefault() : (hud || flash) ? ongoing.FirstOrDefault() : null;
        var (cw, ch) = CollapsedSize();
        RenderBubble(second, cw, ch);
        CollapsedPanel.Margin = new Thickness(minimal ? 0 : 11, 0, minimal ? 0 : 11, 0);
        CodeLinePanel.Visibility = Vis(code && !minimal);
        NotifLinePanel.Visibility = Vis(flash && !minimal);
        MusicLinePanel.Visibility = Vis(music && !minimal);
        Eq.Visibility = Vis(music);
        Eq.HorizontalAlignment = minimal ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        MascotSmall.Visibility = Vis(!music && !hud && !timer && (HasMascot || dropping));
        MascotSmall.HorizontalAlignment = minimal ? HorizontalAlignment.Center : flash ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        bool rest = !minimal && !music && !flash && !code && !dropping && !hud && !timer; // plain resting pill: user-chosen widgets
        Clock.Visibility = Vis(!minimal && !music && !flash && !hud && !timer && (Widgets.PillClock || code || dropping));
        PillWeather.Visibility = Vis(rest && Widgets.PillWeather && _weather is not null);
        PillBattery.Visibility = Vis(rest && Widgets.PillBattery && _battery is not null);

        // Key line takes the tint of the active content (design guidelines: "Shell").
        Color? tint = hud ? _hud!.Tint == Colors.White ? null : _hud.Tint
                    : timer ? TimerOrange
                    : code ? CodeColor(_tracker.Active?.State)
                    : flash ? AvatarColor(_flash!.App)
                    : music ? MusicTint
                    : CodeMode ? Accent()
                    : null;
        // Subtle key line (Apple): a faint hint of the activity colour, otherwise an almost invisible hairline.
        Pill.BorderBrush = new SolidColorBrush(tint is { } t ? Color.FromArgb(0x38, t.R, t.G, t.B) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    private static readonly Color MusicTint = Color.FromRgb(0x30, 0xD1, 0x58);

    private void RenderExpanded()
    {
        bool code = CodeMode, music = _media is not null, alerts = _feed.Items.Count > 0, apps = AppsOn, timer = _timer.Active,
             calendar = CalendarOn, system = SystemOn;
        _view = _picked switch
        {
            View.Code when code => View.Code,
            View.Music when music => View.Music,
            View.Alerts when alerts => View.Alerts,
            View.Apps when apps => View.Apps,
            View.Timer when timer => View.Timer,
            View.Calendar when calendar => View.Calendar,
            View.System when system => View.System,
            _ when code && _codeBusy => View.Code,
            _ when timer => View.Timer,
            _ when _flash is not null => View.Alerts,
            _ when music && _media!.Playing => View.Music,
            _ => code ? View.Code : music ? View.Music : alerts ? View.Alerts : View.Home,
        };
        bool tabs = (code ? 1 : 0) + (music ? 1 : 0) + (alerts ? 1 : 0) + (apps ? 1 : 0) + (timer ? 1 : 0)
                    + (calendar ? 1 : 0) + (system ? 1 : 0) > 1;
        bool header = _view is View.Music or View.Alerts or View.Apps or View.Timer or View.Calendar or View.System;
        TabCalendar.Visibility = Vis(calendar);
        TabCalendar.IsChecked = _view == View.Calendar;
        TabSystem.Visibility = Vis(system);
        TabSystem.IsChecked = _view == View.System;
        CalendarPanel.Visibility = Vis(_view == View.Calendar);
        SystemPanel.Visibility = Vis(_view == View.System);
        if (_view == View.System && _expanded) RenderSystem();
        TabTimer.Visibility = Vis(timer);
        TabTimer.IsChecked = _view == View.Timer;
        TimerPanel.Visibility = Vis(_view == View.Timer);
        TabRow.Visibility = Vis(tabs || header);
        Tabs.Visibility = Vis(tabs);
        TabMusic.Visibility = Vis(music);
        TabCode.Visibility = Vis(code);
        TabAlerts.Visibility = Vis(alerts);
        TabMusic.IsChecked = _view == View.Music;
        TabCode.IsChecked = _view == View.Code;
        TabAlerts.IsChecked = _view == View.Alerts;
        TabApps.Visibility = Vis(apps);
        TabApps.IsChecked = _view == View.Apps;
        AppsPanel.Visibility = Vis(_view == View.Apps || (_view == View.Home && apps)); // Home: clock + your apps
        MascotTiny.Visibility = SmallClock.Visibility = CodeToggleSmall.Visibility = Vis(header); // one </> button per view
        MainRow.Visibility = Vis(!header);
        ClockBlock.Visibility = Vis(_view == View.Home && Widgets.HomeClock);
        HomeWidgets.Visibility = Vis(_view == View.Home && HomeWidgets.Children.Count > 0);
        Greeting.Visibility = Vis(_view == View.Home && !apps);
        CodeHeader.Visibility = CodeFooter.Visibility = Vis(_view == View.Code);
        CodeMeters.Visibility = Vis(_view == View.Code && _tracker.Active?.Agent is null or "Claude Code"); // usage meters are Claude-only
        MusicPanel.Visibility = Vis(_view == View.Music);
        AlertsPanel.Visibility = Vis(_view == View.Alerts);
        PickerPanel.Visibility = Visibility.Collapsed;
        if (_picking) foreach (UIElement child in ExpandedItems.Children) child.Visibility = Vis(child == PickerPanel); // Add apps replaces the view
        FitExpanded();
    }

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
