using System.Windows;

namespace KawaiiIsland.Services;

/// <summary>Pure placement math (physical pixels in, physical pixels out). Unit-tested.</summary>
public static class Placement
{
    /// <summary>
    /// Snaps the pill to a work-area edge or the vertical centre line when it is released within
    /// <paramref name="threshold"/> of it, keeping <paramref name="gap"/> from the edge. Always ends fully on-screen.
    /// </summary>
    /// <returns>The new top-left of the pill.</returns>
    public static Point Snap(Rect pill, Rect work, double threshold, double gap)
    {
        double x = pill.X, y = pill.Y, w = pill.Width, h = pill.Height;

        if (x - work.Left < threshold) x = work.Left + gap;
        else if (work.Right - (x + w) < threshold) x = work.Right - w - gap;

        if (y - work.Top < threshold) y = work.Top + gap;
        else if (work.Bottom - (y + h) < threshold) y = work.Bottom - h - gap;

        double centre = work.Left + work.Width / 2;
        if (Math.Abs(x + w / 2 - centre) < threshold) x = centre - w / 2;

        return ClampInto(new Rect(x, y, w, h), work);
    }

    /// <summary>Moves the pill the minimum distance needed to be fully inside the work area.</summary>
    public static Point ClampInto(Rect pill, Rect work) => new(
        Math.Clamp(pill.X, work.Left, Math.Max(work.Left, work.Right - pill.Width)),
        Math.Clamp(pill.Y, work.Top, Math.Max(work.Top, work.Bottom - pill.Height)));
}
