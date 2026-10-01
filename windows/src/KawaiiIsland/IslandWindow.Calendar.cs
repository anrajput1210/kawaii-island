using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Mail;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

/// <summary>
/// Calendar tab: today's date with month progress, events from the calendar of the account signed in for mail
/// (Google Calendar or Outlook), and the user's own tasks, merged in time order. Upcoming events get a heads-up
/// 10 minutes before. System tab: CPU / memory / disk / network and Lock · Sleep · Restart · Shut down.
/// (Tasks, month progress, system monitor and power hub are features from the Python "dynamic-island-for-windows".)
/// </summary>
public partial class IslandWindow
{
    private readonly DispatcherTimer _calendarTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SystemStats _stats = new();
    private readonly HashSet<string> _remindedEvents = [];
    private List<CalendarItem> _events = [];
    private (TextBlock Value, Border Fill)[] _systemMeters = [];

    private bool CalendarOn => Widgets.Calendar;
    private bool SystemOn => Widgets.ShowSystem;

    private void InitCalendar()
    {
        TabCalendar.Click += (_, _) => Pick(View.Calendar);
        TabSystem.Click += (_, _) => Pick(View.System);
        AddTaskButton.Click += (_, _) => AddTask();
        _calendarTimer.Tick += async (_, _) => await RefreshCalendarAsync();
        _calendarTimer.Start();
        _systemTimer.Tick += (_, _) => { if (_expanded && _view == View.System) RenderSystem(); };
        _systemTimer.Start();
        _systemMeters = [SystemMeter("CPU"), SystemMeter("Memory"), SystemMeter("Disk"), SystemMeter("Network")];
        LockButton.Click += (_, _) => SystemStats.Lock();
        SleepButton.Click += (_, _) => SystemStats.Sleep();
        RestartButton.Click += (_, _) => Confirm("Restart your PC now?", SystemStats.Restart);
        ShutDownButton.Click += (_, _) => Confirm("Shut down your PC now?", SystemStats.ShutDown);
        _ = RefreshCalendarAsync();
    }

    private static void Confirm(string question, Action action)
    {
        if (MessageBox.Show(question + " Unsaved work in other apps may be lost.", "Kawaii Island",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            action();
    }

    // ---------------- calendar ----------------

    /// <summary>Events from Google Calendar or Outlook, whichever account is signed in for mail.</summary>
    public async Task RefreshCalendarAsync()
    {
        if (CalendarOn && OAuthProvider.For(MailCfg.Provider) is { } provider && OAuth.Load(_config.Directory) is { } account && account.Provider == provider.Key)
        {
            try
            {
                var (id, secret) = ClientFor(provider);
                string token = await OAuth.AccessTokenAsync(provider, account, id, secret, _config.Directory, CancellationToken.None, provider.CalendarScope);
                _events = await CalendarService.FetchAsync(provider, token, DateTimeOffset.Now);
            }
            catch (Exception ex) when (ex is HttpRequestException or OAuthException or TaskCanceledException or System.Text.Json.JsonException)
            {
                App.Log.LogInformation("Calendar unavailable: {Message}", ex.Message); // keep the last list
            }
        }
        else _events = [];
        RenderCalendar();
    }

    private IEnumerable<CalendarItem> Agenda(DateTimeOffset now)
    {
        var tasks = Widgets.Tasks.Select((t, i) => new CalendarItem(t.Name, CalendarService.TaskTime(t.Time, now.Date), false,
                                                                    t.Done ? "#555559" : "#30D158", true, "task:" + i));
        return _events.Where(e => e.Start is null || e.AllDay || e.Start > now.AddMinutes(-30))
                      .Concat(tasks)
                      .OrderBy(e => e.Start ?? DateTimeOffset.MaxValue)
                      .Take(5);
    }

    private void RenderCalendar()
    {
        var now = DateTimeOffset.Now;
        DayText.Text = now.ToString("dddd, MMMM d");
        double progress = CalendarService.MonthProgress(now.DateTime);
        MonthText.Text = $"{progress:P0} of {now:MMMM}";
        MonthFill.Width = Math.Max(0, (ExpandedPanel.Width - 36) * progress);

        AgendaList.Children.Clear();
        foreach (var item in Agenda(now))
            AgendaList.Children.Add(AgendaRow(item, now));
        if (AgendaList.Children.Count == 0)
        {
            string hint = OAuth.Load(_config.Directory) is null
                ? "Nothing planned. Sign in with Google or Outlook in Settings → Mail to see your calendar."
                : "Nothing else today.";
            var empty = Text(hint, 12, "IslandMuted");
            empty.TextWrapping = TextWrapping.Wrap;
            AgendaList.Children.Add(empty);
        }
        RenderExpanded();
    }

    /// <summary>"● 3:00 PM  Standup" — coloured dot (blue events, green tasks), time, title. Click a task to tick it off.</summary>
    private FrameworkElement AgendaRow(CalendarItem item, DateTimeOffset now)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 3), Background = Brushes.Transparent };
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
                                Fill = (Brush)new BrushConverter().ConvertFromString(item.Color)! };
        row.Children.Add(dot);
        string when = item.AllDay ? "All day"
                    : item.Start is { } s ? (s.Date == now.Date ? s.ToLocalTime().ToString("t") : "Tomorrow " + s.ToLocalTime().ToString("t"))
                    : "";
        var time = Text(when, 12, "IslandMuted");
        time.Width = item.Start is { } st && st.Date != now.Date ? 110 : 64;
        time.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(time);
        var title = Text(item.Title, 12.5, "IslandText");
        title.FontWeight = FontWeights.Medium;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        if (item.IsTask && Widgets.Tasks.ElementAtOrDefault(int.Parse(item.Id[5..])) is { Done: true })
            title.TextDecorations = TextDecorations.Strikethrough;
        row.Children.Add(title);

        if (item.IsTask)
        {
            int index = int.Parse(item.Id[5..]);
            row.Cursor = Cursors.Hand;
            row.ToolTip = "Click to tick off · right-click to remove";
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Widgets.Tasks[index].Done = !Widgets.Tasks[index].Done; _config.SaveSoon(); RenderCalendar(); };
            row.MouseRightButtonUp += (_, e) => { e.Handled = true; Widgets.Tasks.RemoveAt(index); _config.SaveSoon(); RenderCalendar(); };
        }
        return row;
    }

    private void AddTask()
    {
        if (TextPrompt.Show("Add a task (e.g. \"Gym 5:30 PM\")", "") is not { Length: > 0 } text) return;
        var (name, time) = CalendarService.SplitTask(text);
        Widgets.Tasks.Add(new TaskItem { Name = name, Time = time });
        _config.SaveSoon();
        RenderCalendar();
    }

    /// <summary>Called by the live tick: a heads-up 10 minutes before an event or timed task (once each).</summary>
    private void CheckUpcoming(DateTimeOffset now)
    {
        if (!CalendarOn) return;
        foreach (var item in Agenda(now))
        {
            if (item.AllDay || item.Start is not { } start || (item.IsTask && Widgets.Tasks.ElementAtOrDefault(int.Parse(item.Id[5..])) is { Done: true })) continue;
            var left = start - now;
            if (left <= TimeSpan.Zero || left > TimeSpan.FromMinutes(10) || !_remindedEvents.Add(item.Id + start.ToString("o"))) continue;
            ShowHud("", Color.FromRgb(0xFF, 0x45, 0x3A), item.Title, $"in {Math.Max(1, (int)Math.Round(left.TotalMinutes))} min", null, TimeSpan.FromSeconds(8));
        }
    }

    // ---------------- system ----------------

    private (TextBlock, Border) SystemMeter(string label)
    {
        var name = Text(label, 11, "IslandMuted");
        var value = Text("—", 15, "IslandText");
        value.FontWeight = FontWeights.SemiBold;
        var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = new SolidColorBrush(Accent()) };
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 5, 0, 0), Child = fill };
        track.SetResourceReference(Border.BackgroundProperty, "IslandTrack");
        SystemMeters.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 14, 0), Children = { name, value, track } });
        return (value, fill);
    }

    private void RenderSystem()
    {
        var s = _stats.Read();
        void Set(int i, string text, double pct)
        {
            var (value, fill) = _systemMeters[i];
            value.Text = text;
            // Same semantic colours as the Windhawk mod: accent while comfortable, amber under pressure, red when full.
            fill.Background = new SolidColorBrush(pct >= 90 ? Color.FromRgb(0xFF, 0x45, 0x3A) : pct >= 75 ? Color.FromRgb(0xFF, 0xB3, 0x40) : Accent());
            if (fill.Parent is Border track) fill.Width = track.ActualWidth * Math.Clamp(pct, 0, 100) / 100;
        }
        Set(0, $"{s.Cpu:0}%", s.Cpu);
        Set(1, $"{s.Ram:0}%", s.Ram);
        Set(2, $"{s.Disk:0}%", s.Disk);
        Set(3, "↓ " + SystemStats.Rate(s.DownBps), 0);
        _systemMeters[3].Value.ToolTip = "Up " + SystemStats.Rate(s.UpBps);
    }
}
