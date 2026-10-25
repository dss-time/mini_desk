using System.Windows;
using MiniDesk.Models;

namespace MiniDesk.Services;

internal static class DesktopGroupWindowPool
{
    internal static void Update(WorkspaceConfig config, Dictionary<Guid, DesktopGroupWindow> windows,
        Func<CategoryGroup, DesktopGroupWindow> create)
    {
        var enabled = config.Groups.Where(group => group.IsEnabled).ToList();
        var ids = enabled.Select(group => group.Id).ToHashSet();
        foreach (var stale in windows.Where(pair => !ids.Contains(pair.Key)).ToList())
        {
            stale.Value.AllowClose = true;
            stale.Value.Close();
            windows.Remove(stale.Key);
        }
        foreach (var group in enabled)
        {
            if (!windows.TryGetValue(group.Id, out var window))
            {
                window = create(group);
                windows[group.Id] = window;
                window.Show();
            }
            window.Visibility = Visibility.Visible;
            window.RefreshVisualState();
        }
    }
}
