using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using MiniDesk.Models;

namespace MiniDesk.Services;

public sealed class AppThemeService : IDisposable
{
    private AppThemeMode _mode;
    public event EventHandler? ThemeChanged;
    public bool IsDark { get; private set; }

    public AppThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public void Apply(AppThemeMode mode)
    {
        _mode = mode;
        IsDark = mode == AppThemeMode.Dark || mode == AppThemeMode.System && SystemUsesDarkMode();
        var resources = Application.Current.Resources;
        resources["IsDarkTheme"] = IsDark;
        resources["AppBackgroundBrush"] = Brush(IsDark ? "#171A21" : "#F7FAFE");
        resources["SidebarBrush"] = Brush(IsDark ? "#20242D" : "#F0F5FC");
        resources["CardBrush"] = Brush(IsDark ? "#292E39" : "#FFFFFF");
        resources["SurfaceBrush"] = Brush(IsDark ? "#292E39" : "#FFFFFF");
        resources["BorderBrush"] = Brush(IsDark ? "#3B4350" : "#DEE7F2");
        resources["TextBrush"] = Brush(IsDark ? "#F2F5FA" : "#13213D");
        resources["MutedBrush"] = Brush(IsDark ? "#A9B3C3" : "#66728A");
        resources["ControlBrush"] = Brush(IsDark ? "#303744" : "#EEF4FD");
        resources["SelectedBrush"] = Brush(IsDark ? "#243C60" : "#DBE9FD");
        resources["SuccessBrush"] = Brush(IsDark ? "#42C58A" : "#18A66A");
        resources["WarningBrush"] = Brush(IsDark ? "#F3B44F" : "#ED9A18");
        resources["DangerBrush"] = Brush(IsDark ? "#FF7788" : "#D9384E");
        resources["DisabledBrush"] = Brush(IsDark ? "#687386" : "#AAB6C7");
        resources["ColorBackground"] = Color(IsDark ? "#171A21" : "#F7FAFE");
        resources["ColorSurface"] = Color(IsDark ? "#292E39" : "#FFFFFF");
        resources["ColorBorder"] = Color(IsDark ? "#3B4350" : "#DEE7F2");
        resources["ColorTextPrimary"] = Color(IsDark ? "#F2F5FA" : "#13213D");
        resources["ColorTextSecondary"] = Color(IsDark ? "#A9B3C3" : "#66728A");
        resources["ColorDisabled"] = Color(IsDark ? "#687386" : "#AAB6C7");
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode == AppThemeMode.System) Application.Current.Dispatcher.BeginInvoke(() => Apply(_mode));
    }

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch { return false; }
    }

    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value)!;
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
}
