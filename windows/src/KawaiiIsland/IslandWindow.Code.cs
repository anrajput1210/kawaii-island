using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KawaiiIsland.Controls;
using KawaiiIsland.Services.ClaudeCode;

namespace KawaiiIsland;

/// <summary>
/// Code mode: shows Claude Code sessions on the island (what Claude is doing, context, plan usage left, cost).
/// Data arrives from Claude Code's own hooks/status line via the 127.0.0.1 HookServer and lives in memory only.
/// Installing the hooks is an explicit user action handled by App (tray toggle); starting the app never edits
/// Claude Code's settings.
/// </summary>
public partial class IslandWindow
{
    private static readonly Color Working = Color.FromRgb(0xD9, 0x77, 0x57), Waiting = Color.FromRgb(0xFF, 0xB3, 0x40),
                                  Finished = Color.FromRgb(0x30, 0xD1, 0x58), Quiet = Color.FromRgb(0x8E, 0x8E, 0x93);

    private readonly CodeTracker _tracker = new();
    private readonly DispatcherTimer _doneTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private HookServer? _server;
    private CodeState? _lastState;
    private string? _lastSessionId;
    private bool _doneShown; // "Done ✓" lingers 8 s in the compact pill, then the island goes quiet
    private (TextBlock Value, Border Fill, TextBlock Sub)[] _meters = [];
    private TaskCompletionSource<string?>? _approval; // pending Allow/Deny from a PermissionRequest hook
    private DateTime _lockedInSince; // coding mode = "lock in": hoodie + glasses, focus timer, no pop-open alerts

    public bool CodeMode => _server is not null;

    private void InitCodeMode()
    {
        _meters = [Meter("Context"), Meter("5-hour"), Meter("Week")];
        _tracker.Changed += OnCodeChanged;
        _doneTimer.Tick += (_, _) => { _doneTimer.Stop(); _doneShown = false; SetBaseExpression(IdleExpression); RenderCode(); };
        _peekTimer.Tick += (_, _) => { _peekTimer.Stop(); if (!Pill.IsMouseOver) SetExpanded(false); };
        foreach (var dot in new[] { CodeDot, CodeDotLarge })
            dot.BeginAnimation(OpacityProperty, Motion.Enabled
                ? new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                : null);
        AllowButton.Click += (_, _) => Answer("allow");
        DenyButton.Click += (_, _) => Answer("deny");
        TerminalButton.Click += (_, _) => Answer(null);
        CodeToggle.Click += (_, _) => ToggleCodeMode();
        CodeToggleSmall.Click += (_, _) => ToggleCodeMode();
        if (_config.Current.Modules.Code.Enabled) StartCodeMode();
        ApplyLockIn();
    }

    /// <summary>Shows Allow / Deny / Answer in terminal and waits (up to ApprovalSeconds) for a click.
    /// A newer request replaces an older one, which goes back to the terminal.</summary>
    private async Task<string?> AskApproval()
    {
        if (!_config.Current.Modules.Code.Approvals) return null;
        Answer(null);
        var pending = new TaskCompletionSource<string?>();
        _approval = pending;
        ApprovalRow.Visibility = Visibility.Visible;
        _picked = null;
        RenderExpanded();
        if (!_expanded) SetExpanded(true);
        _peekTimer.Stop(); // stays open until answered
        _autoCollapse.Stop();
        var done = await Task.WhenAny(pending.Task, Task.Delay(TimeSpan.FromSeconds(ClaudeSettings.ApprovalSeconds)));
        if (done != pending.Task) Answer(null);
        return await pending.Task;
    }

    private void Answer(string? behavior)
    {
        if (_approval is not { } pending) return;
        _approval = null;
        ApprovalRow.Visibility = Visibility.Collapsed;
        pending.TrySetResult(behavior);
        if (behavior is not null) ArmAutoCollapse();
    }

    private void ToggleCodeMode() => ((App)Application.Current).SetCodeMode(!CodeMode);

    /// <summary>Mascot outfit + toggle look follow Code mode.</summary>
    private void ApplyLockIn()
    {
        string outfit = CodeMode ? "code" : "";
        foreach (var m in Mascots) m.Outfit = outfit;
        PeekMascot.Outfit = outfit;
        foreach (var b in new[] { CodeToggle, CodeToggleSmall })
        {
            if (CodeMode) b.SetResourceReference(ForegroundProperty, "Accent"); else b.ClearValue(ForegroundProperty);
            b.ToolTip = CodeMode ? "Coding mode is on (locked in) · click to turn off" : "Turn on coding mode (lock in)";
        }
        RenderCompact();
    }

    private string LockInText()
    {
        var t = DateTime.Now - _lockedInSince;
        return "Locked in " + (t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{(int)t.TotalMinutes}m");
    }

    /// <summary>Starts the local listener. Returns an error message, or null on success.</summary>
    public string? StartCodeMode()
    {
        if (_server is not null) return null;
        int port = _config.Current.Modules.Code.Port;
        var server = new HookServer(port);
        server.Hook += e => Dispatcher.BeginInvoke(() => _tracker.OnHook(e, DateTimeOffset.Now));
        server.Status = e => Dispatcher.Invoke(() =>
        {
            _tracker.OnStatus(e, DateTimeOffset.Now);
            return _tracker.StatusLine(e.TryGetProperty("session_id", out var id) ? id.GetString() ?? "" : "");
        });
        server.Permission = async e =>
        {
            var answer = await Dispatcher.InvokeAsync(() => AskApproval()).Task.Unwrap();
            return ClaudeSettings.PermissionReply(answer);
        };
        try { server.Start(); }
        catch (SocketException ex)
        {
            server.Dispose();
            return $"Port {port} is already in use ({ex.SocketErrorCode}). Change modules.code.port in config.json.";
        }
        _server = server;
        _lockedInSince = DateTime.Now;
        ApplyLockIn();
        RenderCode();
        return null;
    }

    public void StopCodeMode()
    {
        Answer(null);
        _server?.Dispose();
        _server = null;
        _doneShown = false;
        SetBaseExpression(IdleExpression);
        ApplyLockIn();
        RenderCode();
    }

    private void OnCodeChanged()
    {
        var s = _tracker.Active;
        if (s is not null && (s.State != _lastState || s.Id != _lastSessionId)) OnTransition(s.State);
        _lastState = s?.State;
        _lastSessionId = s?.Id;
        RenderCode();
    }

    /// <summary>Mascot + island reactions when the active session changes state.</summary>
    private void OnTransition(CodeState state)
    {
        _doneTimer.Stop();
        _doneShown = state == CodeState.Done;
        switch (state)
        {
            case CodeState.NeedsYou:
                _picked = null; // show the Code view
                SetBaseExpression("surprised");
                if (!_expanded) { SetExpanded(true); _peekTimer.Stop(); _peekTimer.Start(); }
                break;
            case CodeState.Done:
                SetBaseExpression("happy");
                foreach (var m in Mascots) m.Hop();
                _doneTimer.Start();
                break;
            case CodeState.Thinking or CodeState.Tool:
                SetBaseExpression("wow");
                break;
            default:
                SetBaseExpression(IdleExpression);
                break;
        }
    }

    private void RenderCode()
    {
        bool on = _server is not null;
        var s = on ? _tracker.Active : null;
        bool busy = s is { State: CodeState.Thinking or CodeState.Tool or CodeState.NeedsYou } || (s?.State == CodeState.Done && _doneShown);

        // compact pill
        _codeBusy = busy;
        RenderCompact();
        var color = CodeColor(s?.State);
        CodeDot.Fill = CodeDotLarge.Fill = new SolidColorBrush(color);
        CodeLine.Text = s?.State switch
        {
            CodeState.Thinking => "Thinking…",
            CodeState.Tool => s.Detail,
            CodeState.NeedsYou => "Needs you",
            CodeState.Done => "Done ✓",
            _ => "",
        };
        Clock.Text = busy && _tracker.FiveHour is { } h ? $"5h {h.UsedPct:0}%"
                   : busy && s?.ContextPct is { } c ? $"ctx {c:0}%"
                   : DateTime.Now.ToString("t");

        // expanded panel
        RenderExpanded();
        if (!on) return;

        CodeTitle.Text = s is null ? "Locked in · Claude Code" : $"Claude Code · {s.Project}";
        CodeStateText.Text = s?.State switch
        {
            null => "Waiting for a session (restart Claude Code after turning Code mode on)",
            CodeState.Thinking => "Thinking…",
            CodeState.Tool => "Running " + s.Detail,
            CodeState.NeedsYou => s.Detail.Length > 0 ? s.Detail : "Waiting for you",
            CodeState.Done => "Finished — your turn",
            _ => "Ready",
        };
        SetMeter(_meters[0], s?.ContextPct, s?.Model ?? "");
        SetMeter(_meters[1], _tracker.FiveHour?.UsedPct, _tracker.FiveHour is { } f ? "resets " + f.ResetsAt.ToLocalTime().ToString("t") : "");
        SetMeter(_meters[2], _tracker.Week?.UsedPct, _tracker.Week is { } w ? "resets " + w.ResetsAt.ToLocalTime().ToString("ddd h tt") : "");
        TickCodeFooter();
    }

    /// <summary>Refreshed every second by the clock so the focus timer moves.</summary>
    private void TickCodeFooter()
    {
        if (!CodeMode) return;
        var s = _tracker.Active;
        int n = _tracker.Sessions.Count;
        CodeFooter.Text = LockInText() + " · " + (s?.CostUsd is { } cost ? $"${cost:0.00} this session · " : "") + (n == 1 ? "1 session" : $"{n} sessions");
    }

    private static Color CodeColor(CodeState? state) => state switch
    {
        CodeState.Thinking or CodeState.Tool => Working,
        CodeState.NeedsYou => Waiting,
        CodeState.Done => Finished,
        _ => Quiet,
    };

    private (TextBlock, Border, TextBlock) Meter(string label)
    {
        var title = Text(label, 11, "IslandMuted");
        var value = Text("—", 11, "IslandText");
        value.HorizontalAlignment = HorizontalAlignment.Right;
        var head = new Grid(); head.Children.Add(title); head.Children.Add(value);

        var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 5, 0, 4), Child = fill };
        track.SetResourceReference(Border.BackgroundProperty, "IslandTrack"); // follows dark/light theme
        var sub = Text("", 10, "IslandMuted");
        sub.TextTrimming = TextTrimming.CharacterEllipsis;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        panel.Children.Add(head); panel.Children.Add(track); panel.Children.Add(sub);
        CodeMeters.Children.Add(panel);
        track.SizeChanged += (_, _) => fill.Width = track.ActualWidth * ((double?)fill.Tag ?? 0) / 100;
        return (value, fill, sub);
    }

    private static void SetMeter((TextBlock Value, Border Fill, TextBlock Sub) m, double? pct, string sub)
    {
        m.Value.Text = pct is { } p ? $"{p:0}%" : "—";
        m.Sub.Text = sub;
        double v = Math.Clamp(pct ?? 0, 0, 100);
        m.Fill.Tag = v;
        m.Fill.Background = new SolidColorBrush(v >= 90 ? Color.FromRgb(0xFF, 0x45, 0x3A) : v >= 75 ? Waiting : Color.FromRgb(0xFF, 0x8F, 0xB1));
        if (m.Fill.Parent is Border track) m.Fill.Width = track.ActualWidth * v / 100;
    }

    private static TextBlock Text(string text, double size, string brushKey)
    {
        var t = new TextBlock { Text = text, FontSize = size };
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey); // DynamicResource: re-themes live
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFont");
        return t;
    }
}
