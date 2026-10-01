using System.Windows;
using KawaiiIsland.Services.Native;

namespace KawaiiIsland.Tests;

public sealed class FullscreenTests
{
    private static readonly Rect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Borderless_window_covering_monitor_is_fullscreen() =>
        Assert.True(FullscreenWatcher.IsFullscreenWindow(Monitor, Monitor, hasCaption: false, "Chrome_WidgetWin_1"));

    [Fact]
    public void Maximized_window_with_caption_is_not() =>
        Assert.False(FullscreenWatcher.IsFullscreenWindow(new Rect(-8, -8, 1936, 1056), Monitor, hasCaption: true, "Notepad"));

    [Fact]
    public void Desktop_is_not() =>
        Assert.False(FullscreenWatcher.IsFullscreenWindow(Monitor, Monitor, hasCaption: false, "WorkerW"));

    [Fact]
    public void Fullscreen_on_other_monitor_is_not() =>
        Assert.False(FullscreenWatcher.IsFullscreenWindow(new Rect(1920, 0, 2560, 1440), Monitor, hasCaption: false, "Chrome_WidgetWin_1"));
}
