using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk;

public partial class MainWindow : Window
{
    private readonly WorkspaceConfig _config;
    private readonly Func<Task> _save;
    private readonly Action _refreshWindows;
    private readonly Func<CategoryGroup, Task> _delete;
    private readonly Func<CategoryGroup, bool> _confirmDelete;
    private readonly SnapshotService? _snapshotService;
    private readonly Func<string, bool, Task<LayoutSnapshot>>? _createSnapshot;
    private readonly Func<LayoutSnapshot, Task>? _restoreSnapshot;
    private readonly Action<AppThemeMode>? _themeChanged;
    private readonly GroupIconStorageService _groupIconStorage = new();
    private CancellationTokenSource? _appearanceSave;

    public MainWindow(WorkspaceConfig config, Func<Task> save, Action refreshWindows, Func<CategoryGroup, Task> delete,
        Func<CategoryGroup, bool>? confirmDelete = null, SnapshotService? snapshotService = null,
        Func<string, bool, Task<LayoutSnapshot>>? createSnapshot = null,
        Func<LayoutSnapshot, Task>? restoreSnapshot = null, Action<AppThemeMode>? themeChanged = null)
    {
        InitializeComponent();
        _config = config;
        _save = save;
        _refreshWindows = refreshWindows;
        _delete = delete;
        _confirmDelete = confirmDelete ?? (group => MessageBox.Show(this,
            LocalizationService.Current.Format("Message_DeleteGroup", group.Name), LocalizationService.Current.Get("Message_DeleteGroupTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        _snapshotService = snapshotService;
        _createSnapshot = createSnapshot;
        _restoreSnapshot = restoreSnapshot;
        _themeChanged = themeChanged;
        DataContext = config;
        GroupList.ItemsSource = config.Groups;
        MonitorList.ItemsSource = DisplayTopologyService.Current.Monitors;
        Loaded += (_, _) =>
        {
            // Keep the manager inside the active work area at high DPI and on smaller displays.
            var workArea = SystemParameters.WorkArea;
            MaxWidth = Math.Max(MinWidth, workArea.Width * 0.96);
            MaxHeight = Math.Max(MinHeight, workArea.Height * 0.94);
            MinWidth = Math.Min(MinWidth, MaxWidth);
            MinHeight = Math.Min(MinHeight, MaxHeight);
            Width = Math.Min(Width, MaxWidth);
            Height = Math.Min(Height, MaxHeight);
            RefreshHomeIllustration();
            StartupCheck.IsChecked = StartupService.IsEnabled;
            SetCollapseStyleRadio();
            SetThemeRadio();
            LanguageSelector.SelectedIndex = string.Equals(_config.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            RegionSelector.SelectedIndex = _config.RegionCulture switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };
            ShowPage("Home");
        };
    }

    public void RefreshData()
    {
        GroupList.Items.Refresh();
        SetCollapseStyleRadio();
        SetThemeRadio();
    }

    private async void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageSelector.SelectedItem is not ComboBoxItem { Tag: string culture } ||
            string.Equals(_config.Language, culture, StringComparison.OrdinalIgnoreCase)) return;
        _config.Language = culture;
        LocalizationService.Current.SetCulture(culture);
        LocalizationService.Current.SetRegionCulture(_config.RegionCulture);
        await _save();
    }

    private async void Region_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || RegionSelector.SelectedItem is not ComboBoxItem { Tag: string culture }) return;
        _config.RegionCulture = culture;
        LocalizationService.Current.SetRegionCulture(culture);
        await _save();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        GroupsPage.Visibility = page == "Groups" ? Visibility.Visible : Visibility.Collapsed;
        SnapshotsPage.Visibility = page == "Snapshots" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        QuickPage.Visibility = page == "Quick" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { HomeNav, GroupsNav, SnapshotsNav, AppearanceNav, QuickNav, AboutNav })
            button.SetResourceReference(Control.BackgroundProperty, Equals(button.Tag, page) ? "SelectedBrush" : "SidebarBrush");
        if (page == "Snapshots") _ = ReloadSnapshotsAsync();
    }

    private void GoGroups_Click(object sender, RoutedEventArgs e) => ShowPage("Groups");

    private void RefreshMonitors_Click(object sender, RoutedEventArgs e)
    {
        DisplayTopologyService.Current.Refresh();
        MonitorList.ItemsSource = DisplayTopologyService.Current.Monitors.ToList();
    }

    private void PreviewExpanded_Click(object sender, RoutedEventArgs e) => SetPreviewMode(0);
    private void PreviewCollapsed_Click(object sender, RoutedEventArgs e) => SetPreviewMode(1);
    private void PreviewIconOnly_Click(object sender, RoutedEventArgs e) => SetPreviewMode(2);

    private void SetPreviewMode(int mode)
    {
        PreviewPanel.Width = mode == 2 ? 50 : 260;
        PreviewPanel.Height = mode == 0 ? 104 : mode == 1 ? 42 : 50;
        PreviewExpandedContents.Margin = mode == 0 ? new Thickness(12) : new Thickness(4);
        PreviewExpandedContents.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewCollapsedContent.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviewIconOnlyContent.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        PreviewExpandedButton.SetResourceReference(StyleProperty, mode == 0 ? "PrimaryButton" : "SecondaryButton");
        PreviewCollapsedButton.SetResourceReference(StyleProperty, mode == 1 ? "PrimaryButton" : "SecondaryButton");
        PreviewIconOnlyButton.SetResourceReference(StyleProperty, mode == 2 ? "PrimaryButton" : "SecondaryButton");
    }

    private void StartupAppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        StartupCheck.IsChecked = sender is CheckBox checkBox && checkBox.IsChecked == true;
        StartupChanged(sender, e);
    }

    private async void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateGroupWindow { Owner = this };
        SetModalOverlay(true);
        bool accepted;
        try { accepted = dialog.ShowDialog() == true; }
        finally { SetModalOverlay(false); }
        if (!accepted || dialog.Result is null) return;
        var result = dialog.Result;
        string iconPath;
        try { iconPath = result.UploadPath is null ? string.Empty : _groupIconStorage.Store(result.UploadPath); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Current.Get("Message_IconSaveFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var count = _config.Groups.Count;
        var group = ConfigService.NewGroup(result.Name, result.Glyph, result.Accent,
            260 + count * 18, 140 + count * 15, state: result.State, iconPath: iconPath);
        group.CollapseStyle = result.CollapseStyle;
        group.IsEnabled = result.ShowOnDesktop;
        _config.Groups.Add(group);
        _refreshWindows();
        await _save();
        GroupList.Items.Refresh();
    }

    internal void SetModalOverlay(bool visible) => ModalOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CategoryGroup group }) return;
        var dialog = new TextPromptWindow(LocalizationService.Current.Get("Prompt_RenameGroup"), LocalizationService.Current.Get("Prompt_GroupName"), group.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        group.Name = dialog.Result.Trim();
        await _save();
    }

    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CategoryGroup group }) return;
        if (_confirmDelete(group))
            await _delete(group);
    }

    private async void GroupState_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not ComboBox { DataContext: CategoryGroup group } combo || combo.SelectedIndex < 0) return;
        if (group.StateIndex == combo.SelectedIndex) return;
        group.StateIndex = combo.SelectedIndex;
        _refreshWindows();
        await _save();
    }

    private async void GroupEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _refreshWindows();
        await _save();
    }

    private void SetCollapseStyleRadio()
    {
        var style = _config.Groups.FirstOrDefault()?.CollapseStyle ?? CollapseStyle.HorizontalCapsule;
        CapsuleStyle.IsChecked = style == CollapseStyle.HorizontalCapsule;
        VerticalStyle.IsChecked = style == CollapseStyle.VerticalIcon;
        IconStyle.IsChecked = style == CollapseStyle.IconOnly;
    }

    private void SetThemeRadio()
    {
        SystemTheme.IsChecked = _config.ThemeMode == AppThemeMode.System;
        LightTheme.IsChecked = _config.ThemeMode == AppThemeMode.Light;
        DarkTheme.IsChecked = _config.ThemeMode == AppThemeMode.Dark;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string value } || !Enum.TryParse<AppThemeMode>(value, out var mode)) return;
        _themeChanged?.Invoke(mode);
    }

    public void RefreshTheme()
    {
        RefreshHomeIllustration();
        ShowPage(VisiblePage());
    }

    private void RefreshHomeIllustration() => HomeIllustration.Visibility =
        Application.Current.Resources["IsDarkTheme"] is true ? Visibility.Collapsed : Visibility.Visible;

    private string VisiblePage() => GroupsPage.IsVisible ? "Groups" : SnapshotsPage.IsVisible ? "Snapshots" :
        AppearancePage.IsVisible ? "Appearance" : QuickPage.IsVisible ? "Quick" : AboutPage.IsVisible ? "About" : "Home";

    private async Task ReloadSnapshotsAsync()
    {
        if (_snapshotService is not null) SnapshotList.ItemsSource = await _snapshotService.ListAsync();
    }

    private async void CreateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_createSnapshot is null) return;
        var dialog = new TextPromptWindow(LocalizationService.Current.Get("Prompt_CreateSnapshot"), LocalizationService.Current.Get("Prompt_SnapshotName"),
            LocalizationService.Current.Format("Prompt_DefaultSnapshot", DateTime.Now.ToString("g", LocalizationService.Current.FormatCulture))) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await _createSnapshot(dialog.Result, false);
        await ReloadSnapshotsAsync();
    }

    private void PreviewSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LayoutSnapshot snapshot }) new SnapshotPreviewWindow(snapshot) { Owner = this }.ShowDialog();
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_restoreSnapshot is null || sender is not FrameworkElement { DataContext: LayoutSnapshot snapshot }) return;
        if (MessageBox.Show(this, LocalizationService.Current.Format("Prompt_RestoreSnapshot", snapshot.Name), LocalizationService.Current.Get("Prompt_RestoreSnapshotTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _restoreSnapshot(snapshot);
        GroupList.Items.Refresh();
    }

    private async void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshotService is null || sender is not FrameworkElement { DataContext: LayoutSnapshot snapshot }) return;
        if (MessageBox.Show(this, LocalizationService.Current.Format("Prompt_DeleteSnapshot", snapshot.Name), LocalizationService.Current.Get("Prompt_DeleteSnapshotTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _snapshotService.DeleteAsync(snapshot.Id);
        await ReloadSnapshotsAsync();
    }

    private void SnapshotMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void CollapseStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string value } || !Enum.TryParse<CollapseStyle>(value, out var style)) return;
        foreach (var group in _config.Groups) group.CollapseStyle = style;
        _refreshWindows();
        DebounceSaveAppearance();
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        // ValueChanged can be raised before the binding source is updated. Copy the
        // live slider value first so every visible step is applied to desktop panels.
        if (ReferenceEquals(sender, OpacitySlider)) _config.PanelOpacity = OpacitySlider.Value;
        else if (ReferenceEquals(sender, RadiusSlider)) _config.CornerRadius = RadiusSlider.Value;
        else if (ReferenceEquals(sender, HeaderSlider)) _config.HeaderHeight = HeaderSlider.Value;
        _refreshWindows();
        DebounceSaveAppearance();
    }

    private async void AttachChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        await _save();
    }

    private void DebounceSaveAppearance()
    {
        _appearanceSave?.Cancel();
        var cts = _appearanceSave = new CancellationTokenSource();
        _ = SaveAfterQuietPeriodAsync(cts.Token);
    }

    private async Task SaveAfterQuietPeriodAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(320, token);
            if (!token.IsCancellationRequested) await _save();
        }
        catch (OperationCanceledException) { }
    }

    private async void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        try { StartupService.SetEnabled(StartupCheck.IsChecked == true); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, LocalizationService.Current.Get("Message_StartupFailed"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        await _save();
    }

    private async void RecoveryChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) await _save();
    }

    private async void ExpandAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _config.Groups) group.State = GroupDisplayState.Expanded;
        _refreshWindows();
        await _save();
    }

    private async void CollapseAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var group in _config.Groups) group.State = GroupDisplayState.Collapsed;
        _refreshWindows();
        await _save();
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}
