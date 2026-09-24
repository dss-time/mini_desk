using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MiniDesk.Services;

public static class DesktopHostService
{
    private static readonly IntPtr HwndBottom = new(1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public static bool TryAttach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        var desktop = FindDesktopOwner();
        if (desktop == IntPtr.Zero) return false;

        // Keep the WPF HWND top-level. Turning a per-pixel-transparent WPF HWND into
        // WS_CHILD breaks WPF mouse routing, drag/drop, menus and keyboard focus.
        // An owner relationship keeps the panel with the desktop without changing
        // the window type WPF created and supports.
        new WindowInteropHelper(window).Owner = desktop;
        MoveToModelPosition(window);
        return true;
    }

    public static bool IsDesktopPoint(System.Drawing.Point point)
    {
        var hwnd = WindowFromPoint(point);
        for (var i = 0; hwnd != IntPtr.Zero && i < 8; i++, hwnd = GetParent(hwnd))
        {
            var name = new char[128];
            var length = GetClassName(hwnd, name, name.Length);
            var cls = length > 0 ? new string(name, 0, length) : string.Empty;
            if (cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32") return true;
        }
        return false;
    }

    public static void MoveToModelPosition(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var source = PresentationSource.FromVisual(window);
        var matrix = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var point = matrix.Transform(new Point(window.Left, window.Top));
        var size = matrix.Transform(new Point(window.ActualWidth > 0 ? window.ActualWidth : window.Width,
                                              window.ActualHeight > 0 ? window.ActualHeight : window.Height));
        SetWindowPos(hwnd, HwndBottom, (int)point.X, (int)point.Y, Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y),
            SwpNoActivate | SwpShowWindow);
    }

    public static void ApplyPanelAppearance(Window window, double cornerRadius)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        // Opacity is rendered by the WPF surface's own per-pixel alpha. Applying
        // Acrylic here adds a second compositor blend and makes the 0-100 slider
        // perceptually nonlinear, so keep the HWND responsible only for clipping.
        ApplyRoundedRegion(hwnd, window, cornerRadius);
    }

    private static IntPtr FindDesktopOwner()
    {
        var progman = FindWindow("Progman", null);
        return progman != IntPtr.Zero ? progman : GetShellWindow();
    }

    private static void ApplyRoundedRegion(IntPtr hwnd, Window window, double cornerRadius)
    {
        if (cornerRadius <= 0)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        if (!GetWindowRect(hwnd, out var rect)) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var scale = Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
        var physicalRadius = Math.Min(cornerRadius * scale, Math.Min(width, height) / 2d);
        var diameter = Math.Max(1, (int)Math.Round(physicalRadius * 2));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero) return;
        if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, char[] className, int maxCount);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr obj);
}
