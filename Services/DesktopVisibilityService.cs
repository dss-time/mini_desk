using System.Runtime.InteropServices;
using MiniDesk.Models;

namespace MiniDesk.Services;

public sealed class DesktopVisibilityService
{
    private const uint ShcneCreate = 0x00000002;
    private const uint ShcneDelete = 0x00000004;
    private const uint ShcneAttributes = 0x00000800;
    private const uint ShcneUpdateDir = 0x00001000;
    private const uint ShcnfPathWFlush = 0x2005;
    private readonly string[] _desktopFolders;
    public string StorageFolder { get; }

    public DesktopVisibilityService(string? userDesktop = null, string? commonDesktop = null, string? storageFolder = null)
    {
        _desktopFolders =
        [
            NormalizeFolder(userDesktop ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            NormalizeFolder(commonDesktop ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory))
        ];
        StorageFolder = storageFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk", "DesktopItems");
    }

    public bool IsDesktopPath(string path)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return parent is not null && _desktopFolders.Contains(NormalizeFolder(parent), StringComparer.OrdinalIgnoreCase);
    }

    public bool Hide(DesktopItem item)
    {
        try
        {
            if (item.VisibilityMode != DesktopVisibilityMode.None) return EnsureHidden(item);
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path) || !IsDesktopPath(item.Path)) return false;

            if (File.Exists(item.Path) && string.Equals(Path.GetExtension(item.Path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(StorageFolder);
                var original = Path.GetFullPath(item.Path);
                var managed = Path.Combine(StorageFolder, $"{item.Id:N}.lnk");
                File.Move(original, managed, false);
                item.OriginalDesktopPath = original;
                item.Path = managed;
                item.VisibilityMode = DesktopVisibilityMode.ManagedShortcut;
                NotifyDeleted(original);
                return true;
            }

            var attributes = File.GetAttributes(item.Path);
            item.OriginalFileAttributes = (int)attributes;
            File.SetAttributes(item.Path, attributes | FileAttributes.Hidden);
            item.OriginalDesktopPath = Path.GetFullPath(item.Path);
            item.VisibilityMode = DesktopVisibilityMode.HiddenAttribute;
            NotifyAttributes(item.Path);
            return true;
        }
        catch (Exception ex)
        {
            Log("hide", item.Path, ex);
            return false;
        }
    }

    public bool Restore(DesktopItem item)
    {
        try
        {
            if (item.VisibilityMode == DesktopVisibilityMode.ManagedShortcut)
            {
                var original = item.OriginalDesktopPath;
                if (!string.IsNullOrWhiteSpace(original) && File.Exists(item.Path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    var destination = AvailableDestination(original);
                    File.Move(item.Path, destination, false);
                    item.Path = destination;
                    NotifyCreated(destination);
                }
            }
            else if (item.VisibilityMode == DesktopVisibilityMode.HiddenAttribute &&
                     (File.Exists(item.Path) || Directory.Exists(item.Path)))
            {
                File.SetAttributes(item.Path, (FileAttributes)item.OriginalFileAttributes);
                NotifyAttributes(item.Path);
            }
            item.VisibilityMode = DesktopVisibilityMode.None;
            item.OriginalDesktopPath = null;
            item.OriginalFileAttributes = 0;
            return true;
        }
        catch (Exception ex)
        {
            Log("restore", item.Path, ex);
            return false;
        }
    }

    public void Reconcile(WorkspaceConfig config)
    {
        foreach (var item in config.Groups.SelectMany(group => group.Items))
        {
            if (item.VisibilityMode == DesktopVisibilityMode.None) Hide(item);
            else EnsureHidden(item);
        }
    }

    private bool EnsureHidden(DesktopItem item)
    {
        try
        {
            if (item.VisibilityMode == DesktopVisibilityMode.ManagedShortcut)
            {
                if (File.Exists(item.Path))
                {
                    if (!string.IsNullOrWhiteSpace(item.OriginalDesktopPath)) NotifyDeleted(item.OriginalDesktopPath);
                    return true;
                }
                if (string.IsNullOrWhiteSpace(item.OriginalDesktopPath) || !File.Exists(item.OriginalDesktopPath)) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
                File.Move(item.OriginalDesktopPath, item.Path, false);
                NotifyDeleted(item.OriginalDesktopPath);
                return true;
            }
            if (item.VisibilityMode == DesktopVisibilityMode.HiddenAttribute && (File.Exists(item.Path) || Directory.Exists(item.Path)))
            {
                File.SetAttributes(item.Path, File.GetAttributes(item.Path) | FileAttributes.Hidden);
                NotifyAttributes(item.Path);
                return true;
            }
        }
        catch (Exception ex) { Log("reconcile", item.Path, ex); }
        return false;
    }

    private static string AvailableDestination(string requested)
    {
        if (!File.Exists(requested) && !Directory.Exists(requested)) return requested;
        var directory = Path.GetDirectoryName(requested)!;
        var name = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} (恢复 {i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{name} (恢复 {Guid.NewGuid():N}){extension}");
    }

    private static string NormalizeFolder(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static void NotifyDeleted(string path)
    {
        SHChangeNotify(ShcneDelete, ShcnfPathWFlush, path, IntPtr.Zero);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) SHChangeNotify(ShcneUpdateDir, ShcnfPathWFlush, parent, IntPtr.Zero);
    }

    private static void NotifyCreated(string path)
    {
        SHChangeNotify(ShcneCreate, ShcnfPathWFlush, path, IntPtr.Zero);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) SHChangeNotify(ShcneUpdateDir, ShcnfPathWFlush, parent, IntPtr.Zero);
    }

    private static void NotifyAttributes(string path) => SHChangeNotify(ShcneAttributes, ShcnfPathWFlush, path, IntPtr.Zero);

    private static void Log(string action, string path, Exception ex)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "desktop-visibility-errors.log"),
                $"{DateTimeOffset.Now:O}\t{action}\t{path}\t{ex.GetBaseException().Message}{Environment.NewLine}");
        }
        catch { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
}
