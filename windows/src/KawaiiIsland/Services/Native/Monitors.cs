using System.Runtime.InteropServices;
using System.Windows;

namespace KawaiiIsland.Services.Native;

/// <summary>A display: device name (e.g. \\.\DISPLAY1), work area in physical pixels, and its DPI scale.</summary>
public sealed record MonitorInfo(string Device, Rect Work, Rect Bounds, double Scale, bool Primary);

/// <summary>
/// Monitor lookup and physical-pixel window positioning. WPF's Left/Top are DIPs relative to whichever
/// monitor the window is on, which gets confusing across mixed-DPI displays, so placement uses raw pixels.
/// </summary>
internal static class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (h, _, _, _) => { if (Info(h) is { } m) list.Add(m); return true; }, 0);
        return list;
    }

    public static MonitorInfo Primary() => Info(MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY))!;

    /// <summary>The monitor a rectangle mostly lies on (nearest one if it is off-screen).</summary>
    public static MonitorInfo For(Rect r)
    {
        var rc = new RECT { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom };
        return Info(MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST)) ?? Primary();
    }

    /// <summary>The saved monitor if it is still connected, else the primary one.</summary>
    public static MonitorInfo ByDevice(string device) =>
        All().FirstOrDefault(m => string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase)) ?? Primary();

    public static Rect WindowRect(nint hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static void MoveWindow(nint hwnd, double x, double y) =>
        SetWindowPos(hwnd, 0, (int)Math.Round(x), (int)Math.Round(y), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    public static void SetBounds(nint hwnd, Rect r) =>
        SetWindowPos(hwnd, 0, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    private static MonitorInfo? Info(nint hMonitor)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref mi)) return null;
        double scale = GetDpiForMonitor(hMonitor, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1.0;
        return new MonitorInfo(mi.szDevice, ToRect(mi.rcWork), ToRect(mi.rcMonitor), scale, (mi.dwFlags & 1) != 0);
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // ---- interop ----
    private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, nint rect, nint data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref RECT rc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint hMonitor, int type, out uint dpiX, out uint dpiY);
}
