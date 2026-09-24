using Microsoft.Win32;

namespace MiniDesk.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MiniDesk";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException(LocalizationService.Current.Get("Error_ExecutableMissing"));
            key.SetValue(AppName, $"\"{exe}\" --background");
        }
        else key.DeleteValue(AppName, false);
    }
}
