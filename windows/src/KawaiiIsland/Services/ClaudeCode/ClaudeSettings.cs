using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KawaiiIsland.Services.ClaudeCode;

/// <summary>
/// Adds/removes Kawaii Island's entries in ~/.claude/settings.json and touches nothing else.
/// Every entry we write contains <see cref="Marker"/>, which is how removal finds exactly ours.
///
/// Hooks are async `curl` calls to the local HookServer: Claude Code never waits on them, there is no helper
/// executable that could go missing after an uninstall, and if the island isn't running curl just fails quietly.
/// </summary>
public static class ClaudeSettings
{
    public const string Marker = "/kawaii/";

    /// <summary>How long the island waits for Allow/Deny before handing the question back to the terminal.</summary>
    public const int ApprovalSeconds = 120;

    /// <summary>PermissionRequest hook output for "allow"/"deny"; null (no answer) → "" so Claude Code asks as usual.</summary>
    public static string PermissionReply(string? behavior) => behavior is "allow" or "deny"
        ? new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = behavior == "deny"
                    ? new JsonObject { ["behavior"] = "deny", ["message"] = "Denied from Kawaii Island" }
                    : new JsonObject { ["behavior"] = "allow" },
            },
        }.ToJsonString()
        : "";

    private static readonly string[] Events =
        ["SessionStart", "SessionEnd", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
         "PermissionRequest", "Notification", "Stop"];

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    /// <summary>Adds our hooks (idempotent). Adds the status line only if the user has none.</summary>
    /// <returns>True if our status line is installed (so plan-usage numbers will arrive).</returns>
    public static bool Install(JsonObject settings, int port)
    {
        Uninstall(settings);
        var hooks = settings["hooks"] as JsonObject ?? (JsonObject)(settings["hooks"] = new JsonObject());
        foreach (var ev in Events)
        {
            var groups = hooks[ev] as JsonArray ?? (JsonArray)(hooks[ev] = new JsonArray());
            // PermissionRequest waits (synchronously) for Allow/Deny on the island. If the island isn't running,
            // curl fails at once with no output and Claude Code shows its normal prompt.
            bool ask = ev == "PermissionRequest";
            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "curl",
                    ["args"] = new JsonArray("-s", "-m", ask ? $"{ApprovalSeconds + 5}" : "2", "-H", "Content-Type: application/json",
                                             "--data-binary", "@-", $"http://127.0.0.1:{port}{Marker}{(ask ? "permission" : "hook")}"),
                    ["async"] = !ask,
                    ["timeout"] = ask ? ApprovalSeconds + 10 : 5,
                }),
            });
        }

        if (settings["statusLine"] is not null) return false; // never replace the user's own status line
        settings["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"curl -s -m 1 -H \"Content-Type: application/json\" --data-binary @- http://127.0.0.1:{port}{Marker}status",
        };
        return true;
    }

    /// <summary>Removes only our entries; drops groups/events/"hooks" that become empty.</summary>
    public static void Uninstall(JsonObject settings)
    {
        if (settings["hooks"] is JsonObject hooks)
        {
            foreach (var (ev, node) in hooks.ToList())
            {
                if (node is not JsonArray groups) continue;
                foreach (var group in groups.ToList())
                {
                    if (group?["hooks"] is not JsonArray handlers) continue;
                    foreach (var handler in handlers.ToList())
                        if (IsOurs(handler)) handlers.Remove(handler);
                    if (handlers.Count == 0) groups.Remove(group);
                }
                if (groups.Count == 0) hooks.Remove(ev);
            }
            if (hooks.Count == 0) settings.Remove("hooks");
        }
        if (IsOurs(settings["statusLine"])) settings.Remove("statusLine");
    }

    public static bool IsInstalled(JsonObject settings) => IsOurs(settings["hooks"]);

    /// <summary>Reads, edits and atomically rewrites the real settings file. A backup is kept before installing.</summary>
    /// <returns>For install: whether our status line went in.</returns>
    public static bool Apply(bool enable, int port, string? path = null)
    {
        path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings = File.Exists(path) ? Parse(File.ReadAllText(path)) : new JsonObject();
        if (enable && File.Exists(path)) File.Copy(path, path + ".kawaii-backup", overwrite: true);

        bool statusLine = false;
        if (enable) statusLine = Install(settings, port); else Uninstall(settings);

        var tmp = path + ".kawaii-tmp";
        File.WriteAllText(tmp, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
        return statusLine;
    }

    /// <exception cref="InvalidDataException">The file isn't a JSON object (left untouched).</exception>
    private static JsonObject Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? throw new InvalidDataException("settings.json is not a JSON object");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("settings.json is not valid JSON: " + ex.Message, ex);
        }
    }

    private static bool IsOurs(JsonNode? node) => node?.ToJsonString().Contains(Marker) == true;
}
