using System.IO;
using KawaiiIsland.Services;

namespace KawaiiIsland.Tests;

public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ki-test-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var svc = new ConfigService(_dir);
        Assert.Equal(180, svc.Current.Window.CollapsedWidth);
        Assert.Equal("kiko", svc.Current.Appearance.Mascot);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        using (var svc = new ConfigService(_dir))
        {
            svc.Current.Window.CollapsedWidth = 222;
            svc.Current.Appearance.Mascot = "ribbit";
            svc.SaveNow();
        }
        using var again = new ConfigService(_dir);
        Assert.Equal(222, again.Current.Window.CollapsedWidth);
        Assert.Equal("ribbit", again.Current.Appearance.Mascot);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_used()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, "{ not json");
        using var svc = new ConfigService(_dir);
        Assert.Equal(180, svc.Current.Window.CollapsedWidth);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_dir, "config.bad.json")));
    }

    [Fact]
    public void Out_of_range_values_are_clamped_and_unknowns_reset()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigPath, """
            { "window": { "collapsedWidth": 5, "opacity": 9, "dockEdge": "Sideways" },
              "appearance": { "mascot": "mochi", "theme": "LIGHT" } }
            """);
        using var svc = new ConfigService(_dir);
        Assert.Equal(120, svc.Current.Window.CollapsedWidth);
        Assert.Equal(1.0, svc.Current.Window.Opacity);
        Assert.Equal("Top", svc.Current.Window.DockEdge);
        Assert.Equal("kiko", svc.Current.Appearance.Mascot);
        Assert.Equal("light", svc.Current.Appearance.Theme);
    }

    [Fact]
    public void Debounced_save_is_flushed_on_dispose()
    {
        using (var svc = new ConfigService(_dir))
        {
            svc.Current.Behavior.AutoCollapseSeconds = 9;
            svc.SaveSoon();
        }
        Assert.Contains("\"autoCollapseSeconds\": 9", File.ReadAllText(ConfigPath));
    }
}
