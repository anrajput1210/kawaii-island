using System.Windows.Media;

namespace KawaiiIsland.Services.Media;

/// <summary>Pure helpers for the music module (unit-tested).</summary>
public static class MediaMath
{
    /// <summary>
    /// SMTC only reports the position when it changes state, so the playhead is interpolated locally:
    /// last reported position + time since that report, clamped to the track. Sources without a timeline
    /// (duration 0, e.g. Apple Music or live streams) don't advance.
    /// </summary>
    public static TimeSpan Interpolate(TimeSpan position, DateTimeOffset updated, DateTimeOffset now, bool playing, TimeSpan duration)
    {
        var p = playing && duration > TimeSpan.Zero ? position + (now - updated) : position;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        return duration > TimeSpan.Zero && p > duration ? duration : p;
    }

    /// <summary>"3:07", or "1:02:03" for long streams.</summary>
    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    /// <summary>
    /// Album-art tint (spec §3.3): average of the saturated pixels of a small (e.g. 32×32) BGRA32 thumbnail.
    /// Grey/black/white art has no tint (null): the island keeps its accent colour.
    /// </summary>
    public static Color? DominantColor(byte[] bgra)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
            int max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            if (max < 50 || (max - min) < max * 0.3) continue; // too dark or too grey
            b += pb; g += pg; r += pr; n++;
        }
        return n == 0 ? null : Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

}
