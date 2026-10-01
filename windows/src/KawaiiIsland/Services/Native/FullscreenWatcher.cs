using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace KawaiiIsland.Services.Native;

/// <summary>
/// "Is something fullscreen on this monitor right now?" (spec §5). Polled every 500 ms by the island:
/// the shell's notification state catches D3D games and presentations; the foreground-window check catches
/// borderless fullscreen (F11 browsers, video players) on a specific monitor.
/// </summary>
internal static class FullscreenWatcher
{
    public static bool IsActive(MonitorInfo monitor)
    {
        if (SHQueryUserNotificationState(out int state) == 0 && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            return true;

        var fg = GetForegroundWindow();
        if (fg == 0 || !GetWindowRect(fg, out var r)) return false;
        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        bool caption = (GetWindowLongPtr(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        return IsFullscreenWindow(new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), monitor.Bounds, caption, cls.ToString());
    }

    /// <summary>
    /// A captionless window covering the whole monitor, excluding the desktop and shell (which also do).
    /// Maximized windows keep their caption, so they never count.
    /// </summary>
    public static bool IsFullscreenWindow(Rect window, Rect monitor, bool hasCaption, string className) =>
        !hasCaption
        && className is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        && window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    // ---- interop ----
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(nint hwnd, int index);
}
