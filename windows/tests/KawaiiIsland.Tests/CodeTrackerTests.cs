using System.Text.Json;
using KawaiiIsland.Services.ClaudeCode;

namespace KawaiiIsland.Tests;

public sealed class CodeTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;
    private static JsonElement Hook(string evt, string extra = "", string session = "s1") =>
        J($$"""{ "session_id": "{{session}}", "cwd": "C:\\code\\my-app", "hook_event_name": "{{evt}}" {{extra}} }""");

    [Fact]
    public void Session_lifecycle_maps_to_states()
    {
        var t = new CodeTracker();
        t.OnHook(Hook("UserPromptSubmit", """, "prompt": "fix tests" """), T0);
        Assert.Equal(CodeState.Thinking, t.Active!.State);
        Assert.Equal("my-app", t.Active.Project);

        t.OnHook(Hook("PreToolUse", """, "tool_name": "Bash", "tool_input": { "command": "npm test" } """), T0.AddSeconds(1));
        Assert.Equal(CodeState.Tool, t.Active.State);
        Assert.Equal("Bash · npm test", t.Active.Detail);

        t.OnHook(Hook("PostToolUse", """, "tool_name": "Bash" """), T0.AddSeconds(2));
        Assert.Equal(CodeState.Thinking, t.Active.State);

        t.OnHook(Hook("PermissionRequest", """, "tool_name": "Edit", "tool_input": { "file_path": "C:\\code\\my-app\\App.cs" } """), T0.AddSeconds(3));
        Assert.Equal(CodeState.NeedsYou, t.Active.State);
        Assert.Equal("Allow Edit · App.cs?", t.Active.Detail);

        t.OnHook(Hook("Stop"), T0.AddSeconds(4));
        Assert.Equal(CodeState.Done, t.Active.State);

        t.OnHook(Hook("SessionEnd"), T0.AddSeconds(5));
        Assert.Null(t.Active);
    }

    [Fact]
    public void Status_line_snapshot_fills_usage()
    {
        var t = new CodeTracker();
        t.OnStatus(J("""
            { "session_id": "s1", "cwd": "/Users/me/site", "model": { "display_name": "Opus" },
              "cost": { "total_cost_usd": 0.42 },
              "context_window": { "used_percentage": 23 },
              "rate_limits": { "five_hour": { "used_percentage": 41.2, "resets_at": 1790000000 },
                               "seven_day": { "used_percentage": 12, "resets_at": 1790500000 } } }
            """), T0);
        var s = t.Active!;
        Assert.Equal("site", s.Project);
        Assert.Equal("Opus", s.Model);
        Assert.Equal(23, s.ContextPct);
        Assert.Equal(0.42, s.CostUsd);
        Assert.Equal(41.2, t.FiveHour!.UsedPct);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000000), t.FiveHour.ResetsAt);
        Assert.Equal("🏝 ctx 23% · 5h 41% · wk 12%", t.StatusLine("s1"));
    }

    [Fact]
    public void Missing_rate_limits_are_null_not_zero()
    {
        var t = new CodeTracker();
        t.OnStatus(J("""{ "session_id": "s1", "context_window": { "used_percentage": null } }"""), T0);
        Assert.Null(t.FiveHour);
        Assert.Null(t.Active!.ContextPct);
        Assert.Equal("🏝", t.StatusLine("s1"));
    }

    [Fact]
    public void Active_is_most_recent_session()
    {
        var t = new CodeTracker();
        t.OnHook(Hook("UserPromptSubmit", session: "a"), T0);
        t.OnHook(Hook("UserPromptSubmit", session: "b"), T0.AddSeconds(5));
        Assert.Equal("b", t.Active!.Id);
        t.OnHook(Hook("Stop", session: "a"), T0.AddSeconds(9));
        Assert.Equal("a", t.Active!.Id);
    }

    [Fact]
    public void Long_commands_are_trimmed_and_unknown_events_ignored()
    {
        var t = new CodeTracker();
        int changes = 0;
        t.Changed += () => changes++;
        t.OnHook(Hook("PreToolUse", """, "tool_name": "Bash", "tool_input": { "command": "dotnet test --configuration Release --no-build --verbosity normal" } """), T0);
        Assert.Equal(40 + "Bash · ".Length, t.Active!.Detail.Length);
        Assert.EndsWith("…", t.Active.Detail);
        t.OnHook(Hook("SomeFutureEvent"), T0);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Any_agent_can_report_with_the_neutral_event_format()
    {
        var t = new CodeTracker();
        t.OnEvent(J("""{ "agent": "Gemini CLI", "cwd": "C:\\code\\site", "state": "working", "detail": "npm test" }"""), T0);
        Assert.Equal("Gemini CLI", t.Active!.Agent);
        Assert.Equal("site", t.Active.Project);
        Assert.Equal(CodeState.Thinking, t.Active.State);

        t.OnEvent(J("""{ "agent": "Gemini CLI", "cwd": "C:\\code\\site", "state": "needs_input", "detail": "Run rm -rf?" }"""), T0.AddSeconds(1));
        Assert.Equal(CodeState.NeedsYou, t.Active.State);
        Assert.Single(t.Sessions); // same agent + folder = same session

        t.OnEvent(J("""{ "agent": "Gemini CLI", "state": "mystery" }"""), T0.AddSeconds(2)); // unknown state: ignored
        Assert.Equal(CodeState.NeedsYou, t.Active.State);

        t.OnEvent(J("""{ "agent": "Gemini CLI", "cwd": "C:\\code\\site", "state": "end" }"""), T0.AddSeconds(3));
        Assert.Empty(t.Sessions);
    }

    [Fact]
    public void Codex_notify_payload_marks_the_turn_done()
    {
        var t = new CodeTracker();
        t.OnCodex(J("""{ "type": "agent-turn-complete", "thread-id": "th1", "cwd": "/home/me/api", "last-assistant-message": "All tests pass.\nDone." }"""), T0);
        Assert.Equal("Codex", t.Active!.Agent);
        Assert.Equal("api", t.Active.Project);
        Assert.Equal(CodeState.Done, t.Active.State);
        Assert.Equal("All tests pass. Done.", t.Active.Detail);
    }
}
