using KawaiiIsland.Services;

namespace KawaiiIsland.Tests;

public sealed class LiveTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Timer_counts_down_pauses_and_finishes_once()
    {
        var t = new CountdownTimer();
        t.Start(TimeSpan.FromMinutes(5), T0);
        Assert.Equal("5:00", CountdownTimer.Format(t.Remaining(T0)));
        Assert.Equal("4:30", CountdownTimer.Format(t.Remaining(T0.AddSeconds(30))));

        t.TogglePause(T0.AddSeconds(30));                       // paused at 4:30
        Assert.Equal("4:30", CountdownTimer.Format(t.Remaining(T0.AddMinutes(3))));
        t.TogglePause(T0.AddMinutes(3));                        // resumed: ends 4:30 later
        Assert.False(t.CheckFinished(T0.AddMinutes(7)));
        Assert.True(t.CheckFinished(T0.AddMinutes(7).AddSeconds(30)));
        Assert.False(t.CheckFinished(T0.AddMinutes(8)));         // only once
        Assert.False(t.Active);
    }

    [Theory]
    [InlineData(3725, "1:02:05")]
    [InlineData(59.2, "1:00")]
    [InlineData(0, "0:00")]
    public void Timer_format(double seconds, string text) => Assert.Equal(text, CountdownTimer.Format(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(50, false, 50, true, BatteryAlerts.Kind.Charging)]
    [InlineData(50, true, 50, false, BatteryAlerts.Kind.Unplugged)]
    [InlineData(21, false, 20, false, BatteryAlerts.Kind.Low)]
    [InlineData(11, false, 10, false, BatteryAlerts.Kind.Low)]
    [InlineData(19, false, 18, false, BatteryAlerts.Kind.None)]   // already warned at 20
    [InlineData(40, false, 39, false, BatteryAlerts.Kind.None)]
    public void Battery_alerts(int p0, bool c0, int p1, bool c1, BatteryAlerts.Kind expected) =>
        Assert.Equal(expected, BatteryAlerts.Check((p0, c0), (p1, c1)));

    [Fact]
    public void Privacy_entry_counts_only_when_started_and_not_stopped()
    {
        Assert.True(PrivacyMonitor.EntryInUse(133000000000000000L, 0L));
        Assert.False(PrivacyMonitor.EntryInUse(0L, 0L));            // never used
        Assert.False(PrivacyMonitor.EntryInUse(133000000000000000L, 133000000000000001L));
        Assert.False(PrivacyMonitor.EntryInUse(null, 0L));
    }

    [Theory]
    [InlineData("AirPods Pro", "")]
    [InlineData("MX Master 3 Mouse", "")]
    [InlineData("JBL Flip Speaker", "")]
    [InlineData("Xbox Wireless Controller", "")]
    [InlineData("Some Gadget", "")]
    public void Bluetooth_glyph_from_name(string name, string glyph) => Assert.Equal(glyph, BluetoothWatcher.Glyph(name));
}
