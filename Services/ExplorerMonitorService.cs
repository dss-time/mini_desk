using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MiniDesk.Services;

public sealed class ExplorerMonitorService : IDisposable
{
    private const int HshellWindowCreated = 1;
    private const int HshellWindowDestroyed = 2;
    private readonly Action _desktopChanged;
    private readonly HwndSource _source;
    private readonly int _shellHookMessage;
    private CancellationTokenSource? _debounce;

    public ExplorerMonitorService(Action desktopChanged)
    {
        _desktopChanged = desktopChanged;
        var parameters = new HwndSourceParameters("MiniDesk.ExplorerMonitor")
        {
            Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000)
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _shellHookMessage && ((int)wParam == HshellWindowCreated || (int)wParam == HshellWindowDestroyed))
        {
            var cls = ClassName(lParam);
            if (cls is "Progman" or "WorkerW" or "SHELLDLL_DefView") DebounceRecovery();
        }
        return IntPtr.Zero;
    }

    private void DebounceRecovery()
    {
        _debounce?.Cancel();
        var token = (_debounce = new CancellationTokenSource()).Token;
        _ = RecoverAfterQuietPeriodAsync(token);
    }

    private async Task RecoverAfterQuietPeriodAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(900, token);
            if (!token.IsCancellationRequested) _source.Dispatcher.Invoke(_desktopChanged);
        }
        catch (OperationCanceledException) { }
    }

    private static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[128];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    public void Dispose()
    {
        _debounce?.Cancel();
        DeregisterShellHookWindow(_source.Handle);
        _source.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DeregisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, char[] className, int maxCount);
}
