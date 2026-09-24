using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using MiniDesk.Models;

namespace MiniDesk.Services;

public sealed class DisplayTopologyService : IDisposable
{
    private const int MonitorDefaultToNearest = 2;
    private const int WmDpiChanged = 0x02E0;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private readonly object _sync = new();
    private IReadOnlyList<DisplayMonitor> _monitors = [];
    private bool _disposed;

    public static DisplayTopologyService Current { get; } = new();
    public event EventHandler? TopologyChanged;
    public IReadOnlyList<DisplayMonitor> Monitors { get { lock (_sync) return _monitors; } }

    private DisplayTopologyService()
    {
        Refresh();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Refresh()
    {
        var monitors = new List<DisplayMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(handle, ref info)) return true;
            var dpiX = 96u;
            var dpiY = 96u;
            try { _ = GetDpiForMonitor(handle, 0, out dpiX, out dpiY); } catch { }
            monitors.Add(new DisplayMonitor(handle, info.Device.TrimEnd('\0'), info.Monitor, info.Work,
                dpiX <= 0 ? 96 : dpiX, dpiY <= 0 ? 96 : dpiY, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        lock (_sync) _monitors = monitors;
    }

    public void Apply(CategoryGroup group, Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var monitors = Monitors;
        if (monitors.Count == 0) return;
        var preferred = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, group.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        var target = preferred ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        var widthDip = Math.Max(64, window.Width);
        var heightDip = Math.Max(64, window.Height);
        var widthPx = Math.Min(target.WorkArea.Width, DipToPx(widthDip, target.DpiX));
        var heightPx = Math.Min(target.WorkArea.Height, DipToPx(heightDip, target.DpiY));

        var offsetX = group.MonitorOffsetX;
        var offsetY = group.MonitorOffsetY;
        if (offsetX is null || offsetY is null || !double.IsFinite(offsetX.Value) || !double.IsFinite(offsetY.Value))
        {
            // Schema v4 stored WPF DIPs in the virtual desktop coordinate space.
            offsetX = group.Left - target.WorkArea.Left * 96d / target.DpiX;
            offsetY = group.Top - target.WorkArea.Top * 96d / target.DpiY;
            group.MonitorOffsetX = offsetX;
            group.MonitorOffsetY = offsetY;
        }

        var x = target.WorkArea.Left + DipToPx(offsetX.Value, target.DpiX);
        var y = target.WorkArea.Top + DipToPx(offsetY.Value, target.DpiY);
        x = Math.Clamp(x, target.WorkArea.Left, Math.Max(target.WorkArea.Left, target.WorkArea.Right - widthPx));
        y = Math.Clamp(y, target.WorkArea.Top, Math.Max(target.WorkArea.Top, target.WorkArea.Bottom - heightPx));
        SetWindowPos(hwnd, IntPtr.Zero, x, y, widthPx, heightPx, SwpNoActivate | SwpNoZOrder);
        group.SavedDpiX = target.DpiX;
        group.SavedDpiY = target.DpiY;
    }

    public void MovePhysical(Window window, int left, int top)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        SetWindowPos(hwnd, IntPtr.Zero, left, top, rect.Width, rect.Height, SwpNoActivate | SwpNoZOrder);
    }

    public PhysicalRect GetPhysicalBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect) ? rect : default;
    }

    public void Capture(CategoryGroup group, Window window, bool updatePreferredMonitor = true)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return;
        var handle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var monitor = Monitors.FirstOrDefault(m => m.Handle == handle);
        if (monitor is null) return;
        if (updatePreferredMonitor) group.MonitorDeviceName = monitor.DeviceName;
        group.MonitorOffsetX = PxToDip(rect.Left - monitor.WorkArea.Left, monitor.DpiX);
        group.MonitorOffsetY = PxToDip(rect.Top - monitor.WorkArea.Top, monitor.DpiY);
        group.SavedDpiX = monitor.DpiX;
        group.SavedDpiY = monitor.DpiY;
        group.Left = PxToDip(rect.Left, monitor.DpiX);
        group.Top = PxToDip(rect.Top, monitor.DpiY);
        if (group.State == GroupDisplayState.Expanded)
        {
            group.Width = PxToDip(rect.Width, monitor.DpiX);
            group.Height = PxToDip(rect.Height, monitor.DpiY);
        }
    }

    public IntPtr HandleDpiMessage(Window window, IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmDpiChanged || lParam == IntPtr.Zero) return IntPtr.Zero;
        // Modern WPF performs the PerMonitorV2 relayout itself. Observe the message
        // without marking it handled so text, controls and images are re-rendered by
        // WPF at the destination monitor DPI, then persist the resulting HWND bounds.
        handled = false;
        window.Dispatcher.BeginInvoke(() =>
        {
            if (window.DataContext is CategoryGroup group) Capture(group, window);
        });
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RaiseTopologyChanged();
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) RaiseTopologyChanged();
    }

    private void RaiseTopologyChanged()
    {
        Refresh();
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    internal static int DipToPx(double value, uint dpi) => (int)Math.Round(value * dpi / 96d);
    internal static double PxToDip(int value, uint dpi) => value * 96d / Math.Max(1, dpi);

    public sealed record DisplayMonitor(IntPtr Handle, string DeviceName, PhysicalRect Bounds, PhysicalRect WorkArea,
        uint DpiX, uint DpiY, bool IsPrimary)
    {
        public int ScalePercent => (int)Math.Round(DpiX * 100d / 96d);
        public string PrimaryLabel => IsPrimary ? "●" : string.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PhysicalRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public PhysicalRect Monitor;
        public PhysicalRect Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out PhysicalRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
