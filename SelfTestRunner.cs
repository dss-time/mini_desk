using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniDesk.Models;
using MiniDesk.Services;

namespace MiniDesk;

public static class SelfTestRunner
{
    private sealed record Result(string Name, bool Passed, string Detail);

    public static async Task<bool> RunAsync()
    {
        var results = new List<Result>();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"MiniDeskSelfTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var sample = Path.Combine(tempRoot, "测试文件.txt");
        await File.WriteAllTextAsync(sample, "MiniDesk self test");

        await Run(results, "配置保存与恢复", async () =>
        {
            var path = Path.Combine(tempRoot, "workspace.json");
            var service = new ConfigService(path);
            var config = new WorkspaceConfig();
            config.Groups.Add(ConfigService.NewGroup("测试组", left: 21, top: 34));
            config.Groups[0].IconPath = Path.Combine(tempRoot, "group-icon.png");
            Assert(DesktopItemService.AddPath(config.Groups[0], sample), "首次添加应成功");
            await service.SaveAsync(config);
            var loaded = await service.LoadAsync();
            Assert(loaded.Groups.Count == 1, "分组数量未恢复");
            Assert(loaded.Groups[0].Items.Count == 1, "关联项目未恢复");
            Assert(loaded.Groups[0].Items[0].Path == Path.GetFullPath(sample), "路径未恢复");
            Assert(loaded.Groups[0].IconPath == config.Groups[0].IconPath, "自定义分组图标路径未恢复");
            Assert(File.Exists(sample), "保存分类关系不应移动或删除原文件");
        });

        await Run(results, "路径添加、去重和真实文件保护", () =>
        {
            var group = ConfigService.NewGroup("文件组", width: 300);
            Assert(DesktopItemService.AddPath(group, sample), "首次添加失败");
            Assert(!DesktopItemService.AddPath(group, sample), "重复路径未被拦截");
            Assert(group.Items.Count == 1, "重复添加改变了数量");
            group.Items.RemoveAt(0);
            Assert(File.Exists(sample), "从分组移除不应删除原文件");
            return Task.CompletedTask;
        });

        await Run(results, "桌面图标隐藏与安全恢复", () =>
        {
            var fakeUserDesktop = Path.Combine(tempRoot, "Desktop");
            var fakeCommonDesktop = Path.Combine(tempRoot, "PublicDesktop");
            var storage = Path.Combine(tempRoot, "ManagedDesktopItems");
            Directory.CreateDirectory(fakeUserDesktop);
            Directory.CreateDirectory(fakeCommonDesktop);
            var visibility = new DesktopVisibilityService(fakeUserDesktop, fakeCommonDesktop, storage);

            var shortcutPath = Path.Combine(fakeCommonDesktop, "测试快捷方式.lnk");
            File.WriteAllText(shortcutPath, "shortcut-data");
            var shortcut = new DesktopItem { Name = "测试快捷方式", Path = shortcutPath };
            Assert(visibility.Hide(shortcut), "桌面快捷方式未隐藏");
            Assert(!File.Exists(shortcutPath) && File.Exists(shortcut.Path), "快捷方式未进入安全存储");
            Assert(shortcut.VisibilityMode == DesktopVisibilityMode.ManagedShortcut, "快捷方式隐藏模式错误");
            Assert(visibility.Restore(shortcut), "快捷方式未恢复");
            Assert(File.Exists(shortcutPath) && File.ReadAllText(shortcutPath) == "shortcut-data", "快捷方式内容或原位置未恢复");

            var filePath = Path.Combine(fakeUserDesktop, "桌面文件.txt");
            File.WriteAllText(filePath, "real-file-data");
            var file = new DesktopItem { Name = "桌面文件", Path = filePath };
            Assert(visibility.Hide(file), "普通桌面文件未隐藏");
            Assert(File.Exists(filePath) && File.GetAttributes(filePath).HasFlag(FileAttributes.Hidden), "普通文件不应移动且应隐藏");
            Assert(visibility.Restore(file), "普通桌面文件未恢复");
            Assert(File.Exists(filePath) && !File.GetAttributes(filePath).HasFlag(FileAttributes.Hidden) && File.ReadAllText(filePath) == "real-file-data",
                "普通桌面文件属性或内容未恢复");
            return Task.CompletedTask;
        });

        await Run(results, "公共桌面恢复权限、同名冲突与失败保留", () =>
        {
            var userFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "RestoreUserDesktop"));
            var publicFolder = Directory.CreateDirectory(Path.Combine(tempRoot, "RestorePublicDesktop"));
            var storage = Path.Combine(tempRoot, "RestoreStorage");
            var service = new DesktopVisibilityService(userFolder.FullName, publicFolder.FullName, storage);
            var original = Path.Combine(publicFolder.FullName, "shortcut.lnk");
            File.WriteAllText(original, "original-shortcut-bytes");
            var item = new DesktopItem { Name = "shortcut", Path = original };
            Assert(service.Hide(item), "权限测试快捷方式未暂存");
            var publicAcl = publicFolder.GetAccessControl();
            var userAcl = userFolder.GetAccessControl();
            var denyCreation = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.CreateFiles, AccessControlType.Deny);
            try
            {
                var deniedPublicAcl = publicFolder.GetAccessControl();
                deniedPublicAcl.AddAccessRule(denyCreation);
                publicFolder.SetAccessControl(deniedPublicAcl);
                var conflict = Path.Combine(userFolder.FullName, "shortcut.lnk");
                File.WriteAllText(conflict, "existing-user-shortcut");
                Assert(service.Restore(item) && service.RestoredToPersonalDesktop,
                    "公共桌面无创建权限时未恢复到个人桌面");
                Assert(Path.GetDirectoryName(item.Path) == userFolder.FullName && item.Path != conflict &&
                       File.ReadAllText(item.Path) == "original-shortcut-bytes" &&
                       File.ReadAllText(conflict) == "existing-user-shortcut" && item.VisibilityMode == DesktopVisibilityMode.None,
                    "恢复快捷方式覆盖了同名文件或损坏了内容");

                // Both destinations unavailable must keep the only copy and recovery metadata.
                var second = new DesktopItem { Name = "blocked", Path = Path.Combine(storage, "blocked.lnk"),
                    OriginalDesktopPath = Path.Combine(publicFolder.FullName, "blocked.lnk"),
                    VisibilityMode = DesktopVisibilityMode.ManagedShortcut };
                File.WriteAllText(second.Path, "keep-this-copy");
                var deniedUserAcl = userFolder.GetAccessControl();
                deniedUserAcl.AddAccessRule(denyCreation);
                userFolder.SetAccessControl(deniedUserAcl);
                var storedPath = second.Path;
                var originalPath = second.OriginalDesktopPath;
                Assert(!service.Restore(second) && !string.IsNullOrWhiteSpace(service.LastRestoreError) &&
                       second.Path == storedPath && second.OriginalDesktopPath == originalPath &&
                       second.VisibilityMode == DesktopVisibilityMode.ManagedShortcut && File.ReadAllText(storedPath) == "keep-this-copy",
                    "恢复失败清除了归属或丢失了快捷方式");
            }
            finally
            {
                publicFolder.SetAccessControl(publicAcl);
                userFolder.SetAccessControl(userAcl);
            }
            var missing = new DesktopItem { Path = Path.Combine(storage, "missing.lnk"),
                OriginalDesktopPath = original, VisibilityMode = DesktopVisibilityMode.ManagedShortcut };
            Assert(!service.Restore(missing) && missing.VisibilityMode == DesktopVisibilityMode.ManagedShortcut &&
                   missing.OriginalDesktopPath == original, "缺失快捷方式被错误标记为恢复成功");
            return Task.CompletedTask;
        });

        await Run(results, "布局快照创建、预览数据、恢复与删除", async () =>
        {
            var snapshots = new SnapshotService(Path.Combine(tempRoot, "Snapshots"));
            var config = new WorkspaceConfig { ThemeMode = AppThemeMode.Dark };
            var group = ConfigService.NewGroup("快照组", left: 73, top: 91);
            DesktopItemService.AddPath(group, sample);
            config.Groups.Add(group);
            var created = await snapshots.CreateAsync(config, "工作布局");
            group.Left = 999;
            var listed = await snapshots.ListAsync();
            Assert(listed.Count == 1 && listed[0].GroupCount == 1 && listed[0].ItemCount == 1, "快照索引数据错误");
            Assert(created.Layout.Groups[0].Left == 73, "快照未与当前布局隔离");
            var restored = SnapshotService.Clone(created.Layout);
            Assert(restored.ThemeMode == AppThemeMode.Dark && restored.Groups[0].Items[0].Path == sample, "快照恢复数据不完整");
            await snapshots.DeleteAsync(created.Id);
            Assert((await snapshots.ListAsync()).Count == 0, "快照删除失败");
            Assert(File.Exists(sample), "快照操作不应影响真实文件");
        });

        await Run(results, "拖出归属数据与真实文件隔离", () =>
        {
            var source = ConfigService.NewGroup("来源");
            var target = ConfigService.NewGroup("目标");
            DesktopItemService.AddPath(source, sample);
            var item = source.Items[0];
            var payload = DesktopGroupWindow.CreateItemDragPayload(source.Id, item.Id);
            Assert(DesktopGroupWindow.TryParseItemDragPayload(payload, out var parsedGroupId, out var parsedItemId) &&
                   parsedGroupId == source.Id && parsedItemId == item.Id, "内部拖拽载荷错误");
            Assert(!DesktopGroupWindow.TryParseItemDragPayload("无效载荷", out _, out _), "损坏的拖拽载荷未被拒绝");
            source.Items.Remove(item); target.Items.Add(item);
            Assert(target.Items.Count == 1 && File.Exists(sample), "跨分组不应移动真实文件");
            target.Items.Remove(item);
            Assert(File.Exists(sample), "拖回桌面只应解除归属");
            return Task.CompletedTask;
        });

        var motionDetail = string.Empty;
        var beforeMotion = results.Count;
        await Run(results, "弹窗首帧与动画响应", async () =>
        {
            var host = new MainWindow(new WorkspaceConfig(), () => Task.CompletedTask, () => { }, _ => Task.CompletedTask) { IsPreview = true };
            host.Show();
            await WaitUntil(() => host.GroupEditor.IsPrepared);
            await Task.Delay(100);
            var intervals = new List<double>();
            var openingTimes = new List<double>();
            var editor = host.GroupEditor;
            var windowCount = System.Windows.Application.Current.Windows.Count;
            try
            {
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    double? previous = null;
                    double firstFrame = 0;
                    EventHandler rendering = (_, args) =>
                    {
                        if (firstFrame == 0) firstFrame = clock.Elapsed.TotalMilliseconds;
                        var time = ((RenderingEventArgs)args).RenderingTime.TotalMilliseconds;
                        if (previous is double last && time > last) intervals.Add(time - last);
                        previous = time;
                    };
                    CompositionTarget.Rendering += rendering;
                    host.OpenGroupPanel();
                    Assert(host.IsGroupPanelOpen && host.CreateGroupLayer.IsHitTestVisible, "点击后没有立即响应");
                    Assert(System.Windows.Application.Current.Windows.Count == windowCount, "打开面板仍然创建了独立窗口");
                    Assert(ReferenceEquals(editor, host.GroupEditor), "反复打开没有复用同一个表单");
                    Assert(editor.NameInput.IsKeyboardFocused, "打开后名称输入框未获得焦点");
                    await Task.Delay(380);
                    CompositionTarget.Rendering -= rendering;
                    Assert(firstFrame > 0, "面板没有进入首个渲染帧");
                    Assert(host.CreateGroupLayer.Opacity > .99, "打开过渡没有完成");
                    openingTimes.Add(firstFrame);
                    if (attempt == 0) SaveVisual(host, Path.Combine(Environment.CurrentDirectory, "implementation-inline-create-group.png"));
                    host.CloseGroupPanel();
                    Assert(host.WorkspaceContent.IsHitTestVisible, "关闭面板后后台仍不能操作");
                    await Task.Delay(320);
                }
                var ordered = intervals.OrderBy(x => x).ToArray();
                var p95 = ordered.Length == 0 ? 0 : ordered[(int)((ordered.Length - 1) * .95)];
                Assert(openingTimes.Max() < 100 && p95 < 35, $"面板仍有卡顿：首帧最大 {openingTimes.Max():0.0} ms，帧间隔 P95 {p95:0.0} ms；Tier {RenderCapability.Tier >> 16}；间隔 {string.Join(",", intervals.Select(x => x.ToString("0")))}");
                motionDetail = $"连续开关 6 次；首次渲染 {openingTimes[0]:0.0} ms，最大 {openingTimes.Max():0.0} ms；帧间隔 P95 {p95:0.0} ms；未创建窗口且表单被复用";
            }
            finally { host.Close(); }
        });
        if (results.Count > beforeMotion && results[^1].Passed) results[^1] = results[^1] with { Detail = motionDetail };

        await Run(results, "窗口内新建面板输入、校验、取消与快速重开", async () =>
        {
            var config = new WorkspaceConfig();
            var host = new MainWindow(config, () => Task.CompletedTask, () => { }, _ => Task.CompletedTask) { IsPreview = true };
            host.Show();
            try
            {
                await host.GroupEditor.PrepareAsync();
                host.OpenGroupPanel();
                var editor = host.GroupEditor;
                Assert(!editor.CreateAction.IsEnabled, "空名称不应能创建");
                editor.NameInput.Text = "   ";
                Assert(!editor.CreateAction.IsEnabled, "空白名称不应能创建");
                editor.NameInput.Text = "  流畅创建测试  ";
                editor.IconList.SelectedIndex = 7;
                editor.DisplayState.SelectedIndex = 2;
                editor.ShowOnDesktop.IsChecked = false;
                Assert(editor.CreateAction.IsEnabled && editor.NameHint.Visibility == Visibility.Collapsed, "名称输入未更新校验或占位符");
                await Task.Delay(360);
                SaveVisual(host, Path.Combine(Environment.CurrentDirectory, "implementation-inline-group-input.png"));
                editor.CancelAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(config.Groups.Count == 0, "取消时创建了分组");
                host.OpenGroupPanel(); // Reopen before the previous close animation completes.
                await Task.Delay(360);
                Assert(host.IsGroupPanelOpen && host.CreateGroupLayer.Visibility == Visibility.Visible, "旧关闭动画隐藏了重开的面板");
                Assert(editor.NameInput.Text.Length == 0 && editor.DisplayState.SelectedIndex == 0 && editor.ShowOnDesktop.IsChecked == true,
                    "重开时没有清空旧输入");
                editor.NameInput.Text = "  新建图片  ";
                editor.IconList.SelectedIndex = 7;
                editor.DisplayState.SelectedIndex = 2;
                editor.ShowOnDesktop.IsChecked = false;
                editor.CreateAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(config.Groups.Count == 1 && config.Groups[0].Name == "新建图片" && !config.Groups[0].IsEnabled &&
                       config.Groups[0].State == GroupDisplayState.Collapsed && config.Groups[0].CollapseStyle == CollapseStyle.IconOnly,
                    "创建未保留名称、图标模式或桌面显示选择");
                Assert(!host.IsGroupPanelOpen && host.WorkspaceContent.IsHitTestVisible, "创建后页面未恢复操作");
            }
            finally { host.Close(); }
        });

        await Run(results, "外观页滚轮平滑过渡、反向与缓存释放", async () =>
        {
            var host = new MainWindow(new WorkspaceConfig(), () => Task.CompletedTask, () => { }, _ => Task.CompletedTask) { IsPreview = true, Height = 680 };
            host.Show();
            try
            {
                Invoke(host.AppearanceNav); await Idle();
                var scroll = host.AppearancePage;
                Assert(scroll.ScrollableHeight > 80, "测试页面没有可滚动内容");
                var content = (FrameworkElement)scroll.Content;
                var originalCache = content.CacheMode;
                void Wheel(int delta) => scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
                Wheel(120); await Idle();
                Assert(!scroll.IsAnimating, "滚动到边界后不应创建动画缓存");
                Wheel(-120); await Task.Delay(35);
                if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
                    Assert(scroll.IsAnimating && scroll.DisplayedOffset > 0 && scroll.DisplayedOffset < scroll.VerticalOffset,
                        "滚轮仍然直接跳到终点，未形成中间帧");
                Wheel(-120); await Task.Delay(25); Wheel(120);
                await Task.Delay(260);
                Assert(!scroll.IsAnimating && ReferenceEquals(content.CacheMode, originalCache) && Math.Abs(scroll.DisplayedOffset-scroll.VerticalOffset)<.01,
                    "动画完成后未释放临时缓存或还原位置");
                Wheel(-120); await Task.Delay(20);
                scroll.ScrollToVerticalOffset(0); await Idle();
                Assert(!scroll.IsAnimating && Math.Abs(scroll.VerticalOffset)<.01, "程序滚动或滚动条操作未中断过渡");
                Assert(Controls.SoftBorder.ShadowCacheBytes <= Controls.SoftBorder.ShadowCacheBudget, "阴影缓存超过预算");
            }
            finally { host.Close(); }
        });

        await Run(results, "常驻绘制预算与多窗口关闭回收", async () =>
        {
            await GroupSystemIconService.WarmAsync();
            var icon=GroupSystemIconService.GetCachedIcon("\uE943");
            var original=RenderOptions.ProcessRenderMode;
            using (var budget=new RenderingBudgetService())
            {
                var first=new Window { Width=160,Height=100,ShowInTaskbar=false };
                var second=new Window { Width=160,Height=100,ShowInTaskbar=false };
                RenderingBudgetService.Watch(first);RenderingBudgetService.Watch(second);
                try
                {
                    Assert(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.SoftwareOnly,"后台绘制没有进入低占用模式");
                    first.Show();await Idle();
                    Assert(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.Default,"打开管理界面未恢复硬件加速");
                    second.Show();
                    first.WindowState=WindowState.Minimized;second.WindowState=WindowState.Minimized;await Idle();
                    Assert(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.SoftwareOnly,"所有交互窗口最小化后仍保留高开销绘制模式");
                    second.WindowState=WindowState.Normal;await Idle();
                    Assert(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.Default,"恢复窗口后没有恢复硬件加速");
                    first.Close();await Idle();
                    Assert(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.Default,"还有对话框时提前切换了绘制模式");
                    second.Close();await Idle();
                    Assert(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.SoftwareOnly,"关闭最后一个交互窗口后没有回到低占用模式");
                    Assert(GroupIconStorageService.CachedPreviewCount==0,"管理预览缓存没有释放");
                    Assert(icon is not null && ReferenceEquals(icon,GroupSystemIconService.GetCachedIcon("\uE943")),"回收文件缓存后内置图标丢失");
                }
                finally { first.Close();second.Close(); }
            }
            Assert(RenderOptions.ProcessRenderMode==original,"绘制预算释放后未还原原设置");
        });

        await Run(results, "未启用分组不创建窗口且启停不丢失内容", async () =>
        {
            var config=new WorkspaceConfig { AttachToDesktop=false };
            var enabled=ConfigService.NewGroup("启用组");
            var disabled=ConfigService.NewGroup("停用组");disabled.IsEnabled=false;
            config.Groups.Add(enabled);config.Groups.Add(disabled);
            DesktopItemService.AddPath(disabled,sample);
            var windows=new Dictionary<Guid,DesktopGroupWindow>();
            DesktopGroupWindow Create(CategoryGroup group)=>new(group,config,()=>Task.CompletedTask,()=>{},_=>Task.CompletedTask);
            try
            {
                DesktopGroupWindowPool.Update(config,windows,Create);await Idle();
                Assert(windows.Count==1 && !windows.ContainsKey(disabled.Id),"停用分组仍创建并隐藏了原生窗口");
                disabled.IsEnabled=true;DesktopGroupWindowPool.Update(config,windows,Create);await Idle();
                Assert(windows.Count==2 && windows[disabled.Id].IsVisible,"启用分组后未显示窗口");
                disabled.IsEnabled=false;DesktopGroupWindowPool.Update(config,windows,Create);await Idle();
                Assert(windows.Count==1 && disabled.Items.Count==1 && File.Exists(sample),"停用没有释放窗口或改变了用户文件");
            }
            finally { foreach(var window in windows.Values){window.AllowClose=true;window.Close();} }
        });

        await Run(results, "关闭分组后解除模型事件引用", async () =>
        {
            var config=new WorkspaceConfig { AttachToDesktop=false };
            var group=ConfigService.NewGroup("仍保留的模型");config.Groups.Add(group);
            var weak=await CloseGroupForCollectionCheck(group,config);
            await Task.Delay(80);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            Assert(!weak.IsAlive,"分组模型的集合事件仍然持有已关闭窗口");
            GC.KeepAlive(config);
        });

        await Run(results, "大图标缩略解码与预览缓存上限", () =>
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0,0,2048,1024));
            var bitmap = new RenderTargetBitmap(2048,1024,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
            var file = Path.Combine(tempRoot,"large-icon.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream=File.Create(file)) encoder.Save(stream);
            Assert(GroupIconStorageService.TryValidate(file,out _), "有效大图被错误拒绝");
            var preview = GroupIconStorageService.LoadPreview(file) as BitmapSource;
            Assert(preview is { PixelWidth: 128, PixelHeight: 64 } && preview.IsFrozen, "图标仍按完整分辨率保存在预览缓存中");
            for (var i=0;i<GroupIconStorageService.PreviewCacheLimit+3;i++)
            {
                var copy=Path.Combine(tempRoot,$"cache-icon-{i}.png"); File.Copy(file,copy);
                Assert(GroupIconStorageService.LoadPreview(copy) is not null,"缓存轮换导致有效图标不可用");
            }
            Assert(GroupIconStorageService.CachedPreviewCount <= GroupIconStorageService.PreviewCacheLimit, "图标缓存无上限增长");
            return Task.CompletedTask;
        });

        await Run(results, "预览快照创建、整理、撤销和恢复使用真实服务", async () =>
        {
            var workspace = new PreviewWorkspaceService(Path.Combine(tempRoot, "FunctionalPreview"));
            var snapshot = await workspace.CreateSnapshotAsync("整理前", false);
            Assert((await workspace.Snapshots.ListAsync()).Count == 1, "预览快照未写入本地存储");
            var candidates = await workspace.ScanAsync();
            Assert(candidates.Count == 3 && candidates.All(c => c.Path.StartsWith(workspace.DesktopFolder)), "预览扫描越出了示例桌面");
            var applied = await workspace.Organization.ApplyAsync(workspace.Config, candidates);
            Assert(applied.Applied == 2 && workspace.Config.Groups.Sum(g => g.Items.Count) == 2, "示例项目没有实际加入分组");
            Assert((await workspace.Snapshots.ListAsync()).Count == 2, "整理前未创建自动快照");
            var undone = await workspace.Organization.UndoAsync(workspace.Config);
            Assert(undone.Restored == 2 && workspace.Config.Groups.All(g => g.Items.Count == 0), "预览撤销没有恢复示例桌面");
            Assert(candidates.All(c => File.Exists(c.Path) && !File.GetAttributes(c.Path).HasFlag(FileAttributes.Hidden)), "撤销后示例文件未恢复可见");
            await workspace.Organization.ApplyAsync(workspace.Config, await workspace.ScanAsync());
            await workspace.RestoreSnapshotAsync(snapshot);
            Assert(workspace.Config.Groups.Count == 3 && workspace.Config.Groups.All(g => g.Items.Count == 0), "预览快照没有恢复分组布局");
        });

        await Run(results, "创建快照显示成功、空状态与失败反馈", async () =>
        {
            var workspace = new PreviewWorkspaceService(Path.Combine(tempRoot, "SnapshotFeedback"));
            var fail = false;
            var window = new MainWindow(workspace.Config, workspace.SaveAsync, () => { }, workspace.DeleteGroupAsync,
                snapshotService: workspace.Snapshots,
                createSnapshot: (name, automatic) => fail ? Task.FromException<LayoutSnapshot>(new IOException("模拟保存失败")) : workspace.CreateSnapshotAsync(name, automatic)) { IsPreview = true };
            try
            {
                window.Show(); await Idle();
                Invoke(window.SnapshotsNav);
                await WaitUntil(() => window.SnapshotEmptyState.IsVisible);
                SchedulePrompt("可恢复的布局");
                Invoke(window.CreateSnapshotButton);
                await WaitUntil(() => window.SnapshotList.Items.Count == 1 && window.CreateSnapshotButton.IsEnabled);
                Assert(window.OperationStatus.Text == LocalizationService.Current.Get("Action_SnapshotCreated"), "保存后没有成功反馈");
                Assert(!window.SnapshotEmptyState.IsVisible, "快照列表不为空时仍显示空状态");
                SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-snapshot-feedback.png"));
                fail = true;
                SchedulePrompt("保存失败测试");
                Invoke(window.CreateSnapshotButton);
                await WaitUntil(() => window.OperationStatus.Text.Contains("模拟保存失败") && window.CreateSnapshotButton.IsEnabled);
                Assert(window.SnapshotList.Items.Count == 1, "失败操作影响了已有快照");
                Invoke(window.GroupUndoButton); await Idle();
                Assert(window.OperationNotice.IsVisible, "不可用操作仍然无提示返回");
            }
            finally { window.Close(); }
        });

        await Run(results, "管理中心导航与批量状态按钮", async () =>
        {
            var config = new WorkspaceConfig();
            var group = ConfigService.NewGroup("交互测试");
            config.Groups.Add(group);
            var saves = 0;
            var refreshes = 0;
            var testSnapshots = new SnapshotService(Path.Combine(tempRoot, "UiSnapshots"));
            await testSnapshots.CreateAsync(config, "当前使用的布局");
            var window = new MainWindow(config, () => { saves++; return Task.CompletedTask; }, () => refreshes++, deleted =>
            {
                config.Groups.Remove(deleted);
                return Task.CompletedTask;
            }, _ => true, snapshotService: testSnapshots,
                createSnapshot: (name, automatic) => testSnapshots.CreateAsync(config, name, automatic),
                restoreSnapshot: _ => Task.CompletedTask, themeChanged: mode => config.ThemeMode = mode);
            window.Show();
            await Idle();
            Invoke(window.GroupsNav); await Idle(); Assert(window.GroupsPage.Visibility == Visibility.Visible, "分组管理导航不可用");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-groups-actions-fixed.png"));
            Invoke(window.AppearanceNav); await Idle(); Assert(window.AppearancePage.Visibility == Visibility.Visible, "外观设置导航不可用");
            foreach (var thumbnail in new[] { window.CapsuleStyleIcon, window.VerticalStyleIcon, window.IconOnlyStyleIcon })
            {
                Assert(FindVisualChildren<Image>(thumbnail).Any(image => image.Source is not null && image.IsVisible && image.ActualWidth > 0),
                    "收起样式缩略图未显示对应分组图标");
            }
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-appearance-redesign.png"));
            Assert(window.MonitorList.Items.Count == DisplayTopologyService.Current.Monitors.Count && window.MonitorList.Items.Count > 0,
                "外观页未显示真实的 Windows 显示器列表");
            window.RegionSelector.SelectedIndex = 2; await Idle();
            Assert(config.RegionCulture == "en-US" && LocalizationService.Current.FormatCulture.Name == "en-US", "区域格式选项未应用英文格式");
            window.RegionSelector.SelectedIndex = 0; await Idle();
            Invoke(window.PreviewCollapsedButton); await Idle(); Assert(window.PreviewCollapsedContent.Visibility == Visibility.Visible, "预览按钮未切换到收起状态");
            Assert(window.PreviewPanel.Width == 220 && window.PreviewPanel.Height == 62 && window.PreviewCollapsedName.Text == group.Name,
                "横向胶囊预览未复用实际分组尺寸或分组名称");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-preview-collapsed-parity.png"));
            window.RadiusSlider.Value = 25;
            await Idle();
            Assert(Math.Abs(config.CornerRadius - 25) < 0.1 && Math.Abs(window.PreviewPanel.CornerRadius.TopLeft - 25) < 0.1,
                "收起状态预览未在拖动圆角滑块时同步更新");
            window.VerticalStyle.IsChecked = true; await Idle();
            Assert(window.PreviewPanel.Width == 94 && window.PreviewPanel.Height == 126,
                "竖向图标预览尺寸与实际桌面分组不一致");
            window.IconStyle.IsChecked = true; await Idle();
            Assert(window.PreviewPanel.Width == 64 && window.PreviewPanel.Height == 64,
                "仅图标预览尺寸与实际桌面分组不一致");
            Invoke(window.PreviewIconOnlyButton); await Idle(); Assert(window.PreviewIconOnlyContent.Visibility == Visibility.Visible, "预览按钮未切换到仅图标状态");
            Invoke(window.PreviewExpandedButton); await Idle(); Assert(window.PreviewExpandedContents.Visibility == Visibility.Visible, "预览按钮未切换到展开状态");
            Assert(window.OpacitySlider.Minimum == 0 && window.OpacitySlider.Maximum == 1, "透明度范围不是 0%–100%");
            Assert(window.OpacitySlider.IsMoveToPointEnabled && window.OpacitySlider.IsSnapToTickEnabled && window.OpacitySlider.SmallChange == 0.01,
                "透明度滑轨未按点击位置设置百分比");
            window.OpacitySlider.Value = 0.32;
            await Idle();
            Assert(Math.Abs(config.PanelOpacity - 0.32) < 0.001, "32% 透明度中间值未写入配置");
            Assert(window.PreviewSurface.Opacity == 1 && window.PreviewSurface.Background is SolidColorBrush previewFill &&
                   previewFill.Color.A == 82 && window.PreviewPanel.Opacity == 1,
                "预览面板必须使用与桌面分类相同的 Alpha 填充，且不能淡化图标和文字");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-appearance-preview-32.png"));
            Assert(window.RadiusSlider.Minimum == 0 && window.RadiusSlider.Maximum == 100 &&
                   window.RadiusSlider.IsMoveToPointEnabled && window.RadiusSlider.IsSnapToTickEnabled,
                "圆角范围或滑轨点击设置不完整");
            window.RadiusSlider.Value = 73;
            await Idle();
            Assert(Math.Abs(config.CornerRadius - 73) < 0.001, "圆角滑块中间值未实时写入配置");
            var previewScale = Math.Min(window.PreviewPanel.Width / Math.Max(260, group.Width), window.PreviewPanel.Height / Math.Max(170, group.Height));
            Assert(window.PreviewPanel.CornerRadius.TopLeft is >= 0 && Math.Abs(window.PreviewPanel.CornerRadius.TopLeft - Math.Min(73 * previewScale, Math.Min(window.PreviewPanel.Width, window.PreviewPanel.Height) / 2)) < 0.1,
                "展开预览圆角未按实际分组比例缩放");
            Invoke(window.SnapshotsNav); await Idle(); await Task.Delay(80); Assert(window.SnapshotsPage.Visibility == Visibility.Visible, "布局快照导航不可用");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-snapshots-window.png"));
            Invoke(window.QuickNav); await Idle(); Assert(window.QuickPage.Visibility == Visibility.Visible, "快捷操作导航不可用");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-quick-redesign.png"));
            Invoke(window.AboutNav); await Idle(); Assert(window.AboutPage.Visibility == Visibility.Visible, "关于导航不可用");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-about-redesign.png"));
            Invoke(window.HomeNav); await Idle(); Assert(window.HomePage.Visibility == Visibility.Visible, "首页导航不可用");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-home-redesign.png"));
            Invoke(window.GroupsNav); await Idle();
            var creation = ScheduleCreateGroup("自动新建组");
            Invoke(window.NewGroupButton);
            await creation;
            await WaitUntil(() => config.Groups.Any(item => item.Name == "自动新建组"));
            Assert(config.Groups.Any(item => item.Name == "自动新建组"), "新建分组按钮未创建分组");
            Assert(!window.IsGroupPanelOpen && !window.CreateGroupLayer.IsHitTestVisible, "新建完成后背景遮罩未释放点击");
            var renameButton = FindVisualChildren<Button>(window).First(button => Equals(button.ToolTip, "重命名") && ReferenceEquals(button.DataContext, group));
            SchedulePrompt("已重命名");
            Invoke(renameButton); await WaitUntil(() => group.Name == "已重命名");
            Assert(group.Name == "已重命名", "重命名按钮未修改名称");
            Invoke(window.CollapseAllButton); await Idle(); Assert(group.State == GroupDisplayState.Collapsed, "全部收起未生效");
            Invoke(window.ExpandAllButton); await Idle(); Assert(group.State == GroupDisplayState.Expanded, "全部展开未生效");
            var deleteButton = FindVisualChildren<Button>(window).First(button => Equals(button.ToolTip, "删除") && ReferenceEquals(button.DataContext, group));
            Invoke(deleteButton); await Idle();
            Assert(!config.Groups.Contains(group), "删除分组按钮未移除分组");
            Assert(saves >= 2 && refreshes >= 2, "批量状态未触发保存或刷新");
            window.Close();
        });

        await Run(results, "桌面分类窗口点击、收起、展开、缩放和菜单", async () =>
        {
            using var renderingBudget = new RenderingBudgetService();
            var config = new WorkspaceConfig { AttachToDesktop = true, PanelOpacity = 0, CornerRadius = 28 };
            var group = ConfigService.NewGroup("桌面交互", left: 40, top: 40, width: 320, height: 210);
            config.Groups.Add(group);
            var saves = 0;
            var window = new DesktopGroupWindow(group, config, () => { saves++; return Task.CompletedTask; }, () => { }, _ => Task.CompletedTask);
            window.Show();
            await Idle();
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = GetWindowLongPtr(hwnd, -16).ToInt64();
            Assert((style & 0x40000000L) == 0, "分类窗口仍被错误改成 WS_CHILD");
            Assert(window.ExpandedCard.Background is SolidColorBrush transparent && transparent.Color.A == 0,
                "0% 面板透明度未传递到分类窗口");
            void AssertUniformPanelAlpha(byte expected)
            {
                var bitmap = new RenderTargetBitmap((int)window.ExpandedCard.ActualWidth,
                    (int)window.ExpandedCard.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window.ExpandedCard);
                var headerPixel = new byte[4];
                var bodyPixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect(160, 15, 1, 1), headerPixel, 4, 0);
                bitmap.CopyPixels(new Int32Rect(160, 155, 1, 1), bodyPixel, 4, 0);
                Assert(headerPixel[3] == expected && bodyPixel[3] == expected,
                    $"标题栏与面板透明度不一致：标题 {headerPixel[3]}，面板 {bodyPixel[3]}，预期 {expected}");
            }
            AssertUniformPanelAlpha(0);
            config.PanelOpacity = 0.01;
            window.RefreshVisualState();
            Assert(window.ExpandedCard.Background is SolidColorBrush onePercent && onePercent.Color.A == 3,
                "1% 面板透明度未按线性 Alpha 映射");
            config.PanelOpacity = 0.5;
            window.RefreshVisualState();
            Assert(window.ExpandedCard.Background is SolidColorBrush halfOpacity && halfOpacity.Color.A == 128,
                "50% 面板透明度未按线性 Alpha 映射");
            AssertUniformPanelAlpha(128);
            config.PanelOpacity = 0.99;
            window.RefreshVisualState();
            Assert(window.ExpandedCard.Background is SolidColorBrush nearOpaque && nearOpaque.Color.A == 252,
                "99% 面板透明度未按线性 Alpha 映射");
            config.PanelOpacity = 0;
            window.RefreshVisualState();
            var roundedProbe = CreateRectRgn(0, 0, 1, 1);
            try { Assert(GetWindowRgn(hwnd, roundedProbe) != 0, "圆角没有应用到 Win32 窗口区域"); }
            finally { DeleteObject(roundedProbe); }
            config.PanelOpacity = 1;
            config.CornerRadius = 0;
            window.RefreshVisualState();
            await Idle();
            Assert(window.ExpandedCard.Background is SolidColorBrush opaque && opaque.Color.A == 255,
                "100% 面板透明度未传递到分类窗口");
            var squareProbe = CreateRectRgn(0, 0, 1, 1);
            try { Assert(GetWindowRgn(hwnd, squareProbe) == 0, "0px 圆角未恢复矩形窗口区域"); }
            finally { DeleteObject(squareProbe); }
            config.CornerRadius = 100;
            window.RefreshVisualState();
            await Idle();
            window.ApplyContentClip();
            Assert(window.ExpandedCard.Clip is RectangleGeometry expandedClip &&
                   Math.Abs(expandedClip.RadiusX - 100) < 0.1 && Math.Abs(expandedClip.RadiusY - 100) < 0.1 &&
                   Math.Abs(expandedClip.Rect.Width - window.ExpandedCard.ActualWidth) < 0.1 &&
                   Math.Abs(expandedClip.Rect.Height - window.ExpandedCard.ActualHeight) < 0.1,
                "最大圆角未裁剪展开组件的最外层四角");
            Assert(window.Header.Margin.Left >= 49 && window.Header.Margin.Right >= 49,
                "最大圆角时标题栏内容没有避让外层圆弧");
            AssertRoundedHwnd(hwnd, "最大圆角没有裁剪桌面 HWND 的外层四角");
            window.Width += 24;
            window.Height += 18;
            await Idle();
            AssertRoundedHwnd(hwnd, "窗口尺寸变化后没有重新应用桌面 HWND 圆角");
            SaveVisual(window, Path.Combine(Environment.CurrentDirectory, "implementation-rounded-group.png"));
            window.ReattachToDesktop(); await Idle();
            Assert(GetWindow(hwnd, 4) != IntPtr.Zero, "分类窗口没有桌面 owner");
            Invoke(window.CollapseButton); await Idle();
            Assert(group.State == GroupDisplayState.Collapsed, "收起按钮未改变状态");
            group.CollapseStyle = CollapseStyle.VerticalIcon;
            window.RefreshVisualState();
            await Idle();
            window.ApplyContentClip();
            Assert(Math.Abs(window.Width - 94) < 0.1 && Math.Abs(window.Height - 126) < 0.1, "竖向收起样式尺寸错误");
            Assert(window.CollapsedCard.Clip is RectangleGeometry collapsedClip && Math.Abs(collapsedClip.RadiusX - 47) < 0.1,
                "最大圆角未裁剪收起组件的最外层四角");
            var rowsBeforeAppearanceDrag = window.CollapsedLayout.RowDefinitions.Count;
            var columnsBeforeAppearanceDrag = window.CollapsedLayout.ColumnDefinitions.Count;
            var widthBeforeAppearanceDrag = window.Width;
            var heightBeforeAppearanceDrag = window.Height;
            config.PanelOpacity = 0.59;
            config.CornerRadius = 33;
            window.RefreshAppearanceOnly();
            Assert(window.CollapsedCard.Visibility == Visibility.Visible && window.ExpandedCard.Visibility == Visibility.Collapsed &&
                   window.CollapsedLayout.RowDefinitions.Count == rowsBeforeAppearanceDrag &&
                   window.CollapsedLayout.ColumnDefinitions.Count == columnsBeforeAppearanceDrag &&
                   Math.Abs(window.Width - widthBeforeAppearanceDrag) < 0.1 && Math.Abs(window.Height - heightBeforeAppearanceDrag) < 0.1,
                "滑块实时外观刷新重建了收起布局或切换了图标容器");
            Assert(window.CollapsedCard.Background is SolidColorBrush liveOpacity && liveOpacity.Color.A == 150 &&
                   Math.Abs(window.CollapsedCard.CornerRadius.TopLeft - 33) < 0.1 &&
                   window.CollapsedCard.Clip is RectangleGeometry liveClip && Math.Abs(liveClip.RadiusX - 33) < 0.1,
                "滑块实时外观刷新未同步应用透明度和圆角");
            window.CommitCornerRadius();
            config.CornerRadius = 100;
            window.RefreshAppearanceOnly();
            window.CommitCornerRadius();
            await Idle();
            var collapsedLeft = group.Left;
            var collapsedTop = group.Top;
            window.MoveCollapsedTo(collapsedLeft + 31, collapsedTop + 24);
            Assert(Math.Abs(group.Left - collapsedLeft - 31) < 0.1 && Math.Abs(group.Top - collapsedTop - 24) < 0.1,
                "收起状态无法拖动更新位置");
            var mouseUp = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                Source = window.CollapsedCard
            };
            window.CollapsedCard.RaiseEvent(mouseUp); await Idle();
            Assert(group.State == GroupDisplayState.Expanded, "点击收起卡片未重新展开");
            window.PopulateGroupMenu(window.ContextMenu!);
            var iconOnly = window.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "Context_CollapseStyle")).Items.OfType<MenuItem>().Single(item => Equals(item.Tag, "Collapse_IconOnly"));
            Invoke(iconOnly); await Idle();
            Assert(group.State == GroupDisplayState.Collapsed && group.CollapseStyle == CollapseStyle.IconOnly,
                "右键菜单的仅图标样式未生效");
            window.PopulateGroupMenu(window.ContextMenu!);
            var expand = window.ContextMenu.Items.OfType<MenuItem>().First(item => Equals(item.Header, "展开"));
            Invoke(expand); await Idle();
            var oldWidth = group.Width;
            var oldHeight = group.Height;
            window.ResizeThumb.RaiseEvent(new DragDeltaEventArgs(28, 22)); await Idle();
            Assert(group.Width >= oldWidth + 27 && group.Height >= oldHeight + 21, "缩放拖柄未更新尺寸");
            Assert(window.ContextMenu?.Items.Count >= 8, "右键菜单项目不完整");
            Assert(saves >= 2, "交互没有保存状态");
            window.AllowClose = true;
            window.Close();
        });

        await Run(results, "右键入口分组选择器", () =>
        {
            var config = new WorkspaceConfig();
            var first = ConfigService.NewGroup("目标分组");
            config.Groups.Add(first);
            CategoryGroup? selected = null;
            var picker = new AddToGroupWindow(sample, config,
                group => { selected = group; return Task.CompletedTask; },
                () => Task.FromResult<CategoryGroup?>(null));
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                picker.UpdateLayout();
                var button = FindVisualChildren<Button>(picker).First(control => control.DataContext is CategoryGroup);
                Invoke(button);
            }), DispatcherPriority.ApplicationIdle);
            var accepted = picker.ShowDialog();
            Assert(accepted == true, "选择器未返回成功");
            Assert(ReferenceEquals(selected, first), "选择器没有返回点击的分组");

            var created = ConfigService.NewGroup("右键新建组");
            selected = null;
            var createPicker = new AddToGroupWindow(sample, config,
                group => { selected = group; return Task.CompletedTask; },
                () => Task.FromResult<CategoryGroup?>(created));
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => Invoke(createPicker.CreateAndAddButton)),
                DispatcherPriority.ApplicationIdle);
            Assert(createPicker.ShowDialog() == true, "创建新分组并放入未返回成功");
            Assert(ReferenceEquals(selected, created), "创建新分组并放入未选择新组");
            return Task.CompletedTask;
        });

        await Run(results, "新建/重命名文本对话框", () =>
        {
            var prompt = new TextPromptWindow("测试", "名称", "旧名称");
            var layoutValid = false;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                prompt.UpdateLayout();
                layoutValid = prompt.ActualHeight >= 245 && prompt.Input.ActualHeight >= 40;
                Assert(prompt.Input.SelectionTextBrush is SolidColorBrush selectedText &&
                    prompt.Input.Foreground is SolidColorBrush normalText && selectedText.Color == normalText.Color,
                    "浅色选区中的文字应保持可读");
                SaveVisual(prompt, Path.Combine(Environment.CurrentDirectory, "implementation-text-input-fixed.png"));
                prompt.Input.Text = "新名称";
                Invoke(prompt.ConfirmButton);
            }), DispatcherPriority.ApplicationIdle);
            Assert(prompt.ShowDialog() == true, "文本对话框无法确认");
            Assert(layoutValid, "文本输入框可用高度不足");
            Assert(prompt.Result == "新名称", "文本对话框结果错误");
            return Task.CompletedTask;
        });

        await Run(results, "新建分组弹窗、实时预览与上传图标", () =>
        {
            var iconsFolder = Path.Combine(tempRoot, "GroupIcons");
            var pngPath = Path.Combine(tempRoot, "custom-icon.png");
            var bitmap = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawRoundedRectangle(Brushes.DodgerBlue, null, new Rect(0, 0, 48, 48), 10, 10);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(pngPath)) encoder.Save(stream);
            Assert(GroupIconStorageService.TryValidate(pngPath, out _), "有效 PNG 图标未通过校验");
            var invalidPath = Path.Combine(tempRoot, "invalid.txt");
            File.WriteAllText(invalidPath, "not an icon");
            Assert(!GroupIconStorageService.TryValidate(invalidPath, out _), "不支持的图标格式未被拒绝");

            var dialog = new CreateGroupWindow();
            var initialButtonDisabled = false;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                initialButtonDisabled = !dialog.CreateButton.IsEnabled;
                Assert(dialog.IconChoices.Items.Count == 18, "内置图标数量错误");
                Assert(ScrollViewer.GetVerticalScrollBarVisibility(dialog.IconChoices) == ScrollBarVisibility.Auto,
                    "新增图标无法滚动访问");
                var typeface = new Typeface(new FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                Assert(typeface.TryGetGlyphTypeface(out var glyphTypeface), "系统图标字体不可用");
                for (var index = 12; index < 18; index++)
                {
                    dialog.IconChoices.SelectedIndex = index;
                    var choice = (CreateGroupWindow.IconChoice)dialog.IconChoices.SelectedItem;
                    Assert(glyphTypeface.CharacterToGlyphMap.ContainsKey(choice.Glyph[0]), "新增系统图标缺少字形");
                    Assert(dialog.PreviewName.Text == choice.Name && dialog.PreviewGlyph.Text == choice.Glyph,
                        "新增图标预览不同步");
                    Assert(dialog.PreviewImage.Source is not null, "内置选项未加载 Windows 系统图标");
                }
                dialog.IconChoices.SelectedIndex = 0;
                SaveVisual(dialog, Path.Combine(Environment.CurrentDirectory, "implementation-create-group-dialog.png"));
                dialog.GroupNameBox.Text = "设计素材";
                dialog.IconChoices.SelectedIndex = 7;
                dialog.DefaultStateCombo.SelectedIndex = 2;
                dialog.ShowOnDesktopCheck.IsChecked = false;
                dialog.UploadTabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(dialog.SetUploadedFile(pngPath), "上传图标未生成预览");
                SaveVisual(dialog, Path.Combine(Environment.CurrentDirectory, "implementation-create-group-upload.png"));
                Invoke(dialog.CreateButton);
            }), DispatcherPriority.ApplicationIdle);
            Assert(dialog.ShowDialog() == true, "新建分组弹窗无法确认");
            Assert(initialButtonDisabled, "未填写名称时创建按钮没有禁用");
            Assert(dialog.Result is { Name: "设计素材", State: GroupDisplayState.Collapsed,
                CollapseStyle: CollapseStyle.IconOnly, ShowOnDesktop: false, UploadPath: not null }, "弹窗结果与所选设置不一致");
            var storage = new GroupIconStorageService(iconsFolder);
            var stored = storage.Store(dialog.Result!.UploadPath!);
            Assert(File.Exists(stored) && Path.GetDirectoryName(stored) == iconsFolder, "上传图标未保存到本地数据目录");
            Assert(GroupIconStorageService.LoadPreview(stored) is not null, "持久化图标无法重新预览");

            var cancelled = new CreateGroupWindow();
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => Invoke(cancelled.CancelButton)),
                DispatcherPriority.ApplicationIdle);
            Assert(cancelled.ShowDialog() == false && cancelled.Result is null, "取消新建仍产生了分组结果");
            var originalCulture = LocalizationService.Current.Culture.Name;
            try
            {
                foreach (var culture in new[] { "zh-CN", "en-US" })
                {
                    LocalizationService.Current.SetCulture(culture);
                    var builtInDialog = new CreateGroupWindow();
                    CreateGroupWindow.IconChoice? selected = null;
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        builtInDialog.IconChoices.SelectedIndex = 17;
                        selected = (CreateGroupWindow.IconChoice)builtInDialog.IconChoices.SelectedItem;
                        builtInDialog.IconChoices.ScrollIntoView(selected);
                        builtInDialog.GroupNameBox.Text = selected.Name;
                        SaveVisual(builtInDialog, Path.Combine(Environment.CurrentDirectory, $"implementation-create-icons-{culture}.png"));
                        Invoke(builtInDialog.CreateButton);
                    }), DispatcherPriority.ApplicationIdle);
                    Assert(builtInDialog.ShowDialog() == true && builtInDialog.Result is { UploadPath: null } result &&
                        result.Glyph == selected!.Glyph && result.Accent == selected.Accent, "内置图标创建结果错误");
                    foreach (var key in new[] { "Icon_Finance", "Icon_Proxy", "Icon_Excel", "Icon_PowerPoint", "Icon_Pdf", "Icon_Word" })
                        Assert(LocalizationService.Current.Get(key) != key, "新增图标缺少翻译");
                }
            }
            finally { LocalizationService.Current.SetCulture(originalCulture); }
            return Task.CompletedTask;
        });

        await Run(results, "Shell 右键菜单注册与命令参数", () =>
        {
            ShellContextMenuService.EnsureRegistered();
            using var fileCommand = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\MiniDesk.Add\command");
            using var folderCommand = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\MiniDesk.Add\command");
            using var backgroundCommand = Registry.CurrentUser.OpenSubKey(@"Software\Classes\DesktopBackground\shell\MiniDesk.NewGroup\command");
            Assert(fileCommand?.GetValue(null)?.ToString()?.Contains("--add") == true, "文件右键命令缺失");
            Assert(folderCommand?.GetValue(null)?.ToString()?.Contains("--add") == true, "文件夹右键命令缺失");
            Assert(backgroundCommand?.GetValue(null)?.ToString()?.Contains("--new-group") == true, "桌面空白处新建分组命令缺失");
            var parsed = IpcRequestService.Parse(["--add", sample]);
            Assert(parsed.Action == "add" && parsed.Path == sample, "右键启动参数解析错误");
            return Task.CompletedTask;
        });

        await Run(results, "开机启动开关可写并可恢复", () =>
        {
            const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.OpenSubKey(runPath, true) ?? Registry.CurrentUser.CreateSubKey(runPath, true);
            var original = key.GetValue("MiniDesk");
            try
            {
                StartupService.SetEnabled(true);
                Assert(StartupService.IsEnabled, "无法启用开机启动");
                StartupService.SetEnabled(false);
                Assert(!StartupService.IsEnabled, "无法关闭开机启动");
            }
            finally
            {
                if (original is null) key.DeleteValue("MiniDesk", false);
                else key.SetValue("MiniDesk", original);
            }
            return Task.CompletedTask;
        });

        await Run(results, "应用图标与 Shell 文件图标", () =>
        {
            var appIcon = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MiniDesk.ico"));
            Assert(appIcon is not null, "应用 ICO 未嵌入");
            appIcon?.Stream.Dispose();
            foreach (var variant in new[] { "Light", "Dark", "Mono" })
            {
                var icon = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/MiniDesk-{variant}.ico"));
                Assert(icon is not null, $"{variant} 图标变体未嵌入");
                icon?.Stream.Dispose();
            }
            Assert(ShellIconService.GetIcon(sample) is not null, "无法读取文件关联图标");
            return Task.CompletedTask;
        });

        await Run(results, "深色、浅色与跟随系统主题", () =>
        {
            using var themes = new AppThemeService();
            themes.Apply(AppThemeMode.Dark);
            Assert(themes.IsDark, "深色主题未生效");
            Assert(System.Windows.Application.Current.Resources["AppBackgroundBrush"] is SolidColorBrush dark && dark.Color.R < 80, "深色资源错误");
            var darkConfig = new WorkspaceConfig { ThemeMode = AppThemeMode.Dark };
            var darkWindow = new MainWindow(darkConfig, () => Task.CompletedTask, () => { }, _ => Task.CompletedTask);
            darkWindow.Show();
            Assert(darkWindow.HomeIllustration.Visibility == Visibility.Collapsed, "深色主题下未避免浅色插画底板产生的突兀白块");
            Invoke(darkWindow.AppearanceNav);
            SaveVisual(darkWindow, Path.Combine(Environment.CurrentDirectory, "implementation-dark-theme.png"));
            darkWindow.Close();
            themes.Apply(AppThemeMode.Light);
            darkWindow.RefreshTheme();
            Assert(darkWindow.HomeIllustration.Visibility == Visibility.Visible, "浅色主题下主页插画未恢复");
            Assert(!themes.IsDark, "浅色主题未生效");
            Assert(System.Windows.Application.Current.Resources["AppBackgroundBrush"] is SolidColorBrush light && light.Color.R > 200, "浅色资源错误");
            themes.Apply(AppThemeMode.System);
            return Task.CompletedTask;
        });

        await Run(results, "多显示器 DPI 坐标换算与布局字段兼容", async () =>
        {
            foreach (var dpi in new uint[] { 96, 120, 144, 168, 192, 240, 288 })
            {
                var physical = DisplayTopologyService.DipToPx(320, dpi);
                var logical = DisplayTopologyService.PxToDip(physical, dpi);
                Assert(Math.Abs(logical - 320) < 0.51, $"DPI {dpi} 坐标往返误差过大");
            }
            var path = Path.Combine(tempRoot, "display-layout.json");
            var service = new ConfigService(path);
            var config = new WorkspaceConfig();
            var group = ConfigService.NewGroup("DPI test");
            group.MonitorDeviceName = @"\\.\DISPLAY2";
            group.MonitorOffsetX = 48.5;
            group.MonitorOffsetY = 72.25;
            group.SavedDpiX = 144;
            group.SavedDpiY = 144;
            config.Groups.Add(group);
            await service.SaveAsync(config);
            var loaded = await service.LoadAsync();
            Assert(loaded.Groups[0].MonitorDeviceName == group.MonitorDeviceName &&
                   loaded.Groups[0].MonitorOffsetX == group.MonitorOffsetX && loaded.Groups[0].SavedDpiX == 144,
                "显示器归属或 DPI 布局字段未持久化");
        });

        await Run(results, "简体中文与 English 运行时切换", async () =>
        {
            var localization = LocalizationService.Current;
            localization.SetCulture("en-US");
            Assert(localization.Get("Nav_Home") == "Home" && CultureInfo.CurrentCulture.Name == "en-US", "English 资源或 Culture 未生效");
            var languageConfig = new WorkspaceConfig { Language = "en-US" };
            var languageSaves = 0;
            var englishWindow = new MainWindow(languageConfig, () => { languageSaves++; return Task.CompletedTask; }, () => { }, _ => Task.CompletedTask);
            englishWindow.Show();
            Invoke(englishWindow.AppearanceNav);
            await Idle();
            Assert(englishWindow.AppearancePage.Visibility == Visibility.Visible && englishWindow.LanguageSelector.SelectedIndex == 1,
                "英文外观页面切换失败");
            Assert(englishWindow.AppLanguageSelector.SelectedIndex == 1, "语言区域下拉框未同步初始语言");
            englishWindow.AppLanguageSelector.SelectedIndex = 0;
            await Idle();
            Assert(languageConfig.Language == "zh-CN" && englishWindow.LanguageSelector.SelectedIndex == 0 &&
                   ((StackPanel)englishWindow.HomeNav.Content).Children.OfType<TextBlock>().Last().Text == "首页" && languageSaves > 0,
                "语言区域切换中文未刷新界面或保存设置");
            englishWindow.AppLanguageSelector.SelectedIndex = 1;
            await Idle();
            Assert(languageConfig.Language == "en-US" && englishWindow.LanguageSelector.SelectedIndex == 1 &&
                   ((StackPanel)englishWindow.HomeNav.Content).Children.OfType<TextBlock>().Last().Text == "Home",
                "语言区域切换英文未刷新界面");
            englishWindow.RegionSelector.SelectedIndex = 1;
            await Idle();
            Assert(languageConfig.Language == "en-US" && localization.FormatCulture.Name == "zh-CN" && localization.Culture.Name == "en-US",
                "区域格式错误地修改了应用语言");
            englishWindow.LanguageSelector.SelectedIndex = 0;
            await Idle();
            Assert(englishWindow.AppLanguageSelector.SelectedIndex == 0, "顶部语言切换未同步语言区域选项");
            englishWindow.LanguageSelector.SelectedIndex = 1;
            await Idle();
            Assert(englishWindow.RefreshMonitorsButton.ActualWidth + 0.5 >= englishWindow.RefreshMonitorsButton.DesiredSize.Width,
                "英文显示器操作按钮宽度不足，可能会裁切文案");
            SaveVisual(englishWindow, Path.Combine(Environment.CurrentDirectory, "implementation-appearance-english.png"));
            SaveVisual(englishWindow, Path.Combine(Environment.CurrentDirectory, "implementation-english.png"));
            englishWindow.Close();
            localization.SetCulture("zh-CN");
            Assert(localization.Get("Nav_Home") == "首页" && CultureInfo.CurrentCulture.Name == "zh-CN", "简体中文资源或 Culture 未生效");
        });

        await Run(results, "整理桌面：保守分类、预览与中英文", async () =>
        {
            var desktop = Path.Combine(tempRoot, "organize-preview"); Directory.CreateDirectory(desktop);
            foreach (var name in new[] { "budget.xlsx", "notes.docx", "unknown.bin", "custom-tool.lnk", "hidden.pdf", "grouped.pdf" })
                await File.WriteAllTextAsync(Path.Combine(desktop, name), "test");
            File.SetAttributes(Path.Combine(desktop, "hidden.pdf"), FileAttributes.Hidden);
            Directory.CreateDirectory(Path.Combine(desktop, "finance"));
            var config = new WorkspaceConfig(); var office = ConfigService.NewGroup("办公"); config.Groups.Add(office);
            DesktopItemService.AddPath(office, Path.Combine(desktop, "grouped.pdf"));
            var candidates = DesktopOrganizationService.Scan(config, new[] { desktop, desktop }, _ => "C:\\Tools\\unknown.exe");
            Assert(candidates.Count == 4, "扫描包含隐藏/已分组/文件夹/重复项目");
            Assert(candidates.Single(c => c.Name == "budget.xlsx").Target?.GroupId == office.Id, "未复用办公组");
            Assert(candidates.Single(c => c.Name == "custom-tool.lnk").Target is null, "未知工具不应猜测分类");
            Assert(DesktopOrganizationService.Classify("shortcut.lnk", "C:\\Tools\\Code.exe") == "development", "目标程序识别错误");
            Assert(DesktopOrganizationService.Classify("finance.lnk", "C:\\Tools\\terminal.exe") is null, "通用程序名不应判定财经");
            var excel = ConfigService.NewGroup("Excel"); config.Groups.Add(excel);
            Assert(DesktopOrganizationService.Scan(config, new[] { desktop }).Single(c => c.Name == "budget.xlsx").Target?.GroupId == excel.Id, "已有 Excel 专用组未优先");
            foreach (var culture in new[] { "zh-CN", "en-US" })
            {
                LocalizationService.Current.SetCulture(culture);
                var preview = DesktopOrganizationService.Scan(config, new[] { desktop });
                var dialog = new OrganizeDesktopWindow(config, preview);
                dialog.Show(); await Idle();
                Assert(dialog.ApplyButton.IsEnabled, "已识别项目应允许整理");
                Assert(dialog.Summary.Text.Contains("2"), "预览数量错误");
                SaveVisual(dialog, Path.Combine(Environment.CurrentDirectory, $"implementation-organize-{culture}.png"));
                dialog.Close(); // Cancel/close: no changes to the config or desktop.
                Assert(File.GetAttributes(Path.Combine(desktop, "budget.xlsx")) == FileAttributes.Normal || !File.GetAttributes(Path.Combine(desktop, "budget.xlsx")).HasFlag(FileAttributes.Hidden), "取消修改了文件");
                Assert(config.Groups.Sum(g => g.Items.Count) == 1, "取消修改了归属");
            }
            LocalizationService.Current.SetCulture("zh-CN");
        });

        await Run(results, "整理桌面：确认、持久化、范围撤销及失败保护", async () =>
        {
            var desktop = Path.Combine(tempRoot, "organize-apply"); Directory.CreateDirectory(desktop);
            var doc = Path.Combine(desktop, "report.docx"); var picture = Path.Combine(desktop, "photo.png");
            await File.WriteAllTextAsync(doc, "document"); await File.WriteAllTextAsync(picture, "picture");
            var config = new WorkspaceConfig(); var office = ConfigService.NewGroup("办公", left: 55, top: 66); config.Groups.Add(office);
            var store = new ConfigService(Path.Combine(tempRoot, "organization.json"));
            var visibility = new DesktopVisibilityService(desktop, desktop, Path.Combine(tempRoot, "organize-storage"));
            int snapshots = 0;
            var service = new DesktopOrganizationService(visibility, () => store.SaveAsync(config), () => { snapshots++; return Task.CompletedTask; });
            var candidates = DesktopOrganizationService.Scan(config, new[] { desktop });
            var result = await service.ApplyAsync(config, candidates);
            Assert(result == (2, 0) && snapshots == 1, "确认整理数量或快照错误");
            Assert(File.Exists(doc) && File.GetAttributes(doc).HasFlag(FileAttributes.Hidden), "真实文档路径被改动或未隐藏");
            Assert(office.Left == 55 && office.Top == 66, "现有分组布局被改动");
            var saved = await store.LoadAsync(); Assert(saved.LastOrganization?.Entries.Count == 2, "撤销记录未持久化");
            office.Items[0].X += 7; // Manual reposition after applying must survive undo.
            result = await service.UndoAsync(config);
            Assert(result == (1, 1) && office.Items.Count == 1, "撤销误覆盖了手动改动");
            Assert(!File.GetAttributes(picture).HasFlag(FileAttributes.Hidden), "撤销未恢复桌面项目");
            visibility.Restore(office.Items[0]); office.Items.Clear();
            var missing = DesktopOrganizationService.Scan(config, new[] { desktop }); File.Delete(doc);
            result = await service.ApplyAsync(config, missing);
            Assert(result == (1, 1), "预览后文件消失未安全跳过");
            await service.UndoAsync(config);
            var noWrites = new DesktopOrganizationService(visibility, () => Task.FromException(new IOException("disk failure")), () => Task.CompletedTask);
            bool threw = false;
            try { await noWrites.ApplyAsync(config, DesktopOrganizationService.Scan(config, new[] { desktop })); } catch (IOException) { threw = true; }
            Assert(threw && !File.GetAttributes(picture).HasFlag(FileAttributes.Hidden), "保存失败仍修改了桌面文件");
        });

        await Run(results, "整理桌面：中断恢复与快捷方式隐藏失败", async () =>
        {
            var desktop = Path.Combine(tempRoot, "organize-recovery"); Directory.CreateDirectory(desktop);
            var link = Path.Combine(desktop, "Code.lnk"); await File.WriteAllTextAsync(link, "shortcut fixture");
            var config = new WorkspaceConfig(); var group = ConfigService.NewGroup("开发工具"); config.Groups.Add(group);
            var storage = Path.Combine(tempRoot, "recovery-storage"); var visibility = new DesktopVisibilityService(desktop, desktop, storage);
            var store = new ConfigService(Path.Combine(tempRoot, "recovery.json"));
            var service = new DesktopOrganizationService(visibility, () => store.SaveAsync(config), () => Task.CompletedTask);
            var candidates = DesktopOrganizationService.Scan(config, new[] { desktop }, _ => "C:\\Code.exe");
            Assert((await service.ApplyAsync(config, candidates)).Applied == 1 && !File.Exists(link), "快捷方式未受控收纳");
            config.LastOrganization!.Entries[0].RestorePending = true; await store.SaveAsync(config);
            Assert(visibility.Restore(group.Items[0]), "模拟中断前恢复失败"); // Crash before saving updated model.
            var loaded = await store.LoadAsync();
            var recover = new DesktopOrganizationService(visibility, () => store.SaveAsync(loaded), () => Task.CompletedTask);
            await recover.UndoAsync(loaded, pendingOnly: true); visibility.Reconcile(loaded);
            Assert(File.Exists(link) && loaded.Groups.Sum(g => g.Items.Count) == 0, "中断恢复重新隐藏或丢失快捷方式");
            var blockingStorage = Path.Combine(tempRoot, "storage-is-file"); await File.WriteAllTextAsync(blockingStorage, "block");
            var blocked = new DesktopOrganizationService(new DesktopVisibilityService(desktop, desktop, blockingStorage), () => store.SaveAsync(config), () => Task.CompletedTask);
            group.Items.Clear();
            config.LastOrganization = null; // New test fixture, not the stale pre-crash model.
            Assert((await blocked.ApplyAsync(config, DesktopOrganizationService.Scan(config, new[] { desktop }, _ => "C:\\Code.exe"))).Failed == 1, "隐藏失败未反馈");
            Assert(File.Exists(link) && group.Items.Count == 0, "隐藏失败留下错误归属或丢失快捷方式");
        });

        await Run(results, "右键菜单：即时状态、分组移动、锁定与中英文", async () =>
        {
            var config = new WorkspaceConfig { AttachToDesktop = false };
            var source = ConfigService.NewGroup("来源组", left: 40, top: 40);
            var target = ConfigService.NewGroup("Target group"); config.Groups.Add(source); config.Groups.Add(target);
            DesktopItemService.AddPath(source, sample, out var item);
            int saves = 0, restores = 0;
            var window = new DesktopGroupWindow(source, config, () => { saves++; return Task.CompletedTask; }, () => { }, _ => Task.CompletedTask,
                (sourceId, moving, targetId, point) => { Assert(GroupItemTransferService.TryMove(config, sourceId, moving, targetId, point, _ => { restores++; return true; }), "菜单移动失败"); return Task.CompletedTask; },
                createGroup: () => Task.FromResult<CategoryGroup?>(null));
            window.Show(); await Idle();
            foreach (var culture in new[] { "zh-CN", "en-US" })
            {
                LocalizationService.Current.SetCulture(culture); window.RefreshLocalization();
                var groupMenu = window.ContextMenu!; groupMenu.PlacementTarget = window.ExpandedCard; groupMenu.IsOpen = true; await Idle();
                Assert(groupMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Tag, "Context_LockPosition")), "缺少锁定入口");
                SaveElement(groupMenu, Path.Combine(Environment.CurrentDirectory, $"implementation-context-group-{culture}.png"));
                groupMenu.IsOpen = false;
                var itemMenu = new ContextMenu { PlacementTarget = window.ExpandedCard }; window.PopulateItemMenu(itemMenu, item!);
                itemMenu.IsOpen = true; await Idle();
                Assert(itemMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Context_MoveToDesktop")).Header?.ToString() == LocalizationService.Current.Get("Context_MoveToDesktop"), "桌面恢复文案未翻译");
                var destinations = itemMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Context_MoveToGroup"));
                Assert(destinations.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, source.Id)).IsChecked && !destinations.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, source.Id)).IsEnabled, "当前分组未标记或仍可重复移动");
                target.IsEnabled = false; window.PopulateItemMenu(new ContextMenu(), item!);
                var disabledMenu = new ContextMenu(); window.PopulateItemMenu(disabledMenu, item!);
                Assert(!disabledMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Context_MoveToGroup")).Items.OfType<MenuItem>().Single(m => Equals(m.Tag, target.Id)).IsEnabled, "未启用分组仍可接收图标导致不可见");
                target.IsEnabled = true;
                SaveElement(itemMenu, Path.Combine(Environment.CurrentDirectory, $"implementation-context-item-{culture}.png"));
                destinations.IsSubmenuOpen = true; await Idle();
                var late = ConfigService.NewGroup("Later added group"); config.Groups.Add(late);
                destinations.IsSubmenuOpen = false; itemMenu.IsOpen = false;
                window.PopulateItemMenu(itemMenu, item!);
                Assert(itemMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Context_MoveToGroup")).Items.OfType<MenuItem>().Any(m => Equals(m.Tag, late.Id)), "菜单没有动态反映新增分组");
                config.Groups.Remove(late);
            }
            var menu = new ContextMenu(); window.PopulateItemMenu(menu, item!);
            var moves = menu.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Context_MoveToGroup"));
            Invoke(moves.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, target.Id))); await Idle();
            Assert(source.Items.Count == 0 && target.Items.Contains(item!) && restores == 0 && item!.Path == sample, "跨组移动改变路径或未更新归属");
            Assert(!await window.ReturnItemToDesktopAsync(item!), "过期菜单可以误移除其他分组的图标");
            source.State = GroupDisplayState.Collapsed; window.RefreshVisualState(); window.PopulateGroupMenu(window.ContextMenu!);
            Invoke(window.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Context_LockPosition"))); await Idle();
            var oldLeft = source.Left; var oldTop = source.Top; var oldWidth = source.Width;
            window.MoveCollapsedTo(oldLeft + 60, oldTop + 60); window.ResizeThumb.RaiseEvent(new DragDeltaEventArgs(60, 60));
            Assert(source.IsPositionLocked && source.Left == oldLeft && source.Top == oldTop && source.Width == oldWidth && !window.ResizeThumb.IsEnabled, "锁定后仍可移动或缩放");
            window.PopulateGroupMenu(window.ContextMenu!);
            Invoke(window.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Tag, "Group_Expand"))); await Idle();
            Assert(source.State == GroupDisplayState.Expanded, "锁定不应禁止展开");
            var store = new ConfigService(Path.Combine(tempRoot, "locked-group.json")); await store.SaveAsync(config);
            Assert((await store.LoadAsync()).Groups[0].IsPositionLocked, "锁定状态没有持久化");
            Assert(saves > 0, "菜单操作未保存");
            window.AllowClose = true; window.Close(); LocalizationService.Current.SetCulture("zh-CN");
        });

        await Run(results, "右键移回桌面：恢复失败、重复及缺失目标保护", async () =>
        {
            var desktop = Path.Combine(tempRoot, "context-restore"); Directory.CreateDirectory(desktop);
            var file = Path.Combine(desktop, "keep.txt"); await File.WriteAllTextAsync(file, "safe");
            var config = new WorkspaceConfig { AttachToDesktop = false }; var group = ConfigService.NewGroup("Safe group"); config.Groups.Add(group);
            DesktopItemService.AddPath(group, file, out var item);
            var visibility = new DesktopVisibilityService(desktop, desktop, Path.Combine(tempRoot, "context-storage"));
            Assert(visibility.Hide(item!), "测试隐藏失败"); bool allowRestore = false; int saves = 0;
            var window = new DesktopGroupWindow(group, config, () => { saves++; return Task.CompletedTask; }, () => { }, _ => Task.CompletedTask,
                restoreItem: i => allowRestore && visibility.Restore(i));
            Assert(!await window.ReturnItemToDesktopAsync(item!) && group.Items.Contains(item!) && saves == 0 && File.GetAttributes(file).HasFlag(FileAttributes.Hidden), "恢复失败后丢失记录");
            allowRestore = true;
            Assert(await window.ReturnItemToDesktopAsync(item!) && group.Items.Count == 0 && File.Exists(file) && !File.GetAttributes(file).HasFlag(FileAttributes.Hidden), "成功恢复后文件或归属错误");
            Assert(!await window.ReturnItemToDesktopAsync(item!), "重复移回桌面没有保护");
            DesktopItemService.AddPath(group, file, out item);
            var target = ConfigService.NewGroup("Target"); config.Groups.Add(target); DesktopItemService.AddPath(target, file);
            Assert(!GroupItemTransferService.TryMove(config, group.Id, item!, target.Id, default, _ => true) && group.Items.Contains(item!), "重复目标导致来源项丢失");
            Assert(!GroupItemTransferService.TryMove(config, group.Id, item!, Guid.NewGuid(), default, _ => true), "不存在的分组仍可接受项目");
            Assert(ShellItemActionsService.GetFolder(new DesktopItem { Path = Path.Combine(tempRoot, "context-storage", "managed.lnk"), OriginalDesktopPath = Path.Combine(desktop, "original.lnk") }) == desktop, "打开位置泄露了内部快捷方式存储目录");
            window.AllowClose = true; window.Close();
        });

        await Run(results, "更换图标：复用选择器且保持分组其他设置", async () =>
        {
            var group = ConfigService.NewGroup("原有分组", state: GroupDisplayState.Collapsed); group.IsPositionLocked = true;
            var dialog = new CreateGroupWindow(group);
            dialog.Show(); await Idle();
            Assert(dialog.GroupNameBox.IsReadOnly && dialog.GroupNameBox.Text == group.Name && dialog.StateSettings.Visibility == Visibility.Collapsed, "更换图标仍允许修改归属设置");
            dialog.IconChoices.SelectedIndex = 14; await Idle();
            SaveVisual(dialog, Path.Combine(Environment.CurrentDirectory, "implementation-change-group-icon.png"));
            dialog.Close();
            Assert(group.State == GroupDisplayState.Collapsed && group.IsPositionLocked && group.Glyph == "\uE7C3", "取消更换图标修改了分组");
        });

        try { Directory.Delete(tempRoot, true); } catch { }
        var passed = results.All(result => result.Passed);
        var outputPath = Environment.GetEnvironmentVariable("MINIDESK_SELFTEST_OUTPUT") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniDesk", "self-test-latest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
        {
            passed,
            timestamp = DateTimeOffset.Now,
            runtime = Environment.Version.ToString(),
            os = Environment.OSVersion.ToString(),
            results
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed;
    }

    private static async Task Run(List<Result> results, string name, Func<Task> test)
    {
        var filter = Environment.GetEnvironmentVariable("MINIDESK_SELFTEST_FILTER");
        if (!string.IsNullOrWhiteSpace(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await test();
            results.Add(new Result(name, true, "通过"));
        }
        catch (Exception ex)
        {
            results.Add(new Result(name, false, ex.GetBaseException().Message));
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Invoke(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var provider = (IInvokeProvider?)peer.GetPattern(PatternInterface.Invoke)
                       ?? throw new InvalidOperationException($"按钮 {button.Name} 不支持 Invoke");
        // UIA posts an asynchronous click. For modal buttons, handle the click on
        // this dispatcher turn so it cannot arrive after ShowDialog has returned.
        if (Window.GetWindow(button) is CreateGroupWindow or TextPromptWindow)
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else
            provider.Invoke();
    }

    private static void Invoke(MenuItem item)
    {
        var peer = new MenuItemAutomationPeer(item);
        var provider = (IInvokeProvider?)peer.GetPattern(PatternInterface.Invoke)
                       ?? throw new InvalidOperationException($"菜单 {item.Header} 不支持 Invoke");
        provider.Invoke();
    }

    private static void SchedulePrompt(string value)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var prompt = System.Windows.Application.Current.Windows.OfType<TextPromptWindow>().LastOrDefault(window => window.IsVisible)
                         ?? throw new InvalidOperationException("未找到文本输入对话框");
            prompt.Input.Text = value;
            // Complete the modal before another ApplicationIdle continuation can
            // resume the owner test and close the dialog ahead of an async UIA click.
            prompt.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }), DispatcherPriority.ApplicationIdle);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CloseGroupForCollectionCheck(CategoryGroup group, WorkspaceConfig config)
    {
        var window=new DesktopGroupWindow(group,config,()=>Task.CompletedTask,()=>{},_=>Task.CompletedTask);
        window.Show();await Idle();
        var weak=new WeakReference(window);
        window.AllowClose=true;window.Close();
        return weak;
    }

    private static Task ScheduleCreateGroup(string value)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
            await WaitUntil(() => System.Windows.Application.Current.Windows.OfType<MainWindow>().Any(w => w.IsGroupPanelOpen));
            var owner = System.Windows.Application.Current.Windows.OfType<MainWindow>().Last(w => w.IsGroupPanelOpen);
            await WaitUntil(() => owner.GroupEditor.IsPrepared);
                Assert(owner.CreateGroupBackdrop.Background is SolidColorBrush veil && veil.Color.A <= 40, "面板遮罩不应遮住后台");
                Assert(owner.GroupsPage.Visibility == Visibility.Visible && owner.GroupList.Items.Count > 0,
                    "弹窗打开后分组页面及列表应保留");
                SaveVisual(owner, Path.Combine(Environment.CurrentDirectory, "implementation-create-group-overlay.png"));
            owner.GroupEditor.NameInput.Text = value;
            owner.GroupEditor.CreateAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }), DispatcherPriority.ApplicationIdle);
        return completion.Task;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("界面操作未在预期时间内完成");
            await Task.Delay(16);
        }
        await Idle();
    }

    private static Task Idle() => System.Windows.Application.Current.Dispatcher.InvokeAsync(
        () => { }, DispatcherPriority.ApplicationIdle).Task;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void SaveVisual(Window window, string path) => SaveElement(window, path);

    private static void SaveElement(FrameworkElement window, string path)
    {
        // Capture the settled content; motion itself is exercised separately.
        if (window is CreateGroupWindow or TextPromptWindow or OrganizeDesktopWindow &&
            window is Window { Content: FrameworkElement surface })
        {
            surface.BeginAnimation(UIElement.OpacityProperty, null);
            surface.Opacity = 1;
            surface.CacheMode = null;
        }
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    private static void AssertRoundedHwnd(IntPtr hwnd, string failure)
    {
        Assert(GetWindowRect(hwnd, out var bounds), "无法读取桌面分类窗口的物理尺寸");
        var region = CreateRectRgn(0, 0, 1, 1);
        try
        {
            Assert(GetWindowRgn(hwnd, region) != 0 &&
                   !PtInRegion(region, 0, 0) &&
                   !PtInRegion(region, 0, bounds.Height - 1) &&
                   PtInRegion(region, bounds.Width / 2, 0), failure);
        }
        finally { DeleteObject(region); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
}
