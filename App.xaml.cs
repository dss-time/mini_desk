using System.Drawing;
using System.Windows;
using MiniDesk.Models;
using MiniDesk.Services;
using Forms = System.Windows.Forms;

namespace MiniDesk;

public partial class App : System.Windows.Application
{
    private readonly ConfigService _configService = new();
    private readonly SnapshotService _snapshotService = new();
    private readonly DesktopVisibilityService _desktopVisibility = new();
    private readonly GroupIconStorageService _groupIconStorage = new();
    private readonly Dictionary<Guid, DesktopGroupWindow> _groupWindows = [];
    private CancellationTokenSource? _saveDebounce;
    private ExplorerMonitorService? _explorerMonitor;
    private AppThemeService? _themeService;
    private Mutex? _singleInstance;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private Forms.NotifyIcon? _tray;
    private Icon? _applicationIcon;
    private MainWindow? _manager;

    public WorkspaceConfig Config { get; private set; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationService.Current.SetCulture("zh-CN");
        if (e.Args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            var passed = await SelfTestRunner.RunAsync();
            Shutdown(passed ? 0 : 1);
            return;
        }
        _singleInstance = new Mutex(true, "Local\\MiniDesk.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            try { IpcRequestService.Enqueue(e.Args); } catch { }
            try { EventWaitHandle.OpenExisting("Local\\MiniDesk.Activate").Set(); } catch { }
            Shutdown();
            return;
        }
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\MiniDesk.Activate");
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
            Dispatcher.BeginInvoke(new Action(async () => await ProcessPendingRequestsAsync())), null, Timeout.Infinite, false);
        Config = await _configService.LoadAsync();
        Config.Version = Math.Max(Config.Version, 5);
        LocalizationService.Current.SetCulture(Config.Language);
        LocalizationService.Current.SetRegionCulture(Config.RegionCulture);
        LocalizationService.Current.CultureChanged += LocalizationChanged;
        DisplayTopologyService.Current.TopologyChanged += DisplayTopologyChanged;
        _desktopVisibility.Reconcile(Config);
        _themeService = new AppThemeService();
        _themeService.ThemeChanged += (_, _) => RefreshThemeVisuals();
        _themeService.Apply(Config.ThemeMode);
        TryRegisterShellMenu();
        CreateTrayIcon();
        RefreshGroupWindows();
        if (Config.RestoreAfterExplorerRestart)
            _explorerMonitor = new ExplorerMonitorService(ReattachAfterExplorerRestart);
        await SaveNowAsync();
        foreach (var queued in IpcRequestService.Drain()) await HandleLaunchRequestAsync(queued);
        if (e.Args.Length > 0)
            await HandleLaunchRequestAsync(IpcRequestService.Parse(e.Args));
        else
            OpenManager();
    }

    public void RefreshGroupWindows()
    {
        var activeIds = Config.Groups.Select(g => g.Id).ToHashSet();
        foreach (var stale in _groupWindows.Where(kv => !activeIds.Contains(kv.Key)).ToList())
        {
            stale.Value.AllowClose = true;
            stale.Value.Close();
            _groupWindows.Remove(stale.Key);
        }
        foreach (var group in Config.Groups)
        {
            if (!_groupWindows.TryGetValue(group.Id, out var window))
            {
                window = new DesktopGroupWindow(group, Config, SaveAsync, OpenManager, DeleteGroupAsync,
                    MoveItemBetweenGroupsAsync, _desktopVisibility.Hide, _desktopVisibility.Restore);
                _groupWindows[group.Id] = window;
                window.Show();
            }
            window.Visibility = group.IsEnabled ? Visibility.Visible : Visibility.Hidden;
            window.RefreshVisualState();
        }
    }

    public void RefreshGroupAppearance()
    {
        foreach (var window in _groupWindows.Values) window.RefreshAppearanceOnly();
    }

    public void CommitGroupCornerRadius()
    {
        foreach (var window in _groupWindows.Values) window.CommitCornerRadius();
    }

    public void OpenManager()
    {
        if (_manager is null)
        {
            _manager = new MainWindow(Config, SaveAsync, RefreshGroupWindows, DeleteGroupAsync,
                snapshotService: _snapshotService, createSnapshot: CreateSnapshotAsync, restoreSnapshot: RestoreSnapshotAsync,
                themeChanged: ApplyTheme, refreshAppearance: RefreshGroupAppearance,
                commitCornerRadius: CommitGroupCornerRadius);
            MainWindow = _manager;
            _manager.Closed += (_, _) => _manager = null;
        }
        _manager.Show();
        _manager.WindowState = WindowState.Normal;
        _manager.Activate();
    }

    public Task SaveAsync()
    {
        EnsureExplorerMonitor();
        _saveDebounce?.Cancel();
        var token = (_saveDebounce = new CancellationTokenSource()).Token;
        _ = SaveAfterQuietPeriodAsync(token);
        return Task.CompletedTask;
    }

    private void EnsureExplorerMonitor()
    {
        if (Config.RestoreAfterExplorerRestart && _explorerMonitor is null)
            _explorerMonitor = new ExplorerMonitorService(ReattachAfterExplorerRestart);
        else if (!Config.RestoreAfterExplorerRestart && _explorerMonitor is not null)
        {
            _explorerMonitor.Dispose();
            _explorerMonitor = null;
        }
    }

    private async Task SaveAfterQuietPeriodAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(450, token);
            if (!token.IsCancellationRequested) await _configService.SaveAsync(Config);
        }
        catch (OperationCanceledException) { }
    }

    public async Task SaveNowAsync()
    {
        _saveDebounce?.Cancel();
        await _configService.SaveAsync(Config);
    }

    public async Task DeleteGroupAsync(CategoryGroup group)
    {
        await CreateSnapshotAsync(LocalizationService.Current.Format("Snapshot_BeforeDelete", group.Name), true);
        if (!TryRestoreAll(group.Items))
        {
            MessageBox.Show(LocalizationService.Current.Get("Message_DeleteBlocked"), LocalizationService.Current.Get("Message_DeleteBlockedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Config.Groups.Remove(group);
        RefreshGroupWindows();
        await SaveAsync();
    }

    private async Task MoveItemBetweenGroupsAsync(Guid sourceGroupId, DesktopItem item, Guid? targetGroupId, System.Windows.Point position)
    {
        var source = Config.Groups.FirstOrDefault(group => group.Id == sourceGroupId);
        if (source is null || !source.Items.Contains(item)) return;
        if (targetGroupId is Guid targetId)
        {
            var target = Config.Groups.FirstOrDefault(group => group.Id == targetId);
            if (target is null || target == source) return;
            if (target.Items.Any(existing => string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase))) return;
            source.Items.Remove(item);
            item.X = Math.Max(0, position.X);
            item.Y = Math.Max(0, position.Y);
            target.Items.Add(item);
            target.NotifyCount();
        }
        else
        {
            if (!_desktopVisibility.Restore(item)) return;
            source.Items.Remove(item);
        }
        source.NotifyCount();
        await SaveAsync();
    }

    private async Task<LayoutSnapshot> CreateSnapshotAsync(string name, bool automatic = false) =>
        await _snapshotService.CreateAsync(Config, name, automatic);

    private async Task RestoreSnapshotAsync(LayoutSnapshot snapshot)
    {
        await CreateSnapshotAsync(LocalizationService.Current.Get("Snapshot_BeforeRestore"), true);
        if (!TryRestoreAll(Config.Groups.SelectMany(group => group.Items)))
        {
            MessageBox.Show(LocalizationService.Current.Get("Message_RestoreBlocked"), LocalizationService.Current.Get("Message_RestoreBlockedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var restored = SnapshotService.Clone(snapshot.Layout);
        Config.Groups.Clear();
        foreach (var group in restored.Groups) Config.Groups.Add(group);
        Config.PanelOpacity = restored.PanelOpacity;
        Config.CornerRadius = restored.CornerRadius;
        Config.HeaderHeight = restored.HeaderHeight;
        Config.ShowCollapsedIcon = restored.ShowCollapsedIcon;
        Config.ShowBadge = restored.ShowBadge;
        Config.AttachToDesktop = restored.AttachToDesktop;
        Config.RestoreAfterExplorerRestart = restored.RestoreAfterExplorerRestart;
        Config.ThemeMode = restored.ThemeMode;
        _desktopVisibility.Reconcile(Config);
        ApplyTheme(Config.ThemeMode);
        RefreshGroupWindows();
        await SaveNowAsync();
    }

    private void ApplyTheme(AppThemeMode mode)
    {
        Config.ThemeMode = mode;
        _themeService?.Apply(mode);
        _ = SaveAsync();
    }

    private bool TryRestoreAll(IEnumerable<DesktopItem> items)
    {
        var restored = new List<DesktopItem>();
        foreach (var item in items.ToList())
        {
            if (_desktopVisibility.Restore(item))
            {
                restored.Add(item);
                continue;
            }
            foreach (var completed in restored) _desktopVisibility.Hide(completed);
            return false;
        }
        return true;
    }

    private void RefreshThemeVisuals()
    {
        foreach (var window in _groupWindows.Values) window.RefreshVisualState();
        _manager?.RefreshTheme();
        UpdateTrayIcon();
    }

    private void ReattachAfterExplorerRestart()
    {
        if (!Config.RestoreAfterExplorerRestart) return;
        foreach (var window in _groupWindows.Values) window.ReattachToDesktop();
    }

    private void DisplayTopologyChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        foreach (var window in _groupWindows.Values) window.RecoverDisplayPlacement();
        _ = SaveAsync();
    });

    private async Task ProcessPendingRequestsAsync()
    {
        var requests = IpcRequestService.Drain();
        if (requests.Count == 0)
        {
            OpenManager();
            return;
        }
        foreach (var request in requests) await HandleLaunchRequestAsync(request);
    }

    private async Task HandleLaunchRequestAsync(LaunchRequest request)
    {
        switch (request.Action)
        {
            case "background":
                break;
            case "add" when !string.IsNullOrWhiteSpace(request.Path):
                await ShowAddToGroupAsync(request.Path);
                break;
            case "new-group":
                await CreateGroupFromPromptAsync();
                break;
            default:
                OpenManager();
                break;
        }
    }

    private Task ShowAddToGroupAsync(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            MessageBox.Show(LocalizationService.Current.Format("Message_ItemMissing", path), LocalizationService.Current.Get("Message_AddFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        var picker = new AddToGroupWindow(path, Config, async group =>
        {
            if (DesktopItemService.AddPath(group, path, out var added))
            {
                if (added is not null) _desktopVisibility.Hide(added);
                group.IsEnabled = true;
                RefreshGroupWindows();
                await SaveAsync();
            }
        }, CreateGroupFromPromptAsync);
        if (_manager is { IsVisible: true }) picker.Owner = _manager;
        picker.ShowDialog();
        return Task.CompletedTask;
    }

    private async Task<CategoryGroup?> CreateGroupFromPromptAsync()
    {
        var owner = Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? _manager;
        var dialog = new CreateGroupWindow();
        if (owner is not null) dialog.Owner = owner;
        var managerWindow = owner as MainWindow;
        managerWindow?.SetModalOverlay(true);
        bool accepted;
        try { accepted = dialog.ShowDialog() == true; }
        finally { managerWindow?.SetModalOverlay(false); }
        if (!accepted || dialog.Result is null) return null;
        var result = dialog.Result;
        string iconPath;
        try { iconPath = result.UploadPath is null ? string.Empty : _groupIconStorage.Store(result.UploadPath); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, LocalizationService.Current.Get("Message_IconSaveFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        var count = Config.Groups.Count;
        var group = ConfigService.NewGroup(result.Name, result.Glyph, result.Accent,
            260 + count * 18, 140 + count * 15, state: result.State, iconPath: iconPath);
        group.CollapseStyle = result.CollapseStyle;
        group.IsEnabled = result.ShowOnDesktop;
        Config.Groups.Add(group);
        RefreshGroupWindows();
        await SaveAsync();
        return group;
    }

    private static void TryRegisterShellMenu()
    {
        try { ShellContextMenuService.EnsureRegistered(); }
        catch (Exception ex)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "shell-menu-error.txt"), ex.ToString());
            }
            catch { }
        }
    }

    private void CreateTrayIcon()
    {
        var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/MiniDesk.ico"))?.Stream;
        if (stream is not null)
        {
            using (stream)
            using (var source = new Icon(stream))
                _applicationIcon = (Icon)source.Clone();
        }
        _tray = new Forms.NotifyIcon
        {
            Text = LocalizationService.Current.Get("Tray_Tooltip"),
            Icon = _applicationIcon ?? SystemIcons.Application,
            Visible = true
        };
        RebuildTrayMenu();
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenManager);
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null) return;
        var old = _tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(LocalizationService.Current.Get("Tray_Open"), null, (_, _) => Dispatcher.Invoke(OpenManager));
        menu.Items.Add(LocalizationService.Current.Get("Tray_NewGroup"), null, async (_, _) => await Dispatcher.InvokeAsync(CreateGroupFromPromptAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Current.Get("Tray_Exit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _tray.ContextMenuStrip = menu;
        _tray.Text = LocalizationService.Current.Get("Tray_Tooltip");
        old?.Dispose();
    }

    private void LocalizationChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        RebuildTrayMenu();
        TryRegisterShellMenu();
        foreach (var window in _groupWindows.Values) window.RefreshLocalization();
        _manager?.RefreshData();
    });

    private void UpdateTrayIcon()
    {
        if (_tray is null) return;
        var variant = _themeService?.IsDark == true ? "MiniDesk-Dark.ico" : "MiniDesk-Light.ico";
        var stream = GetResourceStream(new Uri($"pack://application:,,,/Assets/{variant}"))?.Stream;
        if (stream is null) return;
        using (stream)
        using (var source = new Icon(stream))
        {
            _applicationIcon?.Dispose();
            _applicationIcon = (Icon)source.Clone();
            _tray.Icon = _applicationIcon;
        }
    }

    private async void ExitApplication()
    {
        await SaveNowAsync();
        foreach (var window in _groupWindows.Values) { window.AllowClose = true; window.Close(); }
        _tray?.Dispose();
        _explorerMonitor?.Dispose();
        _themeService?.Dispose();
        DisplayTopologyService.Current.TopologyChanged -= DisplayTopologyChanged;
        LocalizationService.Current.CultureChanged -= LocalizationChanged;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _applicationIcon?.Dispose();
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        _singleInstance?.Dispose();
        DisplayTopologyService.Current.TopologyChanged -= DisplayTopologyChanged;
        LocalizationService.Current.CultureChanged -= LocalizationChanged;
        base.OnExit(e);
    }
}
