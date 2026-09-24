using Microsoft.Win32;

namespace MiniDesk.Services;

public static class ShellContextMenuService
{
    private const string FileVerb = @"Software\Classes\*\shell\MiniDesk.Add";
    private const string DirectoryVerb = @"Software\Classes\Directory\shell\MiniDesk.Add";
    private const string BackgroundVerb = @"Software\Classes\DesktopBackground\shell\MiniDesk.NewGroup";

    public static void EnsureRegistered()
    {
        var l = LocalizationService.Current;
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(l.Get("Error_ExecutableMissing"));
        RegisterVerb(FileVerb, l.Get("Shell_Add"), exe, $"\"{exe}\" --add \"%1\"");
        RegisterVerb(DirectoryVerb, l.Get("Shell_Add"), exe, $"\"{exe}\" --add \"%1\"");
        RegisterVerb(BackgroundVerb, l.Get("Shell_NewGroup"), exe, $"\"{exe}\" --new-group");
    }

    private static void RegisterVerb(string path, string title, string icon, string command)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(path, true);
        verb.SetValue("MUIVerb", title);
        verb.SetValue("Icon", $"\"{icon}\",0");
        verb.SetValue("MultiSelectModel", "Single");
        using var commandKey = verb.CreateSubKey("command", true);
        commandKey.SetValue(null, command);
    }
}
