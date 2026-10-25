using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MiniDesk.Services;

/// <summary>Keep desktop residency light; accelerate interactive windows on demand.</summary>
internal sealed class RenderingBudgetService : IDisposable
{
    private static RenderingBudgetService? _current;
    private readonly RenderMode _originalMode;
    private readonly HashSet<Window> _interactive = [];
    private bool _disposed;

    internal RenderingBudgetService()
    {
        if (_current is not null) throw new InvalidOperationException("Rendering budget already active.");
        _originalMode = RenderOptions.ProcessRenderMode;
        _current = this;
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    internal static void Watch(Window window) => _current?.Track(window);

    private void Track(Window window)
    {
        void Activate(object? sender, EventArgs args)
        {
            if (!_disposed && _interactive.Add(window)) RenderOptions.ProcessRenderMode = RenderMode.Default;
        }
        void StateChanged(object? sender, EventArgs args) => RefreshBudget();
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => RefreshBudget();
        void Release(object? sender, EventArgs args)
        {
            window.SourceInitialized -= Activate;
            window.Closed -= Release;
            window.StateChanged -= StateChanged;
            window.IsVisibleChanged -= VisibilityChanged;
            if (!_disposed && _interactive.Remove(window)) RefreshBudget();
        }
        window.SourceInitialized += Activate;
        window.Closed += Release;
        window.StateChanged += StateChanged;
        window.IsVisibleChanged += VisibilityChanged;
        if (PresentationSource.FromVisual(window) is HwndSource) Activate(null, EventArgs.Empty);
    }

    private void RefreshBudget()
    {
        if (_disposed) return;
        var mode = _interactive.Any(window => window.IsVisible && window.WindowState != WindowState.Minimized)
            ? RenderMode.Default : RenderMode.SoftwareOnly;
        if (RenderOptions.ProcessRenderMode == mode) return;
        RenderOptions.ProcessRenderMode = mode;
        if (mode != RenderMode.SoftwareOnly) return;
        // Live desktop visuals retain their own images; inactive manager
        // thumbnails and shadow textures need not remain globally cached.
        Controls.SoftBorder.ClearShadowCache();
        GroupIconStorageService.ClearPreviewCache();
        ShellIconService.ClearFileCache();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _interactive.Clear();
        if (ReferenceEquals(_current, this)) _current = null;
        RenderOptions.ProcessRenderMode = _originalMode;
    }
}
