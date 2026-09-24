using System.Text.Json;
using System.Text.Json.Serialization;
using MiniDesk.Models;

namespace MiniDesk.Services;

public sealed class SnapshotService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string Folder { get; }

    public SnapshotService(string? folder = null) => Folder = folder ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk", "Snapshots");

    public async Task<LayoutSnapshot> CreateAsync(WorkspaceConfig config, string name, bool automatic = false)
    {
        var snapshot = new LayoutSnapshot
        {
            Name = string.IsNullOrWhiteSpace(name) ? LocalizationService.Current.Get("Default_Snapshot") : name.Trim(),
            IsAutomatic = automatic,
            Layout = Clone(config)
        };
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Folder);
            var final = GetPath(snapshot.Id);
            var temp = final + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, Options));
            File.Move(temp, final, true);
        }
        finally { _gate.Release(); }
        return snapshot;
    }

    public async Task<IReadOnlyList<LayoutSnapshot>> ListAsync()
    {
        if (!Directory.Exists(Folder)) return [];
        var result = new List<LayoutSnapshot>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<LayoutSnapshot>(await File.ReadAllTextAsync(file), Options);
                if (item is not null) result.Add(item);
            }
            catch { }
        }
        return result.OrderByDescending(item => item.CreatedAt).ToList();
    }

    public Task DeleteAsync(Guid id)
    {
        var path = GetPath(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static WorkspaceConfig Clone(WorkspaceConfig source) =>
        JsonSerializer.Deserialize<WorkspaceConfig>(JsonSerializer.Serialize(source, Options), Options) ?? new WorkspaceConfig();

    private string GetPath(Guid id) => Path.Combine(Folder, $"{id:N}.json");
}
