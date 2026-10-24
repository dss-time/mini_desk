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
        resources["AppBackgroundBrush"] = Brush(IsDark ? "#292E39" : "#E0E5EC");
        resources["SidebarBrush"] = Brush(IsDark ? "#292E39" : "#E0E5EC");
        resources["CardBrush"] = Brush(IsDark ? "#292E39" : "#E0E5EC");
        resources["SurfaceBrush"] = Brush(IsDark ? "#292E39" : "#E0E5EC");
        resources["BorderBrush"] = Brush(IsDark ? "#3B4350" : "#B8BCC2");
        resources["TextBrush"] = Brush(IsDark ? "#E0E5EC" : "#333333");
        resources["MutedBrush"] = Brush(IsDark ? "#AFB7C4" : "#596170");
        resources["ControlBrush"] = Brush(IsDark ? "#292E39" : "#E0E5EC");
        resources["SelectedBrush"] = Brush(IsDark ? "#35394A" : "#D8DDEC");
        resources["PrimaryBrush"] = Brush(IsDark ? "#B6A9FF" : "#5D4AD9");
        resources["SoftShadowColor"] = Color(IsDark ? "#1D2028" : "#B8BCC2");
        resources["SoftHighlightColor"] = Color(IsDark ? "#3D4454" : "#FFFFFF");
        resources["SuccessBrush"] = Brush(IsDark ? "#42C58A" : "#18A66A");
        resources["WarningBrush"] = Brush(IsDark ? "#F3B44F" : "#ED9A18");
        resources["DangerBrush"] = Brush(IsDark ? "#FF7788" : "#D9384E");
        resources["DisabledBrush"] = Brush(IsDark ? "#687386" : "#AAB6C7");
        resources["ColorBackground"] = Color(IsDark ? "#292E39" : "#E0E5EC");
        resources["ColorSurface"] = Color(IsDark ? "#292E39" : "#E0E5EC");
        resources["ColorBorder"] = Color(IsDark ? "#3B4350" : "#B8BCC2");
        resources["ColorTextPrimary"] = Color(IsDark ? "#E0E5EC" : "#333333");
        resources["ColorTextSecondary"] = Color(IsDark ? "#AFB7C4" : "#596170");
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
