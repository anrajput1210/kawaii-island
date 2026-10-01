using System.Windows.Media;

namespace KawaiiIsland.Services.Notifications;

/// <summary>One mirrored toast. Lives in memory only (never written to disk).</summary>
public sealed record IslandNotification(uint Id, string App, string Title, string Body, ImageSource? Icon, DateTimeOffset Time);

/// <summary>
/// Notification history (spec §3.2): newest first, last 20, skips muted apps, the island itself and duplicates. Unit-tested.
/// </summary>
public sealed class NotificationFeed(Func<IEnumerable<string>> muted)
{
    public const int Max = 20;
    public const string Self = "Kawaii Island";

    private readonly List<IslandNotification> _items = [];

    public IReadOnlyList<IslandNotification> Items => _items;

    /// <returns>true if the notification is new and should be shown.</returns>
    public bool Add(IslandNotification n)
    {
        if (n.App == Self || muted().Contains(n.App, StringComparer.OrdinalIgnoreCase) || _items.Any(i => i.Id == n.Id)) return false;
        _items.Insert(0, n);
        if (_items.Count > Max) _items.RemoveAt(Max);
        return true;
    }

    public void RemoveApp(string app) => _items.RemoveAll(i => string.Equals(i.App, app, StringComparison.OrdinalIgnoreCase));

    public void Clear() => _items.Clear();

    /// <summary>Toast text lines after the title, first two only (spec: "body (first 2 lines)").</summary>
    public static string Body(IEnumerable<string> lines) => string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(2));

    /// <summary>"now", "5m", "2h", "3d".</summary>
    public static string Ago(TimeSpan age) =>
        age.TotalMinutes < 1 ? "now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m" : age.TotalDays < 1 ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
}
