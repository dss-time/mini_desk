using System.Text.Json;
using System.Text.Json.Serialization;
using MiniDesk.Models;

namespace MiniDesk.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    public string ConfigPath { get; }

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk", "workspace.json");
    }

    public async Task<WorkspaceConfig> LoadAsync()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                await using var stream = File.OpenRead(ConfigPath);
                var loaded = await JsonSerializer.DeserializeAsync<WorkspaceConfig>(stream, JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            TryWriteRecoveryLog(ex);
            TryBackupCorruptConfig();
        }
        return CreateDefault();
    }

    public async Task SaveAsync(WorkspaceConfig config)
    {
        await _gate.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(directory);
            var temp = ConfigPath + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
            File.Move(temp, ConfigPath, true);
        }
        finally { _gate.Release(); }
    }

    private static WorkspaceConfig CreateDefault()
    {
        var config = new WorkspaceConfig();
        var l = LocalizationService.Current;
        config.Groups.Add(NewGroup(l.Get("Default_Music"), "\uE8D6", "#F54474", 105, 60, 310, 190, GroupDisplayState.Collapsed));
        config.Groups.Add(NewGroup(l.Get("Default_Development"), "\uE943", "#2675F5", 350, 70, 430, 270));
        config.Groups.Add(NewGroup(l.Get("Default_Communication"), "\uE8BD", "#2ABB75", 810, 90, 260, 220, GroupDisplayState.Minimized));
        config.Groups.Add(NewGroup(l.Get("Default_Office"), "\uE821", "#5D62F4", 930, 55, 390, 250));
        config.Groups.Add(NewGroup(l.Get("Default_TemporaryFiles"), "\uE8B7", "#F2B84B", 1240, 80, 330, 240));
        return config;
    }

    public static CategoryGroup NewGroup(string? name = null, string glyph = "\uE7C3", string accent = "#1769F7",
        double left = 240, double top = 140, double width = 380, double height = 250,
        GroupDisplayState state = GroupDisplayState.Expanded, string iconPath = "") => new()
    {
        Name = name ?? LocalizationService.Current.Get("Default_NewGroup"), Glyph = glyph, Accent = accent, Left = left, Top = top,
        Width = width, Height = height, State = state, IconPath = iconPath
    };

    private void TryWriteRecoveryLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(ConfigPath)!, "load-error.txt"), ex.ToString());
        }
        catch { }
    }

    private void TryBackupCorruptConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var backup = Path.Combine(Path.GetDirectoryName(ConfigPath)!, $"workspace.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, backup, false);
        }
        catch { }
    }
}
