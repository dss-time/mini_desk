using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using MiniDesk.Models;
using MiniDesk.Services;
using Forms = System.Windows.Forms;

namespace MiniDesk;

public partial class DesktopGroupWindow : Window
{
    private readonly CategoryGroup _group;
    private readonly WorkspaceConfig _config;
    private readonly Func<Task> _save;
    private readonly Action _openManager;
    private readonly Func<CategoryGroup, Task> _delete;
    private readonly Func<Guid, DesktopItem, Guid?, System.Windows.Point, Task> _moveItem;
    private readonly Func<DesktopItem, bool> _hideItem;
    private readonly Func<DesktopItem, bool> _restoreItem;
    private bool _attached;
    private bool _movingWindow;
    private bool _collapsedPointerDown;
    private bool _collapsedHasMoved;
    private System.Drawing.Point _moveStartCursor;
    private System.Windows.Point _moveStartWindow;
    private DisplayTopologyService.PhysicalRect _moveStartBounds;
    private DesktopItem? _movingItem;
    private System.Windows.Point _itemOffset;
    private bool _draggingItem;
    private bool _panelAppearanceApplyPending;

    public bool AllowClose { get; set; }

    public DesktopGroupWindow(CategoryGroup group, WorkspaceConfig config, Func<Task> save,
        Action openManager, Func<CategoryGroup, Task> delete,
        Func<Guid, DesktopItem, Guid?, System.Windows.Point, Task>? moveItem = null,
        Func<DesktopItem, bool>? hideItem = null, Func<DesktopItem, bool>? restoreItem = null)
    {
        InitializeComponent();
        _group = group;
        _config = config;
        _save = save;
        _openManager = openManager;
        _delete = delete;
        _moveItem = moveItem ?? ((_, _, _, _) => Task.CompletedTask);
        _hideItem = hideItem ?? (_ => true);
        _restoreItem = restoreItem ?? (_ => true);
        DataContext = group;
        Left = group.Left;
        Top = group.Top;
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    var result = DisplayTopologyService.Current.HandleDpiMessage(this, hwnd, message, wParam, lParam, ref handled);
                    if (message is 0x0005 or 0x02E0) SchedulePanelAppearance(); // WM_SIZE / WM_DPICHANGED
                    return result;
                });
            _attached = _config.AttachToDesktop && DesktopHostService.TryAttach(this);
            DisplayTopologyService.Current.Apply(_group, this);
            SchedulePanelAppearance();
        };
        Loaded += (_, _) => SchedulePanelAppearance();
        SizeChanged += (_, _) => SchedulePanelAppearance();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };

        RefreshLocalization();

        _group.Items.CollectionChanged += (_, _) => { _group.NotifyCount(); UpdateEmptyState(); };
        RefreshVisualState();
    }

    private MenuItem Menu(string title, Func<Task> action)
    {
        var item = new MenuItem { Header = title };
        item.Click += async (_, _) => await action();
        return item;
    }

    public void RefreshLocalization()
    {
        var l = LocalizationService.Current;
        var menu = new ContextMenu();
        menu.Items.Add(Menu(l.Get("Group_Expand"), async () => { _group.State = GroupDisplayState.Expanded; RefreshVisualState(); await _save(); }));
        menu.Items.Add(Menu(l.Get("Group_CollapseCapsule"), async () => { _group.CollapseStyle = CollapseStyle.HorizontalCapsule; _group.State = GroupDisplayState.Collapsed; RefreshVisualState(); await _save(); }));
        menu.Items.Add(Menu(l.Get("Group_CollapseVertical"), async () => { _group.CollapseStyle = CollapseStyle.VerticalIcon; _group.State = GroupDisplayState.Collapsed; RefreshVisualState(); await _save(); }));
        menu.Items.Add(Menu(l.Get("Group_CollapseIcon"), async () => { _group.CollapseStyle = CollapseStyle.IconOnly; _group.State = GroupDisplayState.Collapsed; RefreshVisualState(); await _save(); }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu(l.Get("Common_Rename"), async () => { Rename(); await _save(); }));
        menu.Items.Add(Menu(l.Get("Common_Settings"), () => { _openManager(); return Task.CompletedTask; }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu(l.Get("Group_Delete"), ConfirmDeleteAsync));
        ContextMenu = menu;
    }

    public void RefreshVisualState()
    {
        HeaderRow.Height = new GridLength(_config.HeaderHeight);
        var headerInset = Math.Max(0, (_config.CornerRadius - 24) * 0.65);
        Header.Margin = new Thickness(headerInset, 0, headerInset, 0);
        var opacity = Math.Clamp(_config.PanelOpacity, 0d, 1d);
        var alpha = (byte)Math.Round(opacity * 255d, MidpointRounding.AwayFromZero);
        var dark = (System.Windows.Application.Current as App)?.Config.ThemeMode == AppThemeMode.Dark ||
                   (System.Windows.Application.Current as App)?.Config.ThemeMode == AppThemeMode.System &&
                   System.Windows.Application.Current.Resources["AppBackgroundBrush"] is SolidColorBrush appBrush && appBrush.Color.R < 80;
        ExpandedCard.Background = new SolidColorBrush(dark
            ? Color.FromArgb(alpha, 38, 43, 53)
            : Color.FromArgb(alpha, 245, 249, 255));
        ExpandedCard.CornerRadius = new CornerRadius(_config.CornerRadius);
        CollapsedCard.Background = new SolidColorBrush(dark
            ? Color.FromArgb(alpha, 43, 49, 61)
            : Color.FromArgb(alpha, 246, 250, 255));
        CollapsedCard.CornerRadius = new CornerRadius(_config.CornerRadius);

        var expanded = _group.State == GroupDisplayState.Expanded;
        ExpandedCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CollapsedCard.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        if (expanded)
        {
            Width = Math.Max(260, _group.Width);
            Height = Math.Max(170, _group.Height);
        }
        else ApplyCollapsedStyle(_group.State == GroupDisplayState.Minimized ? CollapseStyle.VerticalIcon : _group.CollapseStyle);

        UpdateEmptyState();
        if (IsLoaded)
        {
            DisplayTopologyService.Current.Apply(_group, this);
            SchedulePanelAppearance();
        }
    }

    private void SchedulePanelAppearance()
    {
        if (!IsLoaded || _panelAppearanceApplyPending) return;
        _panelAppearanceApplyPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _panelAppearanceApplyPending = false;
            ApplyContentClip();
            ApplyPanelAppearance();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    internal void ApplyContentClip()
    {
        ApplyRoundedClip(ExpandedCard);
        ApplyRoundedClip(CollapsedCard);
    }

    private void ApplyRoundedClip(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        var radius = Math.Clamp(_config.CornerRadius, 0, Math.Min(element.ActualWidth, element.ActualHeight) / 2);
        element.Clip = radius <= 0
            ? null
            : new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), radius, radius);
    }

    private void ApplyPanelAppearance()
    {
        DesktopHostService.ApplyPanelAppearance(this, _config.CornerRadius);
    }

    private void ApplyCollapsedStyle(CollapseStyle style)
    {
        CollapsedLayout.ColumnDefinitions.Clear();
        CollapsedLayout.RowDefinitions.Clear();
        CollapsedLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(CollapsedIcon, 0); Grid.SetRow(CollapsedName, 0); Grid.SetRow(CollapsedBadge, 0);
        CollapsedName.Visibility = Visibility.Visible;
        CollapsedBadge.Visibility = _config.ShowBadge ? Visibility.Visible : Visibility.Collapsed;
        CollapsedIcon.Visibility = _config.ShowCollapsedIcon ? Visibility.Visible : Visibility.Collapsed;
        CollapsedName.Margin = new Thickness(11, 0, 8, 0);
        CollapsedIcon.Margin = new Thickness(0);

        if (style == CollapseStyle.HorizontalCapsule)
        {
            Width = 220; Height = 62;
            CollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            CollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            CollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(CollapsedIcon, 0); Grid.SetColumn(CollapsedName, 1); Grid.SetColumn(CollapsedBadge, 2);
        }
        else if (style == CollapseStyle.VerticalIcon)
        {
            Width = 94; Height = 126;
            CollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            CollapsedLayout.RowDefinitions.Clear();
            CollapsedLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            CollapsedLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            CollapsedLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(CollapsedIcon, 0); Grid.SetColumn(CollapsedName, 0); Grid.SetColumn(CollapsedBadge, 0);
            Grid.SetRow(CollapsedIcon, 0); Grid.SetRow(CollapsedName, 1); Grid.SetRow(CollapsedBadge, 2);
            CollapsedIcon.Margin = new Thickness(0, 2, 0, 5);
            CollapsedName.Margin = new Thickness(0, 3, 0, 4);
            CollapsedName.HorizontalAlignment = HorizontalAlignment.Center;
            CollapsedBadge.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            Width = 64; Height = 64;
            CollapsedName.Visibility = Visibility.Collapsed;
            CollapsedBadge.Visibility = Visibility.Collapsed;
            CollapsedLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(CollapsedIcon, 0);
            CollapsedIcon.HorizontalAlignment = HorizontalAlignment.Center;
            CollapsedIcon.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    private void UpdateEmptyState() => EmptyHint.Visibility = _group.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void Collapse_Click(object sender, RoutedEventArgs e)
    {
        _group.Width = Width; _group.Height = Height;
        _group.State = GroupDisplayState.Collapsed;
        RefreshVisualState();
        await _save();
    }

    private void CollapsedCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _collapsedPointerDown = true;
        _collapsedHasMoved = false;
        _moveStartCursor = Forms.Cursor.Position;
        _moveStartWindow = new System.Windows.Point(_group.Left, _group.Top);
        _moveStartBounds = DisplayTopologyService.Current.GetPhysicalBounds(this);
        CollapsedCard.CaptureMouse();
        e.Handled = true;
    }

    private void CollapsedCard_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_collapsedPointerDown || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = Forms.Cursor.Position;
        var deltaX = cursor.X - _moveStartCursor.X;
        var deltaY = cursor.Y - _moveStartCursor.Y;
        if (!_collapsedHasMoved && Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance) return;
        _collapsedHasMoved = true;
        DisplayTopologyService.Current.MovePhysical(this, _moveStartBounds.Left + deltaX, _moveStartBounds.Top + deltaY);
        e.Handled = true;
    }

    private async void CollapsedCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var wasPointerDown = _collapsedPointerDown;
        var wasMoved = _collapsedHasMoved;
        _collapsedPointerDown = false;
        _collapsedHasMoved = false;
        if (CollapsedCard.IsMouseCaptured) CollapsedCard.ReleaseMouseCapture();
        if (wasPointerDown && wasMoved)
        {
            DisplayTopologyService.Current.Capture(_group, this);
            await _save();
            e.Handled = true;
            return;
        }
        _group.State = GroupDisplayState.Expanded;
        RefreshVisualState();
        await _save();
        e.Handled = true;
    }

    internal void MoveCollapsedTo(double left, double top)
    {
        _group.Left = left;
        _group.Top = top;
        Left = left;
        Top = top;
        if (_attached) DesktopHostService.MoveToModelPosition(this);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is Button) return;
        _movingWindow = true;
        _moveStartCursor = Forms.Cursor.Position;
        _moveStartWindow = new System.Windows.Point(_group.Left, _group.Top);
        _moveStartBounds = DisplayTopologyService.Current.GetPhysicalBounds(this);
        Header.CaptureMouse();
    }

    private void Header_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_movingWindow || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = Forms.Cursor.Position;
        DisplayTopologyService.Current.MovePhysical(this,
            _moveStartBounds.Left + cursor.X - _moveStartCursor.X,
            _moveStartBounds.Top + cursor.Y - _moveStartCursor.Y);
    }

    private async void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_movingWindow) return;
        _movingWindow = false;
        Header.ReleaseMouseCapture();
        DisplayTopologyService.Current.Capture(_group, this);
        await _save();
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _group.Width = Math.Max(260, _group.Width + e.HorizontalChange);
        _group.Height = Math.Max(170, _group.Height + e.VerticalChange);
        Width = _group.Width; Height = _group.Height;
        SchedulePanelAppearance();
    }

    private async void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        DisplayTopologyService.Current.Capture(_group, this);
        await _save();
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("MiniDesk.InternalItem") ? DragDropEffects.Move :
            e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(InternalItemDataFormat) is string payload &&
            TryParseItemDragPayload(payload, out var sourceGroupId, out var itemId))
        {
            var source = _config.Groups.FirstOrDefault(group => group.Id == sourceGroupId);
            var item = source?.Items.FirstOrDefault(candidate => candidate.Id == itemId);
            if (item is null) return;
            var point = e.GetPosition(Items);
            await _moveItem(sourceGroupId, item, _group.Id, point);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var origin = e.GetPosition(Items);
        var added = 0;
        foreach (var path in paths.Where(p => !_group.Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))))
        {
            if (DesktopItemService.AddPath(_group, path, out var item,
                    new System.Windows.Point(origin.X + (added % 3) * 86, origin.Y + (added / 3) * 90)))
            {
                if (item is not null) _hideItem(item);
                added++;
            }
        }
        await _save();
    }

    private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { DataContext: DesktopItem item } border) return;
        if (e.ClickCount > 1)
        {
            OpenItem(item);
            e.Handled = true;
            return;
        }
        _movingItem = item;
        var pointer = e.GetPosition(Items);
        _itemOffset = new System.Windows.Point(pointer.X - item.X, pointer.Y - item.Y);
        border.CaptureMouse();
        e.Handled = true;
    }

    private async void Item_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_movingItem is null || e.LeftButton != MouseButtonState.Pressed || _draggingItem) return;
        var p = e.GetPosition(Items);
        if (p.X < -12 || p.Y < -12 || p.X > Items.ActualWidth + 12 || p.Y > Items.ActualHeight + 12)
        {
            _draggingItem = true;
            var item = _movingItem;
            if (sender is Border border) border.ReleaseMouseCapture();
            var data = new DataObject();
            data.SetData(InternalItemDataFormat, CreateItemDragPayload(_group.Id, item.Id), false);
            try
            {
                DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
                if (DesktopHostService.IsDesktopPoint(Forms.Cursor.Position))
                    await _moveItem(_group.Id, item, null, default);
            }
            finally
            {
                _movingItem = null;
                _draggingItem = false;
            }
            return;
        }
        _movingItem.X = Math.Clamp(p.X - _itemOffset.X, 0, Math.Max(0, Items.ActualWidth - 82));
        _movingItem.Y = Math.Clamp(p.Y - _itemOffset.Y, 0, Math.Max(0, Items.ActualHeight - 86));
    }

    internal const string InternalItemDataFormat = "MiniDesk.InternalItem";

    internal static string CreateItemDragPayload(Guid sourceGroupId, Guid itemId) => $"{sourceGroupId:N}|{itemId:N}";

    internal static bool TryParseItemDragPayload(string? payload, out Guid sourceGroupId, out Guid itemId)
    {
        sourceGroupId = Guid.Empty;
        itemId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        var separator = payload.IndexOf('|');
        return separator > 0 && separator < payload.Length - 1 &&
               Guid.TryParseExact(payload[..separator], "N", out sourceGroupId) &&
               Guid.TryParseExact(payload[(separator + 1)..], "N", out itemId);
    }

    private async void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingItem is null) return;
        _movingItem = null;
        if (sender is Border border) border.ReleaseMouseCapture();
        await _save();
    }

    public void ReattachToDesktop()
    {
        _attached = _config.AttachToDesktop && DesktopHostService.TryAttach(this);
        RefreshVisualState();
    }

    public void RecoverDisplayPlacement() => DisplayTopologyService.Current.Apply(_group, this);

    private void OpenItem(DesktopItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, LocalizationService.Current.Get("Message_OpenFailed"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: Border { DataContext: DesktopItem item } } }) return;
        if (!_restoreItem(item))
        {
            MessageBox.Show(this, LocalizationService.Current.Get("Message_RemoveFailed"), LocalizationService.Current.Get("Message_RemoveFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _group.Items.Remove(item);
        _group.NotifyCount();
        await _save();
    }

    private void Rename()
    {
        var dialog = new TextPromptWindow(LocalizationService.Current.Get("Prompt_RenameGroup"), LocalizationService.Current.Get("Prompt_GroupName"), _group.Name) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Result)) _group.Name = dialog.Result.Trim();
    }

    private async Task ConfirmDeleteAsync()
    {
        if (MessageBox.Show(LocalizationService.Current.Format("Message_DeleteGroup", _group.Name), LocalizationService.Current.Get("Message_DeleteGroupTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            await _delete(_group);
    }
}
