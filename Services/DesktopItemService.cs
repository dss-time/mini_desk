using System.Windows;
using MiniDesk.Models;

namespace MiniDesk.Services;

public static class DesktopItemService
{
    public static bool AddPath(CategoryGroup group, string path, Point? requestedPosition = null)
        => AddPath(group, path, out _, requestedPosition);

    public static bool AddPath(CategoryGroup group, string path, out DesktopItem? addedItem, Point? requestedPosition = null)
    {
        addedItem = null;
        var fullPath = Path.GetFullPath(path);
        if (group.Items.Any(item => string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase))) return false;

        var index = group.Items.Count;
        var columns = Math.Max(1, (int)((group.Width - 16) / 86));
        var position = requestedPosition ?? new Point(8 + index % columns * 86, 8 + index / columns * 90);
        var name = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath).Name
            : Path.GetFileNameWithoutExtension(fullPath);

        addedItem = new DesktopItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(fullPath) : name,
            Path = fullPath,
            X = Math.Max(0, position.X),
            Y = Math.Max(0, position.Y)
        };
        group.Items.Add(addedItem);
        group.NotifyCount();
        return true;
    }
}
