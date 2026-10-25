using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiniDesk.Controls;

public partial class SoftBorder
{
    private readonly record struct RasterKey(double Radius, double Offset, Color Color, bool Inset, double Scale, double Width, double Height);
    private sealed record RasterShadow(BitmapSource Bitmap, ImageBrush[] Slices, double Pad, double Corner, bool StretchX, bool StretchY, long Bytes);
    private static readonly Dictionary<RasterKey, RasterShadow> RasterShadows = [];
    private static readonly Queue<RasterKey> RasterOrder = [];
    private static long _rasterBytes;
    internal static long ShadowCacheBytes => _rasterBytes;
    internal const long ShadowCacheBudget = 2 * 1024 * 1024;
    internal static void ClearShadowCache()
    {
        RasterShadows.Clear();
        RasterOrder.Clear();
        _rasterBytes = 0;
    }

    private void DrawRasterShadow(DrawingContext drawing, Rect bounds, double radius, double offset, Color color, bool inset)
    {
        var scale = Math.Clamp(VisualTreeHelper.GetDpi(this).DpiScaleX, 1, 2);
        var pad = Math.Ceiling(Math.Abs(offset) * 3 + 2);
        var inner = Math.Ceiling(radius + Math.Abs(offset) * 2.1);
        var stretchX = bounds.Width >= inner * 2 + 2;
        var stretchY = bounds.Height >= inner * 2 + 2;
        // Corners retain their geometry. Only straight edge strips are stretched,
        // so resizing a large card does not create a large bitmap per size.
        var width = stretchX ? inner * 2 + 4 : bounds.Width;
        var height = stretchY ? inner * 2 + 4 : bounds.Height;
        var key = new RasterKey(radius, offset, color, inset, scale, stretchX ? 0 : width, stretchY ? 0 : height);
        if (!RasterShadows.TryGetValue(key, out var entry))
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new TranslateTransform(pad, pad));
                BuildShadow(context, new Rect(0, 0, width, height), radius, offset, color, inset);
                context.Pop();
            }
            var pixelsWide = Math.Max(1, (int)Math.Ceiling((width + pad * 2) * scale));
            var pixelsHigh = Math.Max(1, (int)Math.Ceiling((height + pad * 2) * scale));
            var bitmap = new RenderTargetBitmap(pixelsWide, pixelsHigh, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            var corner = inner + pad;
            var xs = stretchX ? new[] { 0d, corner, width + pad * 2 - corner, width + pad * 2 } : new[] { 0d, width + pad * 2 };
            var ys = stretchY ? new[] { 0d, corner, height + pad * 2 - corner, height + pad * 2 } : new[] { 0d, height + pad * 2 };
            var columns = xs.Length - 1;
            var slices = new ImageBrush[columns * (ys.Length - 1)];
            for (var y = 0; y < ys.Length - 1; y++)
            for (var x = 0; x < columns; x++)
            {
                var brush = new ImageBrush(bitmap) { ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(xs[x], ys[y], xs[x+1]-xs[x], ys[y+1]-ys[y]), Stretch = Stretch.Fill };
                brush.Freeze(); slices[y * columns + x] = brush;
            }
            var bytes = (long)pixelsWide * pixelsHigh * 4;
            while (RasterOrder.Count > 0 && (_rasterBytes + bytes > ShadowCacheBudget || RasterShadows.Count >= 64))
            {
                if (RasterShadows.Remove(RasterOrder.Dequeue(), out var old)) _rasterBytes -= old.Bytes;
            }
            entry = new RasterShadow(bitmap, slices, pad, corner, stretchX, stretchY, bytes);
            if (bytes <= ShadowCacheBudget) { RasterShadows[key] = entry; RasterOrder.Enqueue(key); _rasterBytes += bytes; }
        }
        var area = new Rect(bounds.X-entry.Pad, bounds.Y-entry.Pad, bounds.Width+entry.Pad*2, bounds.Height+entry.Pad*2);
        var dx = entry.StretchX ? new[] { area.Left, area.Left+entry.Corner, area.Right-entry.Corner, area.Right } : new[] { area.Left, area.Right };
        var dy = entry.StretchY ? new[] { area.Top, area.Top+entry.Corner, area.Bottom-entry.Corner, area.Bottom } : new[] { area.Top, area.Bottom };
        var opaque = Background is SolidColorBrush fill && fill.Color.A == 255 && fill.Opacity == 1;
        for (var y = 0; y < dy.Length - 1; y++)
        for (var x = 0; x < dx.Length - 1; x++)
        {
            if (!inset && opaque && entry.StretchX && entry.StretchY && x == 1 && y == 1) continue;
            drawing.DrawRectangle(entry.Slices[y * (dx.Length-1) + x], null, new Rect(dx[x], dy[y], dx[x+1]-dx[x], dy[y+1]-dy[y]));
        }
    }
}
