using System.Windows;
using MiniDesk.Models;

namespace MiniDesk.Services;

public static class GroupItemTransferService
{
    public static bool TryMove(WorkspaceConfig config, Guid sourceId, DesktopItem item, Guid? targetId,
        Point position, Func<DesktopItem, bool> restore)
    {
        var source = config.Groups.FirstOrDefault(g => g.Id == sourceId);
        if (source is null || !source.Items.Contains(item)) return false;
        if (targetId is Guid id)
        {
            var target = config.Groups.FirstOrDefault(g => g.Id == id);
            if (target is null || ReferenceEquals(target, source)) return false;
            var identity = item.OriginalDesktopPath ?? item.Path;
            if (target.Items.Any(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.OriginalDesktopPath ?? i.Path, identity, StringComparison.OrdinalIgnoreCase))) return false;
            source.Items.Remove(item);
            item.X = Math.Max(0, position.X); item.Y = Math.Max(0, position.Y);
            target.Items.Add(item); target.NotifyCount();
        }
        else
        {
            if (!restore(item)) return false; // Never remove the relation before restoring successfully.
            source.Items.Remove(item);
        }
        source.NotifyCount();
        return true;
    }
}
