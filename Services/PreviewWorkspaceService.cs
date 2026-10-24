using MiniDesk.Models;

namespace MiniDesk.Services;

/// <summary>A working local preview backed by the real services and a disposable desktop.</summary>
public sealed class PreviewWorkspaceService
{
    public string Root { get; }
    public string DesktopFolder { get; }
    public WorkspaceConfig Config { get; }
    public SnapshotService Snapshots { get; }
    public DesktopOrganizationService Organization { get; }
    private readonly ConfigService _config;
    private readonly DesktopVisibilityService _visibility;

    public PreviewWorkspaceService(string? root = null)
    {
        Root = root ?? Path.Combine(Path.GetTempPath(), "MiniDeskPreview", Guid.NewGuid().ToString("N"));
        DesktopFolder = Path.Combine(Root, "Desktop");
        Directory.CreateDirectory(DesktopFolder);
        Config = new WorkspaceConfig { ThemeMode = AppThemeMode.Light, AttachToDesktop = false };
        Config.Groups.Add(ConfigService.NewGroup("工作文件", "\uE821", "#5D62F4"));
        Config.Groups.Add(ConfigService.NewGroup("常用工具", "\uE943", "#2675F5"));
        Config.Groups.Add(ConfigService.NewGroup("灵感收藏", "\uE734", "#D99B28"));
        _config = new ConfigService(Path.Combine(Root, "workspace.json"));
        Snapshots = new SnapshotService(Path.Combine(Root, "Snapshots"));
        _visibility = new DesktopVisibilityService(DesktopFolder, Path.Combine(Root, "PublicDesktop"), Path.Combine(Root, "DesktopItems"));
        Organization = new DesktopOrganizationService(_visibility, SaveAsync,
            async () => { await CreateSnapshotAsync(LocalizationService.Current.Get("Organize_Snapshot"), true); });
        File.WriteAllText(Path.Combine(DesktopFolder, "项目清单.csv"), "任务,状态\n整理示例桌面,待处理\n");
        File.WriteAllText(Path.Combine(DesktopFolder, "会议记录.rtf"), "{\\rtf1\\ansi MiniDesk preview meeting notes}");
        File.WriteAllText(Path.Combine(DesktopFolder, "待归档.txt"), "MiniDesk preview document");
    }

    public Task SaveAsync() => _config.SaveAsync(Config);
    public async Task DeleteGroupAsync(CategoryGroup group)
    {
        foreach (var item in group.Items) _visibility.Restore(item);
        Config.Groups.Remove(group);
        await SaveAsync();
    }
    public Task<LayoutSnapshot> CreateSnapshotAsync(string name, bool automatic) => Snapshots.CreateAsync(Config, name, automatic);

    public Task<List<OrganizationCandidate>> ScanAsync()
    {
        var copy = SnapshotService.Clone(Config);
        return Task.Run(() =>
        {
            var candidates = DesktopOrganizationService.Scan(copy, [DesktopFolder]);
            if (copy.Groups.FirstOrDefault() is { } first)
            {
                var work = DesktopOrganizationService.Targets(copy).First(t => t.GroupId == first.Id);
                foreach (var candidate in candidates.Where(c => c.Target is not null)) candidate.Target = work;
            }
            return candidates;
        });
    }

    public async Task RestoreSnapshotAsync(LayoutSnapshot snapshot)
    {
        await CreateSnapshotAsync(LocalizationService.Current.Get("Snapshot_BeforeRestore"), true);
        foreach (var item in Config.Groups.SelectMany(g => g.Items)) _visibility.Restore(item);
        var restored = SnapshotService.Clone(snapshot.Layout);
        Config.Groups.Clear();
        foreach (var group in restored.Groups) Config.Groups.Add(group);
        Config.LastOrganization = restored.LastOrganization;
        Config.ThemeMode = restored.ThemeMode;
        Config.PanelOpacity = restored.PanelOpacity;
        Config.CornerRadius = restored.CornerRadius;
        Config.HeaderHeight = restored.HeaderHeight;
        Config.ShowBadge = restored.ShowBadge;
        Config.ShowCollapsedIcon = restored.ShowCollapsedIcon;
        _visibility.Reconcile(Config);
        await SaveAsync();
    }
}
