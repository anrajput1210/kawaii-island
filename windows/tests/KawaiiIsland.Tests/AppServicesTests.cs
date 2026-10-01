using System.IO;
using KawaiiIsland.Services;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland.Tests;

public sealed class AppServicesTests
{
    [Theory]
    [InlineData("Ctrl+Alt+I", HotkeyText.Ctrl | HotkeyText.Alt, 0x49)]
    [InlineData("shift + win + F5", HotkeyText.Shift | HotkeyText.Win, 0x74)]
    public void Hotkey_text_parses(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyText.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(vk, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I")]              // no modifier: would steal normal typing
    [InlineData("Ctrl+Alt")]       // no key
    [InlineData("Ctrl+Banana")]    // unknown key
    [InlineData("Ctrl+A+B")]       // two keys
    public void Bad_hotkeys_are_rejected(string text) => Assert.False(HotkeyText.TryParse(text, out _, out _));

    [Fact]
    public void Dev_builds_are_not_auto_registered_for_startup()
    {
        Assert.True(StartupRegistration.IsDevBuild(@"C:\src\KawaiiIsland\bin\Debug\net8.0\KawaiiIsland.exe"));
        Assert.False(StartupRegistration.IsDevBuild(@"C:\Program Files\Kawaii Island\KawaiiIsland.exe"));
    }

    [Fact]
    public void File_logger_writes_a_daily_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kawaii-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            FileLoggerProvider.Create(dir).LogWarning("hello {Who}", "island");
            var text = File.ReadAllText(Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd}.log"));
            Assert.Contains("WARN hello island", text);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
