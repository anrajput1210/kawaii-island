using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using KawaiiIsland.Services.Mail;

namespace KawaiiIsland.Services;

/// <summary>One calendar event (from Google Calendar or Outlook) or one of the user's own tasks.</summary>
public sealed record CalendarItem(string Title, DateTimeOffset? Start, bool AllDay, string Color, bool IsTask, string Id);

/// <summary>
/// Upcoming events from the calendar of the account signed in for mail: Google Calendar for Google, Outlook
/// (Microsoft Graph) for Microsoft. Read-only, today and tomorrow, nothing stored on disk.
/// </summary>
public static class CalendarService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<List<CalendarItem>> FetchAsync(OAuthProvider provider, string accessToken, DateTimeOffset now)
    {
        var until = now.Date.AddDays(2);
        string url = provider.Key == "google"
            ? "https://www.googleapis.com/calendar/v3/calendars/primary/events?singleEvents=true&orderBy=startTime&maxResults=10" +
              $"&timeMin={Uri.EscapeDataString(now.ToString("o"))}&timeMax={Uri.EscapeDataString(new DateTimeOffset(until).ToString("o"))}"
            : "https://graph.microsoft.com/v1.0/me/calendarView?$orderby=start/dateTime&$top=10&$select=id,subject,start,isAllDay" +
              $"&startDateTime={Uri.EscapeDataString(now.UtcDateTime.ToString("o"))}&endDateTime={Uri.EscapeDataString(until.ToUniversalTime().ToString("o"))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (provider.Key != "google") request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return provider.Key == "google" ? ParseGoogle(doc.RootElement) : ParseMicrosoft(doc.RootElement);
    }

    /// <summary>Google Calendar events.list response. Unit-tested.</summary>
    public static List<CalendarItem> ParseGoogle(JsonElement root)
    {
        var list = new List<CalendarItem>();
        if (!root.TryGetProperty("items", out var items)) return list;
        foreach (var e in items.EnumerateArray())
        {
            var start = e.GetProperty("start");
            bool allDay = !start.TryGetProperty("dateTime", out var dt);
            DateTimeOffset? at = allDay
                ? start.TryGetProperty("date", out var d) ? DateTimeOffset.Parse(d.GetString()!, CultureInfo.InvariantCulture) : null
                : DateTimeOffset.Parse(dt.GetString()!, CultureInfo.InvariantCulture);
            list.Add(new CalendarItem(Str(e, "summary", "(No title)"), at, allDay, "#0A84FF", false, Str(e, "id", "")));
        }
        return list;
    }

    /// <summary>Microsoft Graph calendarView response (times in UTC via the Prefer header). Unit-tested.</summary>
    public static List<CalendarItem> ParseMicrosoft(JsonElement root)
    {
        var list = new List<CalendarItem>();
        if (!root.TryGetProperty("value", out var items)) return list;
        foreach (var e in items.EnumerateArray())
        {
            string raw = e.GetProperty("start").GetProperty("dateTime").GetString()!;
            var at = new DateTimeOffset(DateTime.SpecifyKind(DateTime.Parse(raw, CultureInfo.InvariantCulture), DateTimeKind.Utc));
            bool allDay = e.TryGetProperty("isAllDay", out var a) && a.ValueKind == JsonValueKind.True;
            list.Add(new CalendarItem(Str(e, "subject", "(No title)"), at, allDay, "#0A84FF", false, Str(e, "id", "")));
        }
        return list;
    }

    private static string Str(JsonElement e, string name, string fallback) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : fallback;

    /// <summary>"3% of October" style progress through the current month (from the Python island's month panel).</summary>
    public static double MonthProgress(DateTime now)
    {
        int days = DateTime.DaysInMonth(now.Year, now.Month);
        return ((now.Day - 1) + now.TimeOfDay.TotalDays) / days;
    }

    /// <summary>Today's task time ("15:00", "3:30 PM") → a timestamp, or null when it has none / can't be read.</summary>
    public static DateTimeOffset? TaskTime(string time, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        return DateTime.TryParse(time, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var t)
               || DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out t)
            ? new DateTimeOffset(today.Date + t.TimeOfDay)
            : null;
    }

    /// <summary>"Gym 5:30 PM" → ("Gym", "5:30 PM"); "Read" → ("Read", ""). Unit-tested.</summary>
    public static (string Name, string Time) SplitTask(string text)
    {
        text = text.Trim();
        var parts = text.Split(' ');
        for (int take = Math.Min(2, parts.Length - 1); take >= 1; take--)
        {
            string time = string.Join(' ', parts[^take..]);
            if (TaskTime(time, DateTime.Today) is not null) return (string.Join(' ', parts[..^take]), time);
        }
        return (text, "");
    }
}

/// <summary>CPU, memory, disk and network numbers for the System tab (from the Python island's perf panel).</summary>
public sealed class SystemStats
{
    private long _idle, _total, _rx, _tx;
    private DateTime _netAt;

    public sealed record Snapshot(double Cpu, double Ram, double Disk, double DownBps, double UpBps);

    public Snapshot Read()
    {
        double cpu = 0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long i = idle.Value, t = kernel.Value + user.Value; // kernel time includes idle
            if (_total > 0 && t > _total) cpu = 100.0 * (1 - (double)(i - _idle) / (t - _total));
            _idle = i; _total = t;
        }
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        double ram = GlobalMemoryStatusEx(ref mem) ? mem.dwMemoryLoad : 0;
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        double disk = drive.IsReady ? 100.0 * (drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize : 0;

        long rx = 0, tx = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var s = nic.GetIPStatistics();
            rx += s.BytesReceived; tx += s.BytesSent;
        }
        var now = DateTime.UtcNow;
        double secs = (now - _netAt).TotalSeconds;
        double down = _netAt != default && secs > 0 ? Math.Max(0, (rx - _rx) / secs) : 0;
        double up = _netAt != default && secs > 0 ? Math.Max(0, (tx - _tx) / secs) : 0;
        _rx = rx; _tx = tx; _netAt = now;
        return new Snapshot(Math.Clamp(cpu, 0, 100), ram, disk, down, up);
    }

    /// <summary>"1.2 MB/s". Unit-tested.</summary>
    public static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_048_576 => $"{bytesPerSecond / 1_048_576:0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s",
    };

    // ---------------- power (from the Python island's "Basics" hub) ----------------

    public static void Lock() => LockWorkStation();
    public static void Sleep() => SetSuspendState(false, false, false);
    public static void Restart() => Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
    public static void ShutDown() => Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });

    [StructLayout(LayoutKind.Sequential)] private struct FILETIME64 { public long Value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME64 idle, out FILETIME64 kernel, out FILETIME64 user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
}
