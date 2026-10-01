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

    // ---------------- AppBar docking ----------------

    public static Edge ParseEdge(string? s) => Enum.TryParse<Edge>(s, ignoreCase: true, out var e) ? e : Edge.Top;

    /// <summary>
    /// AppBar strip from the rect the shell returned for ABM_QUERYPOS (it may already be moved off a taskbar
    /// or another app bar): keep the docked side and trim to <paramref name="thickness"/>. Always a full-length strip.
    /// </summary>
    public static Rect Strip(Rect proposed, Edge edge, double thickness) => edge switch
    {
        Edge.Top => new Rect(proposed.Left, proposed.Top, proposed.Width, thickness),
        Edge.Bottom => new Rect(proposed.Left, proposed.Bottom - thickness, proposed.Width, thickness),
        Edge.Left => new Rect(proposed.Left, proposed.Top, thickness, proposed.Height),
        _ => new Rect(proposed.Right - thickness, proposed.Top, thickness, proposed.Height),
    };

    /// <summary>Pill top-left inside the strip: <paramref name="gap"/> from the docked edge, placed along it by alignment.</summary>
    public static Point PillInStrip(Rect strip, Edge edge, string alignment, Size pill, double gap)
    {
        bool horizontal = edge is Edge.Top or Edge.Bottom;
        double along = horizontal
            ? Along(strip.Left, strip.Width, pill.Width, alignment, gap)
            : Along(strip.Top, strip.Height, pill.Height, alignment, gap);
        return edge switch
        {
            Edge.Top => new Point(along, strip.Top + gap),
            Edge.Bottom => new Point(along, strip.Bottom - gap - pill.Height),
            Edge.Left => new Point(strip.Left + gap, along),
            _ => new Point(strip.Right - gap - pill.Width, along),
        };
    }

    private static double Along(double start, double length, double size, string alignment, double gap) =>
        alignment.ToLowerInvariant() switch
        {
            "left" or "top" => start + gap,
            "right" or "bottom" => start + length - size - gap,
            _ => start + (length - size) / 2,
        };

    /// <summary>Where the collapsed pill sits inside its (larger, shadow-padded) window for a given anchor.</summary>
    public static Point PillOffset(Size window, Size pill, HorizontalAlignment h, VerticalAlignment v, double gap) => new(
        h switch { HorizontalAlignment.Left => gap, HorizontalAlignment.Right => window.Width - pill.Width - gap, _ => (window.Width - pill.Width) / 2 },
        v switch { VerticalAlignment.Top => gap, VerticalAlignment.Bottom => window.Height - pill.Height - gap, _ => (window.Height - pill.Height) / 2 });
}

public enum Edge { Top, Bottom, Left, Right }
