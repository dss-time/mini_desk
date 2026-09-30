using System.Runtime.InteropServices;
using MiniDesk.Models;

namespace MiniDesk.Services;

public sealed class DesktopOrganizationService
{
    private readonly DesktopVisibilityService _visibility;
    private readonly Func<Task> _persist;
    private readonly Func<Task> _snapshot;
    private bool _busy;

    public DesktopOrganizationService(DesktopVisibilityService visibility, Func<Task> persist, Func<Task> snapshot)
        => (_visibility, _persist, _snapshot) = (visibility, persist, snapshot);

    private static readonly (string Key, string Resource, string Glyph, string Accent)[] Categories =
    [
        ("office", "Default_Office", "\uE821", "#5D62F4"),
        ("development", "Default_Development", "\uE943", "#2675F5"),
        ("communication", "Default_Communication", "\uE8BD", "#28B978"),
        ("music", "Default_Music", "\uE8D6", "#F54474"),
        ("video", "Organize_Video", "\uE714", "#F05A70"),
        ("games", "Icon_Game", "\uE7FC", "#6F5BF5"),
        ("pictures", "Icon_Picture", "\uEB9F", "#F05A70"),
        ("browser", "Icon_Browser", "\uE774", "#2675F5"),
        ("finance", "Icon_Finance", "\uE9D2", "#D99B28"),
        ("proxy", "Icon_Proxy", "\uE705", "#5967D8"),
        ("word", "Icon_Word", "\uE7C3", "#2B579A"),
        ("excel", "Icon_Excel", "\uF0E2", "#217346"),
        ("ppt", "Icon_PowerPoint", "\uE786", "#D24726"),
        ("pdf", "Icon_Pdf", "\uEA90", "#DF3B38")
    ];

    public static List<OrganizationTarget> Targets(WorkspaceConfig config)
    {
        var targets = config.Groups.Select(g => new OrganizationTarget("existing", g.Name, g.Glyph, g.Accent, g.Id)).ToList();
        foreach (var c in Categories)
        {
            var name = LocalizationService.Current.Get(c.Resource);
            // Preserve both Chinese and English existing category names, even after switching language.
            var manager = new System.Resources.ResourceManager("MiniDesk.Resources.Strings", typeof(DesktopOrganizationService).Assembly);
            var aliases = new[] { name, manager.GetString(c.Resource, CultureInfo.GetCultureInfo("zh-CN")), manager.GetString(c.Resource, CultureInfo.GetCultureInfo("en-US")) };
            var existing = targets.FirstOrDefault(t => aliases.Any(a => string.Equals(a, t.Name, StringComparison.OrdinalIgnoreCase)));
            targets.Add(new OrganizationTarget(c.Key, existing?.Name ?? name, c.Glyph, c.Accent, existing?.GroupId));
        }
        return targets;
    }

    public static string? Classify(string path, string? shortcutTarget = null)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".lnk")
        {
            // Exact executable identity, never guess from shortcut titles or generic words.
            return System.IO.Path.GetFileName(shortcutTarget ?? "").ToLowerInvariant() switch
            {
                "code.exe" or "devenv.exe" or "idea64.exe" or "goland64.exe" or "pycharm64.exe" or "datagrip64.exe" or "postman.exe" => "development",
                "wechat.exe" or "weixin.exe" or "qq.exe" or "telegram.exe" or "discord.exe" => "communication",
                "cloudmusic.exe" or "spotify.exe" or "qqmusic.exe" => "music",
                "vlc.exe" or "potplayermini64.exe" or "potplayermini.exe" => "video",
                "steam.exe" or "epicgameslauncher.exe" => "games",
                "chrome.exe" or "msedge.exe" or "firefox.exe" or "brave.exe" => "browser",
                "v2rayn.exe" or "clash.exe" or "clash-verge.exe" => "proxy",
                "tradingview.exe" => "finance",
                "winword.exe" => "word", "excel.exe" => "excel", "powerpnt.exe" => "ppt", "acrord32.exe" or "acrobat.exe" => "pdf",
                _ => null
            };
        }
        return ext switch
        {
            ".doc" or ".docx" or ".rtf" or ".odt" => "word",
            ".xls" or ".xlsx" or ".xlsm" or ".csv" or ".ods" => "excel",
            ".ppt" or ".pptx" or ".odp" => "ppt",
            ".pdf" => "pdf",
            ".mp3" or ".flac" or ".wav" or ".m4a" or ".ogg" => "music",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "video",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".heic" => "pictures",
            _ => null
        };
    }

    public static List<OrganizationCandidate> Scan(WorkspaceConfig config, IEnumerable<string> folders, Func<string, string?>? resolveShortcut = null)
    {
        var grouped = config.Groups.SelectMany(g => g.Items).SelectMany(i => new[] { i.Path, i.OriginalDesktopPath ?? i.Path }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = Targets(config);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OrganizationCandidate>();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] paths;
            try { paths = Directory.GetFiles(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var path in paths.OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!seen.Add(path) || grouped.Contains(path)) continue;
                try
                {
                    var attrs = File.GetAttributes(path);
                    if ((attrs & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                    var targetPath = Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? resolveShortcut?.Invoke(path) : null;
                    if (string.Equals(Path.GetFileName(targetPath), "MiniDesk.exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("MiniDesk.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var key = Classify(path, targetPath);
                    var target = targets.FirstOrDefault(t => t.Key == key);
                    if (key is "word" or "excel" or "ppt" or "pdf" && target?.GroupId is null) target = targets.First(t => t.Key == "office");
                    // A disabled category is a deliberate user choice, not an invitation to re-enable it.
                    if (target?.GroupId is Guid id && config.Groups.First(g => g.Id == id).IsEnabled == false) target = null;
                    result.Add(new OrganizationCandidate { Path = path, Name = Path.GetFileName(path), Target = target });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException) { }
            }
        }
        return result;
    }

    public static Task<List<OrganizationCandidate>> ScanDesktopAsync(WorkspaceConfig config)
    {
        var copy = SnapshotService.Clone(config);
        var completion = new TaskCompletionSource<List<OrganizationCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type is not null) shell = Activator.CreateInstance(type);
                string? Resolve(string path)
                {
                    object? link = null;
                    try
                    {
                        if (shell is null) return null;
                        link = ((dynamic)shell).CreateShortcut(path);
                        return (string)((dynamic)link).TargetPath;
                    }
                    catch (Exception) { return null; }
                    finally { if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
                }
                completion.SetResult(Scan(copy, new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }, Resolve));
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        }) { IsBackground = true, Name = "MiniDesk desktop preview" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public async Task<(int Applied, int Failed)> ApplyAsync(WorkspaceConfig config, IEnumerable<OrganizationCandidate> candidates)
    {
        if (_busy) throw new InvalidOperationException(LocalizationService.Current.Get("Organize_Busy"));
        _busy = true;
        try
        {
            if (config.LastOrganization?.Entries.Any(e => e.RestorePending) == true)
                throw new InvalidOperationException(LocalizationService.Current.Get("Organize_RetryUndo"));
            await _snapshot(); // Must succeed before any desktop mutation.
            var batch = new OrganizationBatch();
            var previous = config.LastOrganization;
            config.LastOrganization = batch;
            try { await _persist(); } catch { config.LastOrganization = previous; throw; }
            int failed = 0;
            foreach (var candidate in candidates.Where(c => c.Target is not null).ToArray())
            {
                var target = candidate.Target!;
                if (!File.Exists(candidate.Path) || !_visibility.IsDesktopPath(candidate.Path) || config.Groups.SelectMany(g => g.Items).Any(i => string.Equals(i.Path, candidate.Path, StringComparison.OrdinalIgnoreCase) || string.Equals(i.OriginalDesktopPath, candidate.Path, StringComparison.OrdinalIgnoreCase))) { failed++; continue; }
                var group = target.GroupId is Guid id ? config.Groups.FirstOrDefault(g => g.Id == id) : config.Groups.FirstOrDefault(g => g.Name == target.Name);
                if (target.GroupId is not null && group is null) { failed++; continue; }
                bool created = group is null;
                group ??= ConfigService.NewGroup(target.Name, target.Glyph, target.Accent, left: 240 + config.Groups.Count * 16, top: 140 + config.Groups.Count * 12);
                if (created) config.Groups.Add(group);
                if (!DesktopItemService.AddPath(group, candidate.Path, out var item) || item is null) { failed++; continue; }
                try
                {
                    // Write-ahead visibility metadata lets startup finish an interrupted operation.
                    item.OriginalDesktopPath = Path.GetFullPath(candidate.Path);
                    item.OriginalFileAttributes = (int)File.GetAttributes(candidate.Path);
                    item.VisibilityMode = Path.GetExtension(candidate.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? DesktopVisibilityMode.ManagedShortcut : DesktopVisibilityMode.HiddenAttribute;
                    if (item.VisibilityMode == DesktopVisibilityMode.ManagedShortcut) item.Path = Path.Combine(_visibility.StorageFolder, $"{item.Id:N}.lnk");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { group.Items.Remove(item); group.NotifyCount(); if (created) config.Groups.Remove(group); failed++; continue; }
                var entry = new OrganizationEntry { GroupId = group.Id, ItemId = item.Id, Path = item.Path, OriginalPath = candidate.Path, X = item.X, Y = item.Y };
                batch.Entries.Add(entry);
                try { await _persist(); }
                catch { batch.Entries.Remove(entry); group.Items.Remove(item); group.NotifyCount(); if (created) config.Groups.Remove(group); throw; }
                if (!_visibility.Hide(item))
                {
                    batch.Entries.Remove(entry); group.Items.Remove(item); group.NotifyCount();
                    if (created && group.Items.Count == 0) config.Groups.Remove(group);
                    failed++;
                    await _persist();
                }
            }
            return (batch.Entries.Count, failed);
        }
        finally { _busy = false; }
    }

    public async Task<(int Restored, int Skipped)> UndoAsync(WorkspaceConfig config, bool pendingOnly = false)
    {
        if (_busy) throw new InvalidOperationException(LocalizationService.Current.Get("Organize_Busy"));
        _busy = true;
        try
        {
            var batch = config.LastOrganization;
            if (batch is null) return (0, 0);
            int restored = 0, skipped = 0;
            foreach (var entry in batch.Entries.ToArray())
            {
                if (pendingOnly && !entry.RestorePending) continue;
                var group = config.Groups.FirstOrDefault(g => g.Id == entry.GroupId);
                var item = group?.Items.FirstOrDefault(i => i.Id == entry.ItemId);
                if (item is null || item.Path != entry.Path || item.X != entry.X || item.Y != entry.Y || (!entry.RestorePending && item.OriginalDesktopPath != entry.OriginalPath)) { skipped++; if (!pendingOnly) batch.Entries.Remove(entry); continue; }
                entry.RestorePending = true;
                await _persist();
                // If a crash occurred after restoring the shortcut but before saving, do not hide it again.
                bool alreadyRestored = item.VisibilityMode == DesktopVisibilityMode.ManagedShortcut && !File.Exists(item.Path) && File.Exists(entry.OriginalPath);
                if (alreadyRestored || _visibility.Restore(item))
                { group!.Items.Remove(item); group.NotifyCount(); batch.Entries.Remove(entry); restored++; }
                else { skipped++; }
                await _persist();
            }
            await _persist();
            return (restored, skipped);
        }
        finally { _busy = false; }
    }
}
