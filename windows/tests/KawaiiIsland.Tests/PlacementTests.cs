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

    // ---- AppBar strip (spec §9: rect calculation as a pure function of monitor bounds + DPI) ----

    [Fact]
    public void Top_strip_is_full_width_and_bar_thick()
    {
        // 1920x1080 monitor at 125% → 48 DIP strip = 60 px
        Assert.Equal(new Rect(0, 0, 1920, 60), Placement.Strip(new Rect(0, 0, 1920, 1080), Edge.Top, 48 * 1.25));
    }

    [Fact]
    public void Bottom_strip_sits_above_a_taskbar_the_shell_already_excluded()
    {
        // shell moved the proposed bottom up to 1032 (48 px taskbar)
        Assert.Equal(new Rect(0, 972, 1920, 60), Placement.Strip(new Rect(0, 0, 1920, 1032), Edge.Bottom, 60));
    }

    [Fact]
    public void Side_strips_on_a_second_monitor()
    {
        var mon = new Rect(1920, 0, 2560, 1440);
        Assert.Equal(new Rect(1920, 0, 192, 1440), Placement.Strip(mon, Edge.Left, 192));
        Assert.Equal(new Rect(4288, 0, 192, 1440), Placement.Strip(mon, Edge.Right, 192));
    }

    [Fact]
    public void Pill_is_placed_in_strip_by_alignment()
    {
        var strip = new Rect(0, 0, 1920, 60);
        var pill = new Size(225, 45);
        Assert.Equal(new Point(847.5, 7.5), Placement.PillInStrip(strip, Edge.Top, "Center", pill, 7.5));
        Assert.Equal(new Point(7.5, 7.5), Placement.PillInStrip(strip, Edge.Top, "Left", pill, 7.5));
        Assert.Equal(new Point(1920 - 225 - 7.5, 7.5), Placement.PillInStrip(strip, Edge.Top, "Right", pill, 7.5));
        var side = new Rect(0, 0, 240, 1032);
        Assert.Equal(new Point(240 - 7.5 - 225, 1032 - 45 - 7.5), Placement.PillInStrip(side, Edge.Right, "Bottom", pill, 7.5));
    }

    [Fact]
    public void Pill_offset_follows_anchor()
    {
        var win = new Size(700, 275); var pill = new Size(225, 45);
        Assert.Equal(new Point(237.5, 7.5), Placement.PillOffset(win, pill, HorizontalAlignment.Center, VerticalAlignment.Top, 7.5));
        Assert.Equal(new Point(467.5, 222.5), Placement.PillOffset(win, pill, HorizontalAlignment.Right, VerticalAlignment.Bottom, 7.5));
    }

    [Fact]
    public void Unknown_edge_defaults_to_top() => Assert.Equal(Edge.Top, Placement.ParseEdge("sideways"));

    [Fact]
    public void Second_monitor_offsets_are_respected()
    {
        var right = new Rect(1920, 0, 2560, 1400);
        Assert.Equal(new Point(1926, 6), Placement.Snap(new Rect(1930, 3, 180, 36), right, 24, 6));
    }
}
