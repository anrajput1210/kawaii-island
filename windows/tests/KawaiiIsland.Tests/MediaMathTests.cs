using System.Windows.Media;
using KawaiiIsland.Services.Media;

namespace KawaiiIsland.Tests;

public sealed class MediaMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Track = TimeSpan.FromMinutes(3);

    [Fact]
    public void Playing_advances_from_last_report() =>
        Assert.Equal(TimeSpan.FromSeconds(70), MediaMath.Interpolate(TimeSpan.FromSeconds(60), T0, T0.AddSeconds(10), true, Track));

    [Fact]
    public void Paused_stays_put() =>
        Assert.Equal(TimeSpan.FromSeconds(60), MediaMath.Interpolate(TimeSpan.FromSeconds(60), T0, T0.AddSeconds(10), false, Track));

    [Fact]
    public void Never_runs_past_the_end() =>
        Assert.Equal(Track, MediaMath.Interpolate(TimeSpan.FromSeconds(175), T0, T0.AddMinutes(5), true, Track));

    [Fact]
    public void No_timeline_does_not_advance() => // Apple Music reports duration 0 and no update time
        Assert.Equal(TimeSpan.Zero, MediaMath.Interpolate(TimeSpan.Zero, default, T0, true, TimeSpan.Zero));

    [Theory]
    [InlineData(187, "3:07")]
    [InlineData(5, "0:05")]
    [InlineData(3723, "1:02:03")]
    public void Formats_times(int seconds, string expected) => Assert.Equal(expected, MediaMath.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Tint_ignores_grey_and_dark_pixels()
    {
        // two pink pixels, one grey, one black → pink wins
        byte[] bgra = [0xB1, 0x8F, 0xFF, 255, 0xB1, 0x8F, 0xFF, 255, 128, 128, 128, 255, 0, 0, 0, 255];
        Assert.Equal(Color.FromRgb(0xFF, 0x8F, 0xB1), MediaMath.DominantColor(bgra));
    }

    [Fact]
    public void Greyscale_art_has_no_tint()
    {
        byte[] bgra = [100, 100, 100, 255, 200, 200, 200, 255];
        Assert.Null(MediaMath.DominantColor(bgra));
    }

}
