using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Input;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk;

public partial class MainWindow : Window
{
    internal bool IsPreview { get; init; }
    private bool _openingGroup;
    private int _groupPanelVersion;
    internal bool IsGroupPanelOpen => _openingGroup;
    private bool _snapshotBusy;
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly WorkspaceConfig _config;
    private readonly Func<Task> _save;
    private readonly Action _refreshWindows;
    private readonly Func<CategoryGroup, Task> _delete;
    private readonly Func<CategoryGroup, bool> _confirmDelete;
    private readonly SnapshotService? _snapshotService;
    private readonly Func<string, bool, Task<LayoutSnapshot>>? _createSnapshot;
    private readonly Func<LayoutSnapshot, Task>? _restoreSnapshot;
    private readonly Action<AppThemeMode>? _themeChanged;
    private readonly Action _refreshAppearance;
    private readonly Action _commitCornerRadius;
    private readonly Func<bool, Task>? _organize;
    private readonly GroupIconStorageService _groupIconStorage = new();
    private CancellationTokenSource? _appearanceSave;
    private int _previewMode;
    private string? _previewWallpaperPath;
    private bool _radiusThumbDragging;
    private readonly RectangleGeometry _previewClip = new();

    public MainWindow(WorkspaceConfig config, Func<Task> save, Action refreshWindows, Func<CategoryGroup, Task> delete,
        Func<CategoryGroup, bool>? confirmDelete = null, SnapshotService? snapshotService = null,
        Func<string, bool, Task<LayoutSnapshot>>? createSnapshot = null,
        Func<LayoutSnapshot, Task>? restoreSnapshot = null, Action<AppThemeMode>? themeChanged = null,
        Action? refreshAppearance = null, Action? commitCornerRadius = null, Func<bool, Task>? organize = null)
    {
        InitializeComponent();
        GroupEditor.Completed += GroupEditor_Completed;
        SizeChanged += (_, _) => GroupPanelSurface.MaxHeight = Math.Max(320, RootGrid.ActualHeight - 40);
        _noticeTimer.Tick += (_, _) => { _noticeTimer.Stop(); OperationNotice.Visibility = Visibility.Collapsed; };
        Closed += (_, _) => _noticeTimer.Stop();
        _config = config;
        _organize = organize;
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
        _refreshAppearance = refreshAppearance ?? refreshWindows;
        _commitCornerRadius = commitCornerRadius ?? refreshWindows;
        RadiusSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _radiusThumbDragging = true));
        RadiusSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            _radiusThumbDragging = false;
            _commitCornerRadius();
        }));
        RadiusSlider.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!_radiusThumbDragging) _commitCornerRadius();
        };
        RadiusSlider.PreviewKeyUp += (_, _) => _commitCornerRadius();
        DataContext = config;
        GroupList.ItemsSource = config.Groups;
        MonitorList.ItemsSource = DisplayTopologyService.Current.Monitors;
        Loaded += (_, _) =>
        {
            _ = GroupSystemIconService.WarmAsync();
            _ = Dispatcher.InvokeAsync(async () => await GroupEditor.PrepareAsync(), DispatcherPriority.Background);
            PreviewHint.Visibility = IsPreview ? Visibility.Visible : Visibility.Collapsed;
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
            RefreshAppearancePreviewSample();
            var firstGroupState = GetPreviewGroup()?.State ?? GroupDisplayState.Expanded;
            SetPreviewMode(firstGroupState switch
            {
                GroupDisplayState.Collapsed => 1,
                GroupDisplayState.Minimized => 2,
                _ => 0
            });
            LanguageSelector.SelectedIndex = string.Equals(_config.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            RegionSelector.SelectedIndex = _config.RegionCulture switch { "zh-CN" => 1, "en-US" => 2, _ => 0 };
            ShowPage("Home");
        };
    }

    public void RefreshData()
    {
        GroupUndoButton.ToolTip = LocalizationService.Current.Get(_config.LastOrganization?.Entries.Count > 0 ? "Organize_Undo" : "Organize_NoUndo");
        GroupList.Items.Refresh();
        SetCollapseStyleRadio();
        SetThemeRadio();
        RefreshAppearancePreviewSample();
        SetPreviewMode(_previewMode);
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
        {
            var selected = Equals(button.Tag, page);
            button.SetResourceReference(Control.ForegroundProperty, selected ? "PrimaryBrush" : "TextBrush");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            Controls.SoftBorder.SetSelected(button, selected);
        }
        if (page == "Snapshots") _ = ReloadSnapshotsAsync();
        if (page == "Appearance")
        {
            RefreshPreviewWallpaper();
            RefreshAppearancePreviewSample();
            var state = GetPreviewGroup()?.State ?? GroupDisplayState.Expanded;
            SetPreviewMode(state switch
            {
                GroupDisplayState.Collapsed => 1,
                GroupDisplayState.Minimized => 2,
                _ => 0
            });
        }
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
        _previewMode = Math.Clamp(mode, 0, 2);
        mode = _previewMode;
        if (mode == 0)
        {
            PreviewPanel.Width = 260;
            PreviewPanel.Height = 104;
        }
        else if (mode == 1)
        {
            var style = GetPreviewCollapseStyle();
            (PreviewPanel.Width, PreviewPanel.Height) = style switch
            {
                CollapseStyle.VerticalIcon => (94, 126),
                CollapseStyle.IconOnly => (64, 64),
                _ => (220, 62)
            };
            ApplyPreviewCollapsedLayout(style);
        }
        else
        {
            PreviewPanel.Width = 64;
            PreviewPanel.Height = 64;
        }
        PreviewExpandedContents.Margin = mode == 0 ? new Thickness(12) : new Thickness(4);
        PreviewExpandedContents.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviewCollapsedContent.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviewIconOnlyContent.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
        PreviewExpandedButton.SetResourceReference(StyleProperty, mode == 0 ? "PrimaryButton" : "SecondaryButton");
        PreviewCollapsedButton.SetResourceReference(StyleProperty, mode == 1 ? "PrimaryButton" : "SecondaryButton");
        PreviewIconOnlyButton.SetResourceReference(StyleProperty, mode == 2 ? "PrimaryButton" : "SecondaryButton");
        RefreshAppearancePreview();
    }

    private CategoryGroup? GetPreviewGroup()
    {
        var groups = _config.Groups.Where(group => group.IsEnabled).ToList();
        if (groups.Count == 0) groups = _config.Groups.ToList();
        var representativeState = groups.GroupBy(group => group.State)
            .OrderByDescending(stateGroup => stateGroup.Count())
            .Select(stateGroup => (GroupDisplayState?)stateGroup.Key)
            .FirstOrDefault();
        return representativeState is null ? null : groups.FirstOrDefault(group => group.State == representativeState.Value);
    }

    private CollapseStyle GetPreviewCollapseStyle() => GetPreviewGroup()?.CollapseStyle ??
        (VerticalStyle.IsChecked == true ? CollapseStyle.VerticalIcon : IconStyle.IsChecked == true ? CollapseStyle.IconOnly : CollapseStyle.HorizontalCapsule);

    private void ApplyPreviewCollapsedLayout(CollapseStyle style)
    {
        PreviewCollapsedLayout.ColumnDefinitions.Clear();
        PreviewCollapsedLayout.RowDefinitions.Clear();
        PreviewCollapsedLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(PreviewCollapsedIcon, 0); Grid.SetRow(PreviewCollapsedName, 0); Grid.SetRow(PreviewCollapsedBadge, 0);
        PreviewCollapsedName.Visibility = Visibility.Visible;
        PreviewCollapsedBadge.Visibility = _config.ShowBadge ? Visibility.Visible : Visibility.Collapsed;
        PreviewCollapsedIcon.Visibility = _config.ShowCollapsedIcon ? Visibility.Visible : Visibility.Collapsed;
        PreviewCollapsedIcon.Margin = new Thickness(0);
        PreviewCollapsedName.Margin = new Thickness(11, 0, 8, 0);
        PreviewCollapsedName.HorizontalAlignment = HorizontalAlignment.Stretch;
        PreviewCollapsedBadge.HorizontalAlignment = HorizontalAlignment.Stretch;

        if (style == CollapseStyle.HorizontalCapsule)
        {
            PreviewCollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            PreviewCollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PreviewCollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(PreviewCollapsedIcon, 0); Grid.SetColumn(PreviewCollapsedName, 1); Grid.SetColumn(PreviewCollapsedBadge, 2);
        }
        else if (style == CollapseStyle.VerticalIcon)
        {
            PreviewCollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PreviewCollapsedLayout.RowDefinitions.Clear();
            for (var i = 0; i < 3; i++) PreviewCollapsedLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(PreviewCollapsedIcon, 0); Grid.SetColumn(PreviewCollapsedName, 0); Grid.SetColumn(PreviewCollapsedBadge, 0);
            Grid.SetRow(PreviewCollapsedIcon, 0); Grid.SetRow(PreviewCollapsedName, 1); Grid.SetRow(PreviewCollapsedBadge, 2);
            PreviewCollapsedIcon.Margin = new Thickness(0, 2, 0, 5);
            PreviewCollapsedName.Margin = new Thickness(0, 3, 0, 4);
            PreviewCollapsedName.HorizontalAlignment = HorizontalAlignment.Center;
            PreviewCollapsedBadge.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            PreviewCollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PreviewCollapsedName.Visibility = Visibility.Collapsed;
            PreviewCollapsedBadge.Visibility = Visibility.Collapsed;
            Grid.SetColumn(PreviewCollapsedIcon, 0);
            PreviewCollapsedIcon.HorizontalAlignment = HorizontalAlignment.Center;
            PreviewCollapsedIcon.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void RefreshAppearancePreviewSample()
    {
        var sample = GetPreviewGroup();
        var name = sample?.Name ?? LocalizationService.Current.Get("Appearance_Development");
        var glyph = string.IsNullOrWhiteSpace(sample?.Glyph) ? "\uE943" : sample.Glyph;
        Brush accent = Brushes.DodgerBlue;
        try { accent = (Brush)new BrushConverter().ConvertFromString(sample?.Accent ?? "#2675F5")!; }
        catch { }
        var customIcon = string.IsNullOrWhiteSpace(sample?.IconPath) ? null : GroupIconStorageService.LoadPreview(sample.IconPath);
        var displayIcon = customIcon ?? GroupSystemIconService.GetIcon(glyph);
        foreach (var icon in new[] { PreviewExpandedIcon, PreviewCollapsedIcon, PreviewOnlyIcon })
            icon.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        foreach (var text in new[] { PreviewExpandedGlyph, PreviewCollapsedGlyph, PreviewOnlyGlyph })
        {
            text.Text = glyph;
            text.Foreground = accent;
            text.Visibility = displayIcon is null ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var image in new[] { PreviewExpandedCustomIcon, PreviewCollapsedCustomIcon, PreviewOnlyCustomIcon }) image.Source = displayIcon;
        var styleSample = sample ?? new CategoryGroup { Name = name, Glyph = glyph, Accent = "#5D4AD9" };
        foreach (var thumbnail in new[] { CapsuleStyleIcon, VerticalStyleIcon, IconOnlyStyleIcon })
            thumbnail.Content = styleSample;
        PreviewExpandedName.Text = name;
        PreviewCollapsedName.Text = name;
        PreviewCollapsedCount.Text = sample?.ItemCount.ToString(LocalizationService.Current.FormatCulture) ?? "0";
        var items = sample?.Items.Take(4).ToList() ?? [];
        PreviewItems.ItemsSource = items;
        PreviewItemsEmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPreviewWallpaper()
    {
        var path = GetDesktopWallpaperPath();
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, _previewWallpaperPath, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > 64 * 1024 * 1024) return;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1024;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewScene.Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            PreviewSceneTint.Visibility = Visibility.Collapsed;
            PreviewSceneAccent.Visibility = Visibility.Collapsed;
            _previewWallpaperPath = path;
        }
        catch
        {
            // Keep the built-in preview scene if the current wallpaper cannot be read locally.
        }
    }

    private static string? GetDesktopWallpaperPath()
    {
        var buffer = new StringBuilder(32768);
        return SystemParametersInfo(0x0073, (uint)buffer.Capacity, buffer, 0) &&
               !string.IsNullOrWhiteSpace(buffer.ToString()) &&
               !buffer.ToString().StartsWith("\\\\", StringComparison.Ordinal)
            ? buffer.ToString()
            : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);

    private void RefreshAppearancePreview()
    {
        if (!IsInitialized) return;
        var opacity = Math.Clamp(_config.PanelOpacity, 0d, 1d);
        var alpha = (byte)Math.Round(opacity * 255d, MidpointRounding.AwayFromZero);
        var dark = Application.Current.Resources["IsDarkTheme"] is true;
        var collapsedPreview = _previewMode != 0;
        var scale = 1d;
        var fill = dark ? Color.FromRgb(41, 46, 57) : Color.FromRgb(224, 229, 236);
        var radius = Math.Max(0, _config.CornerRadius);
        if (_previewMode == 0)
        {
            var group = GetPreviewGroup();
            var groupWidth = Math.Max(260, group?.Width ?? 320);
            var groupHeight = Math.Max(170, group?.Height ?? 210);
            scale = Math.Min(PreviewPanel.Width / groupWidth, PreviewPanel.Height / groupHeight);
            radius *= scale;
        }
        PreviewExpandedHeader.Height = Math.Max(1, _config.HeaderHeight * scale);
        radius = Math.Min(radius, Math.Min(PreviewPanel.Width, PreviewPanel.Height) / 2);
        PreviewPanel.CornerRadius = new CornerRadius(radius);
        PreviewSurface.CornerRadius = new CornerRadius(radius);
        var previewColor = Color.FromArgb(alpha, fill.R, fill.G, fill.B);
        if (PreviewSurface.ReadLocalValue(Border.BackgroundProperty) is SolidColorBrush previewBrush && !previewBrush.IsFrozen)
            previewBrush.Color = previewColor;
        else
            PreviewSurface.Background = new SolidColorBrush(previewColor);
        PreviewSurface.Opacity = 1;
        PreviewPanel.BorderBrush = Brushes.Transparent;
        if (radius <= 0)
            PreviewPanel.Clip = null;
        else
        {
            _previewClip.Rect = new Rect(0, 0, PreviewPanel.Width, PreviewPanel.Height);
            _previewClip.RadiusX = radius;
            _previewClip.RadiusY = radius;
            if (!ReferenceEquals(PreviewPanel.Clip, _previewClip)) PreviewPanel.Clip = _previewClip;
        }
    }

    private void StartupAppearanceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        StartupCheck.IsChecked = sender is CheckBox checkBox && checkBox.IsChecked == true;
        StartupChanged(sender, e);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e) => OpenGroupPanel();

    internal void OpenGroupPanel()
    {
        if (_openingGroup) return;
        _openingGroup = true;
        var version = ++_groupPanelVersion;
        GroupEditor.Reset();
        GroupPanelSurface.MaxHeight = Math.Max(320, RootGrid.ActualHeight - 40);
        WorkspaceContent.IsHitTestVisible = false;
        SidebarShell.IsHitTestVisible = false;
        WorkspaceContent.CacheMode = new BitmapCache();
        SidebarShell.CacheMode = new BitmapCache();
        CreateGroupLayer.Visibility = Visibility.Visible;
        CreateGroupLayer.IsHitTestVisible = true;
        var duration = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast ? 300 : 0;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        // Cache only the compact form during motion, so its text, icons and soft
        // shadows are not rasterized again for every scale/opacity frame.
        CreateGroupLayer.BeginAnimation(OpacityProperty, null);
        CreateGroupLayer.Opacity = 1;
        GroupPanelSurface.CacheMode = new BitmapCache();
        var reveal = new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease };
        reveal.Completed += (_, _) => { if (_openingGroup && version == _groupPanelVersion) GroupPanelSurface.CacheMode = null; };
        GroupPanelSurface.BeginAnimation(OpacityProperty, reveal);
        GroupPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease });
        GroupPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease });
        GroupEditor.FocusName();
        _ = GroupEditor.PrepareAsync();
    }

    internal void CloseGroupPanel()
    {
        if (!_openingGroup) return;
        _openingGroup = false;
        var version = ++_groupPanelVersion;
        CreateGroupLayer.IsHitTestVisible = false;
        WorkspaceContent.IsHitTestVisible = true;
        SidebarShell.IsHitTestVisible = true;
        var duration = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast ? 300 : 0;
        GroupPanelSurface.CacheMode = new BitmapCache();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(duration)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, _) =>
        {
            if (!_openingGroup && version == _groupPanelVersion)
            {
                CreateGroupLayer.Visibility = Visibility.Hidden;
                GroupPanelSurface.CacheMode = null;
                WorkspaceContent.CacheMode = null;
                SidebarShell.CacheMode = null;
            }
        };
        GroupPanelSurface.BeginAnimation(OpacityProperty, fade);
        NewGroupButton.Focus();
    }

    private void GroupBackdrop_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; CloseGroupPanel(); }

    private async void GroupEditor_Completed(CreateGroupWindow.CreationResult? result)
    {
        CloseGroupPanel();
        if (result is null) return;
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
        try
        {
            await _save();
            GroupList.Items.Refresh();
            ShowNotice(LocalizationService.Current.Get("Action_GroupCreated"));
        }
        catch (Exception ex) { ShowNotice(LocalizationService.Current.Format("Action_Failed", ex.Message)); }
    }

    internal void SetModalOverlay(bool visible)
    {
        var duration = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast ? 300 : 0;
        if (visible)
        {
            var from = ModalOverlay.Visibility == Visibility.Visible ? ModalOverlay.Opacity : 0;
            ModalOverlay.Visibility = Visibility.Visible;
            ModalOverlay.IsHitTestVisible = true;
            ModalOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(duration))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        else
        {
            // Release input immediately; the last frame can fade without swallowing clicks.
            ModalOverlay.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(duration))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            fade.Completed += (_, _) => { if (!ModalOverlay.IsHitTestVisible) ModalOverlay.Visibility = Visibility.Collapsed; };
            ModalOverlay.BeginAnimation(OpacityProperty, fade);
        }
    }

    internal void ShowNotice(string message)
    {
        OperationStatus.Text = message;
        OperationNotice.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    internal void SetOperationBusy(bool busy, string? message = null)
    {
        foreach (var button in new[] { NewGroupButton, CreateSnapshotButton, GroupOrganizeButton, GroupUndoButton, HomeOrganizeButton })
            button.IsEnabled = !busy;
        if (message is not null) ShowNotice(message);
        if (busy) _noticeTimer.Stop();
    }

    private void DismissNotice_Click(object sender, RoutedEventArgs e)
    { _noticeTimer.Stop(); OperationNotice.Visibility = Visibility.Collapsed; }

    private async void Organize_Click(object sender, RoutedEventArgs e)
    {
        if (_organize is null) { ShowNotice(LocalizationService.Current.Get("Action_Unavailable")); return; }
        await _organize(false);
    }

    private async void UndoOrganization_Click(object sender, RoutedEventArgs e)
    {
        if (_organize is null) { ShowNotice(LocalizationService.Current.Get("Action_Unavailable")); return; }
        await _organize(true);
    }

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
        RefreshAppearancePreview();
    }

    public void RefreshTheme()
    {
        RefreshHomeIllustration();
        RefreshAppearancePreview();
        ShowPage(VisiblePage());
    }

    private void RefreshHomeIllustration() => HomeIllustration.Visibility =
        Application.Current.Resources["IsDarkTheme"] is true ? Visibility.Collapsed : Visibility.Visible;

    private string VisiblePage() => GroupsPage.IsVisible ? "Groups" : SnapshotsPage.IsVisible ? "Snapshots" :
        AppearancePage.IsVisible ? "Appearance" : QuickPage.IsVisible ? "Quick" : AboutPage.IsVisible ? "About" : "Home";

    private async Task ReloadSnapshotsAsync()
    {
        try
        {
            IReadOnlyList<LayoutSnapshot> snapshots = _snapshotService is null ? [] : await _snapshotService.ListAsync();
            SnapshotList.ItemsSource = snapshots;
            SnapshotEmptyState.Visibility = snapshots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { ShowNotice(LocalizationService.Current.Format("Action_Failed", ex.Message)); }
    }

    private async void CreateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_createSnapshot is null) { ShowNotice(LocalizationService.Current.Get("Action_Unavailable")); return; }
        if (_snapshotBusy) return;
        _snapshotBusy = true;
        try
        {
        var dialog = new TextPromptWindow(LocalizationService.Current.Get("Prompt_CreateSnapshot"), LocalizationService.Current.Get("Prompt_SnapshotName"),
            LocalizationService.Current.Format("Prompt_DefaultSnapshot", DateTime.Now.ToString("g", LocalizationService.Current.FormatCulture))) { Owner = this };
        SetModalOverlay(true);
        bool accepted;
        try { accepted = dialog.ShowDialog() == true; }
        finally { SetModalOverlay(false); }
        if (!accepted) return;
        SetOperationBusy(true, LocalizationService.Current.Get("Action_SavingSnapshot"));
        await Dispatcher.Yield(DispatcherPriority.Render);
        await _createSnapshot(dialog.Result, false);
        await ReloadSnapshotsAsync();
        ShowNotice(LocalizationService.Current.Get("Action_SnapshotCreated"));
        }
        catch (Exception ex) { ShowNotice(LocalizationService.Current.Format("Action_Failed", ex.Message)); }
        finally { _snapshotBusy = false; SetOperationBusy(false); }
    }

    private void PreviewSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LayoutSnapshot snapshot }) new SnapshotPreviewWindow(snapshot) { Owner = this }.ShowDialog();
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_restoreSnapshot is null || sender is not FrameworkElement { DataContext: LayoutSnapshot snapshot }) return;
        if (MessageBox.Show(this, LocalizationService.Current.Format("Prompt_RestoreSnapshot", snapshot.Name), LocalizationService.Current.Get("Prompt_RestoreSnapshotTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            SetOperationBusy(true, LocalizationService.Current.Get("Action_RestoringSnapshot"));
            await _restoreSnapshot(snapshot);
            RefreshData();
            await ReloadSnapshotsAsync();
            ShowNotice(LocalizationService.Current.Get("Action_SnapshotRestored"));
        }
        catch (Exception ex) { ShowNotice(LocalizationService.Current.Format("Action_Failed", ex.Message)); }
        finally { SetOperationBusy(false); }
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
        RefreshAppearancePreviewSample();
        SetPreviewMode(1);
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
        if (sender is Slider)
        {
            // Keep the preview responsive and update only the visual properties that
            // changed; rebuilding group layouts while dragging causes icon flicker.
            RefreshAppearancePreview();
            _refreshAppearance();
        }
        else
        {
            _refreshWindows();
            RefreshAppearancePreviewSample();
            SetPreviewMode(_previewMode);
        }
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
        if (!IsLoaded || IsPreview) return;
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
