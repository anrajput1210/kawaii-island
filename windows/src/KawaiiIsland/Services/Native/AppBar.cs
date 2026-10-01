using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KawaiiIsland.Services.Native;

/// <summary>
/// Desktop AppBar reservation (SHAppBarMessage). Design choice (spec §4): the reservation is owned by a separate,
/// fully transparent, click-through "strip" window that spans the whole edge; the visible pill is the normal island
/// window placed inside the strip. This keeps the pill small (no giant hit-testable window) and lets the island
/// expand OVER the content below without touching the reservation, so other windows never jump.
/// </summary>
internal sealed class AppBarService : IDisposable
{
    private HwndSource? _strip;
    private int _callbackMessage;
    private bool _registered;
    private (MonitorInfo Monitor, Edge Edge, double Thickness)? _dock;
    private Rect _lastStrip;

    /// <summary>Reserved strip in physical pixels, raised after every (re)dock.</summary>
    public event Action<Rect, MonitorInfo>? Docked;

    public bool IsDocked => _registered;

    /// <summary>Registers (if needed) and reserves a strip of <paramref name="thicknessPx"/> on the edge of the monitor.</summary>
    public void Dock(MonitorInfo monitor, Edge edge, double thicknessPx)
    {
        _dock = (monitor, edge, thicknessPx);
        var hwnd = EnsureStrip();

        var abd = NewData(hwnd);
        if (!_registered)
        {
            abd.uCallbackMessage = (uint)_callbackMessage;
            SHAppBarMessage(ABM_NEW, ref abd);
            _registered = true;
        }

        abd.uEdge = edge switch { Edge.Left => ABE_LEFT, Edge.Right => ABE_RIGHT, Edge.Bottom => ABE_BOTTOM, _ => ABE_TOP };
        abd.rc = ToRECT(monitor.Bounds);                    // 1. propose the whole monitor
        SHAppBarMessage(ABM_QUERYPOS, ref abd);              // 2. shell moves it off the taskbar / other bars
        abd.rc = ToRECT(Placement.Strip(ToRect(abd.rc), edge, thicknessPx)); // 3. trim to bar thickness
        SHAppBarMessage(ABM_SETPOS, ref abd);                // 4. this actually reserves the work area
        _lastStrip = ToRect(abd.rc);

        Monitors.SetBounds(hwnd, _lastStrip);                // 5. strip window occupies the reserved rect
        Docked?.Invoke(_lastStrip, monitor);
    }

    /// <summary>Releases the reservation; the work area returns to normal immediately.</summary>
    public void Undock()
    {
        var monitor = _dock?.Monitor;
        _dock = null;
        if (!_registered || _strip is null) return;
        var abd = NewData(_strip.Handle);
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
        if (monitor is not null) RefitMaximizedWindows(monitor.Device);
    }

    /// <summary>
    /// ABM_REMOVE frees the work area, but windows that are already maximized keep their smaller size and leave
    /// an empty strip. Re-applying the monitor's (now full) work area with SPIF_SENDCHANGE makes Windows re-fit them.
    /// </summary>
    private static void RefitMaximizedWindows(string device)
    {
        var work = ToRECT(Monitors.ByDevice(device).Work); // fresh query: reservation already released
        SystemParametersInfo(SPI_SETWORKAREA, 0, ref work, SPIF_SENDCHANGE);
    }

    public void Dispose()
    {
        Undock();
        _strip?.Dispose();
        _strip = null;
    }

    private nint EnsureStrip()
    {
        if (_strip is not null) return _strip.Handle;
        _callbackMessage = RegisterWindowMessage("KawaiiIsland.AppBarMessage");
        _strip = new HwndSource(new HwndSourceParameters("KawaiiIsland AppBar strip")
        {
            WindowStyle = unchecked((int)WS_POPUP),
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED,
            Width = 1, Height = 1,
        });
        _strip.AddHook(WndProc);
        SetLayeredWindowAttributes(_strip.Handle, 0, 0, LWA_ALPHA); // alpha 0: invisible and click-through
        ShowWindow(_strip.Handle, SW_SHOWNOACTIVATE);
        return _strip.Handle;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != _callbackMessage) return 0;
        switch ((int)wParam)
        {
            case ABN_POSCHANGED when _dock is { } d:
                // Taskbar or another app bar moved: re-query, but only re-dock if our strip would change
                // (re-docking on our own SETPOS echo would loop).
                var abd = NewData(hwnd);
                abd.uEdge = d.Edge switch { Edge.Left => ABE_LEFT, Edge.Right => ABE_RIGHT, Edge.Bottom => ABE_BOTTOM, _ => ABE_TOP };
                abd.rc = ToRECT(d.Monitor.Bounds);
                SHAppBarMessage(ABM_QUERYPOS, ref abd);
                if (Placement.Strip(ToRect(abd.rc), d.Edge, d.Thickness) != _lastStrip)
                    Dock(d.Monitor, d.Edge, d.Thickness);
                break;
        }
        handled = true;
        return 0;
    }

    private static APPBARDATA NewData(nint hwnd) => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
    private static RECT ToRECT(Rect r) => new() { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom };
    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // ---- interop ----
    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    private const int ABN_POSCHANGED = 1;
    private const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    private const uint WS_POPUP = 0x80000000;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 0x2;
    private const int SW_SHOWNOACTIVATE = 4;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    private const uint SPI_SETWORKAREA = 0x002F, SPIF_SENDCHANGE = 0x2;
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint winIni);
    [DllImport("shell32.dll")] private static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
}
