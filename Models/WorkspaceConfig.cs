using System.Collections.ObjectModel;

namespace MiniDesk.Models;

public enum AppThemeMode { System, Light, Dark }

public sealed class WorkspaceConfig : ObservableObject
{
    private double _panelOpacity = 0.80;
    private double _cornerRadius = 16;
    private double _headerHeight = 42;
    private bool _showCollapsedIcon = true;
    private bool _showBadge = true;
    private bool _attachToDesktop = true;
    private AppThemeMode _themeMode = AppThemeMode.System;
    private bool _restoreAfterExplorerRestart = true;
    private string _language = "zh-CN";
    private string _regionCulture = "system";

    public int Version { get; set; } = 5;
    public ObservableCollection<CategoryGroup> Groups { get; set; } = [];
    public double PanelOpacity { get => _panelOpacity; set => SetProperty(ref _panelOpacity, value); }
    public double CornerRadius { get => _cornerRadius; set => SetProperty(ref _cornerRadius, value); }
    public double HeaderHeight { get => _headerHeight; set => SetProperty(ref _headerHeight, value); }
    public bool ShowCollapsedIcon { get => _showCollapsedIcon; set => SetProperty(ref _showCollapsedIcon, value); }
    public bool ShowBadge { get => _showBadge; set => SetProperty(ref _showBadge, value); }
    public bool AttachToDesktop { get => _attachToDesktop; set => SetProperty(ref _attachToDesktop, value); }
    public AppThemeMode ThemeMode { get => _themeMode; set => SetProperty(ref _themeMode, value); }
    public bool RestoreAfterExplorerRestart { get => _restoreAfterExplorerRestart; set => SetProperty(ref _restoreAfterExplorerRestart, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }
    public string RegionCulture { get => _regionCulture; set => SetProperty(ref _regionCulture, value); }
}
