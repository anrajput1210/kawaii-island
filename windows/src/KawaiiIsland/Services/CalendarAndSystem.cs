using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace KawaiiIsland.Services;

/// <summary>One of the user's own tasks, as shown in the Calendar tab.</summary>
public sealed record CalendarItem(string Title, DateTimeOffset? Start, bool AllDay, string Color, bool IsTask, string Id);

public static class CalendarService
{
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
