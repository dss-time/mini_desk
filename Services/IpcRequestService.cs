using System.Text.Json;

namespace MiniDesk.Services;

public sealed record LaunchRequest(string Action, string? Path = null);

public static class IpcRequestService
{
    private static readonly string RequestDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk", "Requests");

    public static LaunchRequest Parse(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "--add", StringComparison.OrdinalIgnoreCase))
            return new LaunchRequest("add", args[1]);
        if (args.Any(a => string.Equals(a, "--new-group", StringComparison.OrdinalIgnoreCase)))
            return new LaunchRequest("new-group");
        if (args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            return new LaunchRequest("settings");
        if (args.Any(a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase)))
            return new LaunchRequest("background");
        return new LaunchRequest("open");
    }

    public static void Enqueue(string[] args)
    {
        Directory.CreateDirectory(RequestDirectory);
        var request = Parse(args);
        var file = System.IO.Path.Combine(RequestDirectory, $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(request));
    }

    public static IReadOnlyList<LaunchRequest> Drain()
    {
        if (!Directory.Exists(RequestDirectory)) return [];
        var requests = new List<LaunchRequest>();
        foreach (var file in Directory.EnumerateFiles(RequestDirectory, "*.json").OrderBy(path => path))
        {
            try
            {
                var request = JsonSerializer.Deserialize<LaunchRequest>(File.ReadAllText(file));
                if (request is not null) requests.Add(request);
                File.Delete(file);
            }
            catch { }
        }
        return requests;
    }
}
