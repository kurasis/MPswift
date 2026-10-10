using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace Player.App.Services.Windows;

/// <summary>Moves only our panel. No Explorer parenting, shell messages or global hooks.</summary>
internal static class DesktopPanelLayer
{
    internal const int MouseActivate = 0x0021, PositionChanging = 0x0046;
    internal const uint NoSize = 1, NoMove = 2, NoZOrder = 4, NoActivate = 0x10;
    [StructLayout(LayoutKind.Sequential)] internal struct Position { public nint Window, After; public int X, Y, Width, Height; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Bounds { public int Left, Top, Right, Bottom; }
    internal static nint BottomAnchor(nint panel)
    {
        // Leave the desktop wallpaper/desktop icons underneath the panel. HWND_BOTTOM
        // can put a window behind Explorer's desktop surface. With no ordinary window,
        // NOTOPMOST places this nonactivating tool window above the desktop only.
        nint anchor = -2;
        var seen = new HashSet<nint>(); var shell = GetShellWindow(); var name = new StringBuilder(128);
        for (var window = GetTopWindow(0); window != 0 && seen.Count < 10000 && seen.Add(window); window = GetWindow(window, 2))
        {
            if (window == panel || window == shell || !IsWindowVisible(window) || IsIconic(window) || (Style(window) & 8) != 0) continue;
            name.Clear(); GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") continue;
            anchor = window;
        }
        return anchor;
    }
    internal static void Behind(nint panel) => Move(panel, BottomAnchor(panel), 0, 0, NoMove | NoSize | NoActivate);
    internal static void Nonactivating(nint panel)
    {
        var value = Style(panel); // TOOLWINDOW + NOACTIVATE, never APPWINDOW.
        var result = SetWindowLongPtr(panel, -20, (nint)((value | 0x08000080) & ~0x00040000L));
        if (result == 0 && Marshal.GetLastPInvokeError() != 0) throw Failure("Cannot set desktop panel styles.");
    }
    internal static long Style(nint panel) => GetWindowLongPtr(panel, -20).ToInt64();
    internal static Bounds ReadBounds(nint panel)
    { if (!GetWindowRect(panel, out var value)) throw Failure("Cannot read desktop panel bounds."); return value; }
    internal static void Fit(nint panel, nint main, int? left, int? top)
    {
        var bounds = ReadBounds(panel); var width = bounds.Right - bounds.Left; var height = bounds.Bottom - bounds.Top;
        var area = left is { } x && top is { } y ? Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea : Forms.Screen.FromHandle(main).WorkingArea;
        var targetLeft = Math.Clamp(left ?? (area.Left + (area.Width - width) / 2), area.Left, Math.Max(area.Left, area.Right - width));
        var targetTop = Math.Clamp(top ?? (area.Bottom - height - 12), area.Top, Math.Max(area.Top, area.Bottom - height));
        Move(panel, BottomAnchor(panel), targetLeft, targetTop, NoSize | NoActivate);
        // WPF may resize the HWND after entering a monitor with a different DPI.
        bounds = ReadBounds(panel); area = Forms.Screen.FromHandle(panel).WorkingArea;
        targetLeft = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - (bounds.Right - bounds.Left)));
        targetTop = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - (bounds.Bottom - bounds.Top)));
        if (targetLeft != bounds.Left || targetTop != bounds.Top) Move(panel, BottomAnchor(panel), targetLeft, targetTop, NoSize | NoActivate);
    }
    private static void Move(nint panel, nint after, int x, int y, uint flags)
    { if (!SetWindowPos(panel, after, x, y, 0, 0, flags)) throw Failure("Cannot place desktop panel."); }
    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastPInvokeError()));
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Bounds bounds);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint GetTopWindow(nint window);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
