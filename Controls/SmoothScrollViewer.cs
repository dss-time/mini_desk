using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MiniDesk.Controls;

/// <summary>Pixel wheel scrolling with compositor motion between layout updates.</summary>
public sealed class SmoothScrollViewer : ScrollViewer
{
    private readonly TranslateTransform _motion = new();
    private FrameworkElement? _surface;
    private Transform? _originalTransform;
    private CacheMode? _originalCache;
    private double _target;
    private double? _visualStart;
    private int _version;
    internal bool IsAnimating => _surface is not null;
    internal double DisplayedOffset => VerticalOffset - _motion.Y;

    public SmoothScrollViewer() => Unloaded += (_, _) => Finish();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled || ScrollableHeight <= 0 || SystemParameters.WheelScrollLines == 0 ||
            !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || Content is not FrameworkElement surface)
        { base.OnMouseWheel(e); return; }
        var distance = SystemParameters.WheelScrollLines < 0 ? ViewportHeight : SystemParameters.WheelScrollLines * 16d;
        var next = Math.Clamp((IsAnimating ? _target : VerticalOffset) - e.Delta / 120d * distance, 0, ScrollableHeight);
        e.Handled = true;
        if (IsAnimating && Math.Abs(next - _target) < .01) return;
        if (!IsAnimating && Math.Abs(next - VerticalOffset) < .01) return;
        _visualStart = DisplayedOffset;
        if (_surface is null)
        {
            _surface = surface;
            _originalTransform = surface.RenderTransform;
            _originalCache = surface.CacheMode;
            surface.RenderTransform = new TransformGroup { Children = { _originalTransform, _motion } };
            var dpi = VisualTreeHelper.GetDpi(this);
            // Temporary, bounded cache: release it when motion ends or the page unloads.
            if (surface.ActualWidth * surface.ActualHeight * dpi.DpiScaleX * dpi.DpiScaleY * 4 <= 16 * 1024 * 1024)
                surface.CacheMode = new BitmapCache();
        }
        _target = next;
        ScrollToVerticalOffset(next);
        if (Math.Abs(VerticalOffset - next) < .01) StartMotion();
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) Finish();
        else if (_visualStart.HasValue) StartMotion();
        else if (e.VerticalChange != 0 && IsAnimating) Finish(); // Thumb, keyboard or programmatic scrolling.
        base.OnScrollChanged(e);
    }

    private void StartMotion()
    {
        if (_surface is null || _visualStart is not double start) return;
        _visualStart = null;
        var version = ++_version;
        var from = VerticalOffset - start;
        _motion.BeginAnimation(TranslateTransform.YProperty, null);
        _motion.Y = from;
        var animation = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => { if (version == _version) Finish(); };
        _motion.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private void Finish()
    {
        _version++;
        _visualStart = null;
        _motion.BeginAnimation(TranslateTransform.YProperty, null);
        _motion.Y = 0;
        if (_surface is not null)
        {
            _surface.RenderTransform = _originalTransform;
            _surface.CacheMode = _originalCache;
        }
        _surface = null;
        _originalTransform = null;
        _originalCache = null;
        _target = VerticalOffset;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        StopAtCurrentPosition();
        base.OnPreviewMouseDown(e);
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        StopAtCurrentPosition();
        base.OnPreviewKeyDown(e);
    }
    private void StopAtCurrentPosition()
    {
        if (!IsAnimating) return;
        var offset = Math.Clamp(DisplayedOffset, 0, ScrollableHeight);
        Finish();
        ScrollToVerticalOffset(offset);
    }
}
