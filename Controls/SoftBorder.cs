using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MiniDesk.Controls;

/// <summary>A solid soft-plastic surface. Positive depth is raised; negative depth is inset.</summary>
public class SoftBorder : Border
{
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.RegisterAttached(
        "Selected", typeof(bool), typeof(SoftBorder), new PropertyMetadata(false));
    public static bool GetSelected(DependencyObject target) => (bool)target.GetValue(SelectedProperty);
    public static void SetSelected(DependencyObject target, bool value) => target.SetValue(SelectedProperty, value);
    public static readonly DependencyProperty DepthProperty = DependencyProperty.Register(
        nameof(Depth), typeof(double), typeof(SoftBorder), new PropertyMetadata(0d, DepthChanged));
    private static readonly DependencyProperty BlendProperty = DependencyProperty.Register(
        "Blend", typeof(double), typeof(SoftBorder), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    private double _previousDepth;
    private double _targetDepth;
    private readonly record struct ShadowKey(double Width, double Height, double Radius, double Offset, Color Color, bool Inset);
    private static readonly Dictionary<ShadowKey, DrawingGroup> Shadows = [];
    public double Depth { get => (double)GetValue(DepthProperty); set => SetValue(DepthProperty, value); }

    private static void DepthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var surface = (SoftBorder)sender;
        var target = (double)args.NewValue;
        surface._previousDepth = surface._targetDepth;
        surface._targetDepth = target;
        if (!surface.IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            surface.BeginAnimation(BlendProperty, null);
            surface.SetValue(BlendProperty, 1d);
            return;
        }
        surface.BeginAnimation(BlendProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(300))
        { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var radius = Math.Min(CornerRadius.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2);
        var progress = (double)GetValue(BlendProperty);
        var depth = _targetDepth;
        var previousDepth = _previousDepth;
        // Compact windows retain the same light direction with half-size shadows.
        if (Window.GetWindow(this) is { ActualWidth: < 960 }) { depth *= 0.5; previousDepth *= 0.5; }
        var dark = TryFindResource("SoftShadowColor") is Color darkColor ? darkColor : Color.FromRgb(184, 188, 194);
        var light = TryFindResource("SoftHighlightColor") is Color lightColor ? lightColor : Colors.White;
        var shape = new RectangleGeometry(bounds, radius, radius);
        if (progress < 1)
        {
            drawing.PushOpacity(1 - progress);
            DrawSurface(drawing, bounds, radius, previousDepth, dark, light, shape);
            drawing.Pop();
        }
        drawing.PushOpacity(progress);
        DrawSurface(drawing, bounds, radius, depth, dark, light, shape);
        drawing.Pop();
        if (BorderBrush is not null && BorderThickness.Left > 0)
        {
            var strokeBounds = bounds;
            strokeBounds.Inflate(-BorderThickness.Left / 2, -BorderThickness.Left / 2);
            if (strokeBounds.Width > 0 && strokeBounds.Height > 0)
                drawing.DrawRoundedRectangle(null, new Pen(BorderBrush, Math.Min(1, BorderThickness.Left)), strokeBounds, radius, radius);
        }
    }

    private void DrawSurface(DrawingContext drawing, Rect bounds, double radius, double depth, Color dark, Color light, Geometry shape)
    {
        if (depth > 0)
        {
            DrawShadow(drawing, bounds, radius, depth, dark, false);
            DrawShadow(drawing, bounds, radius, -depth, light, false);
        }
        // Do not call Border.OnRender: the custom surface owns both shadows and fill.
        drawing.DrawGeometry(Background, null, shape);
        if (depth < 0)
        {
            drawing.PushClip(shape);
            DrawShadow(drawing, bounds, radius, -depth, dark, true);
            DrawShadow(drawing, bounds, radius, depth, light, true);
            drawing.Pop();
        }
    }

    private static void DrawShadow(DrawingContext drawing, Rect bounds, double radius, double offset, Color color, bool inset)
    {
        var key = new ShadowKey(bounds.Width, bounds.Height, radius, offset, color, inset);
        if (!Shadows.TryGetValue(key, out var shadow))
        {
            shadow = new DrawingGroup();
            using (var context = shadow.Open()) BuildShadow(context, bounds, radius, offset, color, inset);
            shadow.Freeze();
            if (Shadows.Count >= 512) Shadows.Clear();
            Shadows[key] = shadow;
        }
        drawing.DrawDrawing(shadow);
    }

    private static void BuildShadow(DrawingContext drawing, Rect bounds, double radius, double offset, Color color, bool inset)
    {
        // Nested translucent silhouettes approximate a Gaussian blur without bitmap effects
        // or shaders. The fill is always solid; only the two shadows are feathered.
        const int samples = 8;
        var blur = Math.Abs(offset) * 2;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        for (var index = samples; index >= 1; index--)
        {
            var spread = blur * index / samples;
            var rect = bounds;
            // For inset shadows this translated silhouette is the cutout, matching
            // CSS inset +X/+Y: pressure shades the inner top/left edge.
            rect.Offset(offset, offset);
            rect.Inflate(spread - blur * 0.45, spread - blur * 0.45);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            drawing.PushOpacity(1 - Math.Pow(1 - 0.045, 20d / samples));
            var silhouette = new RectangleGeometry(rect, Math.Max(0, radius + spread - blur * 0.45), Math.Max(0, radius + spread - blur * 0.45));
            if (inset)
            {
                var outer = bounds;
                outer.Inflate(blur * 4 + 1, blur * 4 + 1);
                drawing.DrawGeometry(brush, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outer), silhouette));
            }
            else drawing.DrawGeometry(brush, null, silhouette);
            drawing.Pop();
        }
    }
}
