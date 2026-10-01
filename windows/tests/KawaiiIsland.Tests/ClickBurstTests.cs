using KawaiiIsland.Controls;

namespace KawaiiIsland.Tests;

public sealed class ClickBurstTests
{
    [Fact]
    public void Three_clicks_within_window_trigger()
    {
        var burst = new ClickBurst();
        Assert.False(burst.Register(0));
        Assert.False(burst.Register(300));
        Assert.True(burst.Register(800));
    }

    [Fact]
    public void Slow_clicks_never_trigger()
    {
        var burst = new ClickBurst();
        Assert.False(burst.Register(0));
        Assert.False(burst.Register(600));
        Assert.False(burst.Register(1200)); // first click fell out of the 900 ms window
        Assert.True(burst.Register(1400));  // 600, 1200, 1400 are within 900 ms
    }

    [Fact]
    public void Burst_resets_after_triggering()
    {
        var burst = new ClickBurst();
        burst.Register(0); burst.Register(100);
        Assert.True(burst.Register(200));
        Assert.False(burst.Register(300));
        Assert.False(burst.Register(400));
    }
}
