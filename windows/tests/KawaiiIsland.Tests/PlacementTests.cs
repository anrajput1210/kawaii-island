using System.Windows;
using KawaiiIsland.Services;

namespace KawaiiIsland.Tests;

public sealed class PlacementTests
{
    // 1920x1040 work area (taskbar at the bottom), 180x36 pill, 24 px threshold, 6 px gap.
    private static readonly Rect Work = new(0, 0, 1920, 1040);
    private static Point SnapAt(double x, double y) => Placement.Snap(new Rect(x, y, 180, 36), Work, 24, 6);

    [Fact]
    public void Near_left_edge_snaps_with_gap() => Assert.Equal(new Point(6, 400), SnapAt(15, 400));

    [Fact]
    public void Near_right_edge_snaps_with_gap() => Assert.Equal(new Point(1920 - 180 - 6, 400), SnapAt(1730, 400));

    [Fact]
    public void Near_top_and_centre_docks_top_centre() => Assert.Equal(new Point(870, 6), SnapAt(880, 10));

    [Fact]
    public void Near_bottom_snaps_above_taskbar() => Assert.Equal(new Point(500, 1040 - 36 - 6), SnapAt(500, 1000));

    [Fact]
    public void Middle_of_screen_stays_put() => Assert.Equal(new Point(500, 400), SnapAt(500, 400));

    [Fact]
    public void Dropped_off_screen_comes_back() => Assert.Equal(new Point(6, 6), SnapAt(-300, -50));

    [Fact]
    public void Clamp_moves_minimum_distance_onto_screen()
    {
        Assert.Equal(new Point(1740, 1004), Placement.ClampInto(new Rect(5000, 5000, 180, 36), Work));
        Assert.Equal(new Point(300, 200), Placement.ClampInto(new Rect(300, 200, 180, 36), Work));
    }

    [Fact]
    public void Second_monitor_offsets_are_respected()
    {
        var right = new Rect(1920, 0, 2560, 1400);
        Assert.Equal(new Point(1926, 6), Placement.Snap(new Rect(1930, 3, 180, 36), right, 24, 6));
    }
}
