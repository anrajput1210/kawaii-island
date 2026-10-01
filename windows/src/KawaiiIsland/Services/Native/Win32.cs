using System.Runtime.InteropServices;

namespace KawaiiIsland.Services.Native;

/// <summary>Small, documented Win32 wrappers. All P/Invoke lives under Services/Native.</summary>
internal static partial class Win32
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;  // hidden from Alt+Tab and the taskbar
    private const long WS_EX_NOACTIVATE = 0x08000000;  // clicking never steals focus from the user's app
    private const long WS_EX_APPWINDOW = 0x00040000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT p);

    /// <summary>Mouse position in physical screen pixels (works anywhere on screen, not just over our window).</summary>
    public static System.Windows.Point CursorPos() => GetCursorPos(out var p) ? new(p.X, p.Y) : new(double.NaN, double.NaN);

    /// <summary>Marks the window as a non-activating tool window.</summary>
    public static void MakeToolWindow(nint hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = (style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
    }
}
