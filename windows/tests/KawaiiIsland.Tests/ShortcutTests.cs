using KawaiiIsland.Services;

namespace KawaiiIsland.Tests;

public sealed class ShortcutTests
{
    private static readonly string Notepad = Environment.ExpandEnvironmentVariables(@"%WINDIR%\notepad.exe");
    private static readonly string Explorer = Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe");

    [Fact]
    public void Pin_labels_from_file_name_and_rejects_duplicates_missing_files_and_overflow()
    {
        var items = new List<ShortcutItem>();
        Assert.Equal(PinResult.Added, ShortcutLauncher.Pin(items, Notepad, max: 2));
        Assert.Equal("notepad", items[0].Label);
        Assert.Equal(PinResult.AlreadyPinned, ShortcutLauncher.Pin(items, Notepad.ToUpperInvariant(), max: 2));
        Assert.Equal(PinResult.Missing, ShortcutLauncher.Pin(items, @"C:\nope\missing.exe", max: 2));
        Assert.Equal(PinResult.Added, ShortcutLauncher.Pin(items, Explorer, max: 2));
        Assert.Equal(PinResult.Full, ShortcutLauncher.Pin(items, Environment.SystemDirectory, max: 2));
    }

    [Fact]
    public void Move_reorders_and_ignores_out_of_range()
    {
        var items = new List<ShortcutItem> { new() { Label = "a" }, new() { Label = "b" }, new() { Label = "c" } };
        ShortcutLauncher.Move(items, 0, 2);
        Assert.Equal("bca", string.Concat(items.Select(i => i.Label)));
        ShortcutLauncher.Move(items, 2, 9);
        Assert.Equal("bca", string.Concat(items.Select(i => i.Label)));
    }
}
