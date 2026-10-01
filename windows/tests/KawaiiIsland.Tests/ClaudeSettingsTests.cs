using System.IO;
using System.Text.Json.Nodes;
using KawaiiIsland.Services.ClaudeCode;

namespace KawaiiIsland.Tests;

public sealed class ClaudeSettingsTests
{
    private const string UserSettings = """
        {
          "model": "opus",
          "hooks": {
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "my-guard.sh" } ] } ]
          },
          "statusLine": { "type": "command", "command": "my-status.sh" }
        }
        """;

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Install_adds_async_curl_hooks_and_keeps_user_entries()
    {
        var s = Parse(UserSettings);
        bool statusLine = ClaudeSettings.Install(s, 47811);

        Assert.False(statusLine); // user already has a status line: left alone
        Assert.Equal("my-status.sh", s["statusLine"]!["command"]!.GetValue<string>());
        Assert.Equal("opus", s["model"]!.GetValue<string>());

        var pre = s["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(2, pre.Count);
        Assert.Equal("my-guard.sh", pre[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        var ours = pre[1]!["hooks"]![0]!;
        Assert.Equal("curl", ours["command"]!.GetValue<string>());
        Assert.True(ours["async"]!.GetValue<bool>());
        Assert.Contains("http://127.0.0.1:47811/kawaii/hook", ours["args"]!.ToJsonString());
        Assert.NotNull(s["hooks"]!["Stop"]);
        Assert.NotNull(s["hooks"]!["PermissionRequest"]);
    }

    [Fact]
    public void Install_adds_status_line_when_user_has_none()
    {
        var s = Parse("""{ "theme": "dark" }""");
        Assert.True(ClaudeSettings.Install(s, 47811));
        Assert.Contains("/kawaii/status", s["statusLine"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Install_twice_does_not_duplicate()
    {
        var s = Parse(UserSettings);
        ClaudeSettings.Install(s, 47811);
        ClaudeSettings.Install(s, 47811);
        Assert.Equal(2, s["hooks"]!["PreToolUse"]!.AsArray().Count);
        Assert.Single(s["hooks"]!["Stop"]!.AsArray());
    }

    [Fact]
    public void Uninstall_restores_exactly_the_original()
    {
        var s = Parse(UserSettings);
        ClaudeSettings.Install(s, 47811);
        ClaudeSettings.Uninstall(s);
        Assert.True(JsonNode.DeepEquals(Parse(UserSettings), s));

        var bare = Parse("""{ "theme": "dark" }""");
        ClaudeSettings.Install(bare, 47811);
        ClaudeSettings.Uninstall(bare);
        Assert.True(JsonNode.DeepEquals(Parse("""{ "theme": "dark" }"""), bare));
    }

    [Fact]
    public void Apply_writes_file_with_backup_and_refuses_invalid_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ki-claude-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, UserSettings);
            ClaudeSettings.Apply(enable: true, 47811, path);
            Assert.True(ClaudeSettings.IsInstalled(Parse(File.ReadAllText(path))));
            Assert.Equal(UserSettings, File.ReadAllText(path + ".kawaii-backup"));

            ClaudeSettings.Apply(enable: false, 47811, path);
            Assert.False(ClaudeSettings.IsInstalled(Parse(File.ReadAllText(path))));

            File.WriteAllText(path, "{ broken");
            Assert.Throws<InvalidDataException>(() => ClaudeSettings.Apply(enable: true, 47811, path));
            Assert.Equal("{ broken", File.ReadAllText(path)); // untouched
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
