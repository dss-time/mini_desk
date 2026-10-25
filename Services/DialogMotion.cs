using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Shell;

namespace MiniDesk.Services;

public static class DialogMotion
{
    public static void Attach(Window window)
    {
        RenderingBudgetService.Watch(window);
        if (window.Content is not FrameworkElement surface) return;
        if (window.WindowStyle == WindowStyle.None && !window.AllowsTransparency)
        {
            WindowChrome.SetWindowChrome(window, new WindowChrome
            { CaptionHeight = 0, CornerRadius = new CornerRadius(16), GlassFrameThickness = new Thickness(0), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
            window.SourceInitialized += (_, _) =>
            {
                var round = 2;
                DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref round, sizeof(int));
            };
        }
        surface.Opacity = 0;
        window.Loaded += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) { surface.Opacity = 1; return; }
            surface.CacheMode = new BitmapCache();
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            animation.Completed += (_, _) => surface.CacheMode = null;
            surface.BeginAnimation(UIElement.OpacityProperty, animation);
        };
        var allowClose = false;
        var closing = false;
        window.Closing += (_, args) =>
        {
            if (allowClose || !window.IsVisible || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            var result = window.DialogResult;
            surface.IsHitTestVisible = false;
            surface.CacheMode = new BitmapCache();
            var fade = new DoubleAnimation(surface.Opacity, 0, TimeSpan.FromMilliseconds(300))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += (_, _) =>
            {
                allowClose = true;
                if (result.HasValue) window.DialogResult = result;
                else window.Close();
            };
            surface.BeginAnimation(UIElement.OpacityProperty, fade);
        };
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
