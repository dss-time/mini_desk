using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace MiniDesk.Services;

public sealed class GroupIconStorageService
{
    public const long MaximumFileSize = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".svg", ".ico" };
    private static readonly Dictionary<string, ImageSource?> PreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PreviewSync = new();
    private readonly string _storageDirectory;

    public GroupIconStorageService(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk", "GroupIcons");
    }

    public static bool TryValidate(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = LocalizationService.Current.Get("Error_IconMissing");
            return false;
        }
        var extension = Path.GetExtension(path);
        if (!AllowedExtensions.Contains(extension))
        {
            error = LocalizationService.Current.Get("Error_IconFormat");
            return false;
        }
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > MaximumFileSize)
        {
            error = LocalizationService.Current.Get("Error_IconSize");
            return false;
        }
        try
        {
            if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = XmlReader.Create(path, settings);
                while (reader.Read() && reader.NodeType != XmlNodeType.Element) { }
                if (!reader.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("根元素不是 svg");
            }
            else
            {
                using var stream = File.OpenRead(path);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0 || decoder.Frames[0].PixelWidth == 0 || decoder.Frames[0].PixelHeight == 0)
                    throw new InvalidDataException("图片尺寸无效");
            }
        }
        catch
        {
            error = LocalizationService.Current.Get("Error_IconCorrupt");
            return false;
        }
        return true;
    }

    public string Store(string sourcePath)
    {
        if (!TryValidate(sourcePath, out var error)) throw new InvalidDataException(error);
        Directory.CreateDirectory(_storageDirectory);
        var destination = Path.Combine(_storageDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}");
        File.Copy(sourcePath, destination, false);
        return destination;
    }

    public static ImageSource? LoadPreview(string path)
    {
        lock (PreviewSync)
        {
            if (PreviewCache.TryGetValue(path, out var cached)) return cached;
            var image = LoadBitmap(path) ?? LoadShellThumbnail(path) ?? ShellIconService.GetIcon(path);
            if (image?.CanFreeze == true) image.Freeze();
            PreviewCache[path] = image;
            return image;
        }
    }

    private static ImageSource? LoadBitmap(string path)
    {
        if (!File.Exists(path) || Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.FirstOrDefault();
        }
        catch { return null; }
    }

    private static ImageSource? LoadShellThumbnail(string path)
    {
        if (!File.Exists(path)) return null;
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            factory.GetImage(new NativeSize { Width = 128, Height = 128 }, 0x8, out var bitmap);
            if (bitmap == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(128, 128));
                source.Freeze();
                return source;
            }
            finally { DeleteObject(bitmap); }
        }
        catch { return null; }
        finally { if (factory is not null) Marshal.FinalReleaseComObject(factory); }
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindingContext,
        ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
