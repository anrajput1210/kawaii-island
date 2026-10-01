using System.IO;
using System.Text.Json;

namespace KawaiiIsland.Services.ClaudeCode;

public enum CodeState { Idle, Thinking, Tool, NeedsYou, Done }

public sealed class CodeSession
{
    public required string Id { get; init; }
    public string Project { get; set; } = "";
    public CodeState State { get; set; } = CodeState.Idle;
    /// <summary>"Bash · npm test", "Allow Edit · App.cs?", a notification message…</summary>
    public string Detail { get; set; } = "";
    public string? Model { get; set; }
    public double? ContextPct { get; set; }
    public double? CostUsd { get; set; }
    public DateTimeOffset Updated { get; set; }
}

/// <summary>Plan usage windows from the status line (claude.ai Pro/Max only; null when Claude Code doesn't send them).</summary>
public sealed record UsageWindow(double UsedPct, DateTimeOffset ResetsAt);

/// <summary>
/// Turns Claude Code hook events and status-line snapshots (both documented JSON, posted to the local HookServer)
/// into per-session state. Pure: no IO, no UI, everything stays in memory. Unit-tested.
/// </summary>
public sealed class CodeTracker
{
    private readonly Dictionary<string, CodeSession> _sessions = new();

    public IReadOnlyCollection<CodeSession> Sessions => _sessions.Values;
    /// <summary>The session with the most recent activity.</summary>
    public CodeSession? Active => _sessions.Values.MaxBy(s => s.Updated);
    public UsageWindow? FiveHour { get; private set; }
    public UsageWindow? Week { get; private set; }

    public event Action? Changed;

    public void OnHook(JsonElement e, DateTimeOffset now)
    {
        string id = Str(e, "session_id"), evt = Str(e, "hook_event_name");
        if (id.Length == 0) return;
        if (evt == "SessionEnd")
        {
            if (_sessions.Remove(id)) Changed?.Invoke();
            return;
        }

        var s = Get(id, e);
        switch (evt)
        {
            case "SessionStart": Set(s, CodeState.Idle, ""); if (Str(e, "model") is { Length: > 0 } m) s.Model = m; break;
            case "UserPromptSubmit": Set(s, CodeState.Thinking, ""); break;
            case "PreToolUse": Set(s, CodeState.Tool, Describe(e)); break;
            case "PostToolUse" or "PostToolUseFailure": Set(s, CodeState.Thinking, ""); break;
            case "PermissionRequest": Set(s, CodeState.NeedsYou, $"Allow {Describe(e)}?"); break;
            case "Notification": Set(s, CodeState.NeedsYou, Str(e, "message")); break;
            case "Stop": Set(s, CodeState.Done, ""); break;
            default: return; // events we don't show
        }
        s.Updated = now;
        Changed?.Invoke();
    }

    /// <summary>Status-line snapshot: model, context %, cost for the session; 5-hour / 7-day plan usage for the account.</summary>
    public void OnStatus(JsonElement e, DateTimeOffset now)
    {
        if (Str(e, "session_id") is { Length: > 0 } id)
        {
            var s = Get(id, e);
            if (Str(e, "model", "display_name") is { Length: > 0 } model) s.Model = model;
            s.ContextPct = Num(e, "context_window", "used_percentage") ?? s.ContextPct;
            s.CostUsd = Num(e, "cost", "total_cost_usd") ?? s.CostUsd;
            if (s.Updated == default) s.Updated = now;
        }
        FiveHour = Window(e, "five_hour");
        Week = Window(e, "seven_day");
        Changed?.Invoke();
    }

    /// <summary>Text Claude Code shows in its status bar, e.g. "🏝 ctx 23% · 5h 41% · wk 12%".</summary>
    public string StatusLine(string sessionId)
    {
        var parts = new List<string>();
        if (_sessions.TryGetValue(sessionId, out var s) && s.ContextPct is { } ctx) parts.Add($"ctx {ctx:0}%");
        if (FiveHour is { } h) parts.Add($"5h {h.UsedPct:0}%");
        if (Week is { } w) parts.Add($"wk {w.UsedPct:0}%");
        return parts.Count == 0 ? "🏝" : "🏝 " + string.Join(" · ", parts);
    }

    private CodeSession Get(string id, JsonElement e)
    {
        if (!_sessions.TryGetValue(id, out var s)) _sessions[id] = s = new CodeSession { Id = id };
        if (Str(e, "cwd") is { Length: > 0 } cwd) s.Project = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        return s;
    }

    private static void Set(CodeSession s, CodeState state, string detail) { s.State = state; s.Detail = detail; }

    /// <summary>Short human label for a tool call: the tool plus its most telling argument.</summary>
    internal static string Describe(JsonElement e)
    {
        string tool = Str(e, "tool_name");
        string arg = tool switch
        {
            "Bash" or "PowerShell" => Str(e, "tool_input", "command"),
            "Edit" or "Write" or "Read" or "MultiEdit" or "NotebookEdit" => Path.GetFileName(Str(e, "tool_input", "file_path")),
            "Grep" or "Glob" => Str(e, "tool_input", "pattern"),
            "WebFetch" => Uri.TryCreate(Str(e, "tool_input", "url"), UriKind.Absolute, out var u) ? u.Host : "",
            "Task" or "Agent" => Str(e, "tool_input", "description"),
            _ => "",
        };
        arg = arg.ReplaceLineEndings(" ").Trim();
        if (arg.Length > 40) arg = arg[..39] + "…";
        return arg.Length == 0 ? tool : $"{tool} · {arg}";
    }

    private static UsageWindow? Window(JsonElement e, string name) =>
        Num(e, "rate_limits", name, "used_percentage") is { } pct && Num(e, "rate_limits", name, "resets_at") is { } at
            ? new UsageWindow(pct, DateTimeOffset.FromUnixTimeSeconds((long)at))
            : null;

    private static JsonElement? Find(JsonElement e, string[] path)
    {
        foreach (var key in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e)) return null;
        }
        return e;
    }

    private static string Str(JsonElement e, params string[] path) =>
        Find(e, path) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";

    private static double? Num(JsonElement e, params string[] path) =>
        Find(e, path) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
}
