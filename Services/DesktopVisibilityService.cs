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
    public string? LastRestoreError { get; private set; }
    public bool RestoredToPersonalDesktop { get; private set; }

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
        LastRestoreError = null;
        RestoredToPersonalDesktop = false;
        try
        {
            if (item.VisibilityMode == DesktopVisibilityMode.ManagedShortcut)
            {
                var original = item.OriginalDesktopPath;
                if (string.IsNullOrWhiteSpace(original))
                    throw new InvalidOperationException(LocalizationService.Current.Get("Restore_OriginalPathMissing"));
                if (!File.Exists(item.Path))
                    throw new FileNotFoundException(LocalizationService.Current.Get("Restore_ShortcutMissing"), item.Path);
                string destination;
                try
                {
                    destination = MoveShortcut(item.Path, original);
                }
                catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is IOException) &&
                    string.Equals(NormalizeFolder(Path.GetDirectoryName(original)!), _desktopFolders[1], StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_desktopFolders[0], _desktopFolders[1], StringComparison.OrdinalIgnoreCase))
                {
                    // Public Desktop may permit deletion but not creation for this user.
                    // Move without overwriting; only commit model changes after success.
                    Log("restore-public-fallback", item.Path, ex);
                    destination = MoveShortcut(item.Path, Path.Combine(_desktopFolders[0], Path.GetFileName(original)));
                    RestoredToPersonalDesktop = true;
                }
                item.Path = destination;
                NotifyCreated(destination);
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
            LastRestoreError = ex.GetBaseException().Message;
            Log("restore", item.Path, ex);
            return false;
        }
    }

    private static string MoveShortcut(string source, string requested)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(requested)!);
        var destination = AvailableDestination(requested);
        File.Move(source, destination, false);
        return destination;
    }

    public void Reconcile(WorkspaceConfig config)
    {
        foreach (var item in config.Groups.SelectMany(group => group.Items))
        {
            if (config.LastOrganization?.Entries.Any(e => e.ItemId == item.Id && e.RestorePending) == true) continue;
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
