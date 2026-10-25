using System.Collections.Concurrent;
using System.Windows.Media;

namespace MiniDesk.Services;

/// <summary>Uses Windows Shell icons and the user's file associations for built-in choices.</summary>
public static class GroupSystemIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource> BuiltInIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Task> Warmup = new(StartWarmup);
    private static Task StartWarmup()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
        var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0;
        try
        {
        foreach (var glyph in new[] { "\uE8D6", "\uE943", "\uE8BD", "\uE821", "\uE8B7", "\uE774", "\uE7FC", "\uEB9F", "\uE8A5", "\uE896", "\uE734", "\uE712", "\uE9D2", "\uE705", "\uF0E2", "\uE786", "\uEA90", "\uE7C3" })
            GetIcon(glyph);
        completion.SetResult();
        }
        catch (Exception ex) { completion.SetException(ex); }
        finally { if (initialized) CoUninitialize(); }
        }) { IsBackground = true, Name = "MiniDesk icon preload" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    [System.Runtime.InteropServices.DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [System.Runtime.InteropServices.DllImport("ole32.dll")] private static extern void CoUninitialize();
    public static Task WarmAsync() => Warmup.Value;
    public static System.Windows.Media.ImageSource? GetIcon(string? glyph)
    {
        var path = IconPath(glyph);
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (BuiltInIcons.TryGetValue(path, out var cached)) return cached;
        var icon = ShellIconService.GetIcon(path);
        // Built-ins remain available after a large desktop scan evicts ordinary
        // file icons. The fixed catalog needs fewer than 32 shared image sources.
        if (icon is not null && BuiltInIcons.Count < 32) BuiltInIcons.TryAdd(path, icon);
        return icon;
    }

    public static System.Windows.Media.ImageSource? GetCachedIcon(string? glyph)
    {
        var path = IconPath(glyph);
        if (string.IsNullOrWhiteSpace(path)) return null;
        return BuiltInIcons.TryGetValue(path, out var cached) ? cached : ShellIconService.GetCachedIcon(path);
    }

    private static string IconPath(string? glyph)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return glyph switch
        {
            "\uE8D6" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "\uE943" => Path.Combine(system, "control.exe"),
            "\uE8BD" => "MiniDesk.eml",
            "\uE821" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "\uE7C3" => "MiniDesk.docx",
            "\uE8B7" => Path.GetTempPath(),
            "\uE774" => "MiniDesk.html",
            "\uE7FC" => Path.Combine(system, "joy.cpl"),
            "\uEB9F" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "\uE8A5" => "MiniDesk.txt",
            "\uE896" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "\uE734" => Environment.GetFolderPath(Environment.SpecialFolder.Favorites),
            "\uE9D2" or "\uF0E2" => "MiniDesk.xlsx",
            "\uE705" => "MiniDesk.url",
            "\uE786" => "MiniDesk.pptx",
            "\uEA90" => "MiniDesk.pdf",
            _ => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
    }
}
