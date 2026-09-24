# MiniDesk 第一版架构

## 进程与模块

MiniDesk 是单进程 WPF 应用。`App` 管理配置、托盘和分类窗口生命周期；每个 `CategoryGroup` 对应一个 `DesktopGroupWindow`。管理中心 `MainWindow` 共享同一份内存模型，不需要 IPC、数据库或后台服务。

- `Models/`：可序列化数据模型和属性变更通知
- `Services/ConfigService`：JSON 加载、原子替换保存、默认数据
- `Services/SnapshotService`：独立 JSON 快照、深拷贝、预览元数据、恢复点
- `Services/AppThemeService`：跟随系统/浅色/深色动态资源与图标切换
- `Services/ExplorerMonitorService`：Shell Hook 桌面窗口重建事件与防抖恢复
- `Services/DisplayTopologyService`：显示器枚举、每屏 DPI、HWND 物理坐标/DIP 换算、断连回收与重连恢复
- `Services/LocalizationService`：RESX 资源、当前 Culture 与运行时语言资源切换
- `Services/DesktopHostService`：桌面层定位、窗口挂载、Acrylic 和像素坐标转换
- `Services/ShellIconService`：按需读取 Windows Shell 关联图标并缓存
- `Services/StartupService`：当前用户 `HKCU\...\Run` 开机启动
- `DesktopGroupWindow`：桌面分类、文件拖放、内部坐标、收起状态
- `MainWindow`：分组管理、外观和快捷设置

## 数据结构

`WorkspaceConfig`

- `Version`
- `PanelOpacity`、`CornerRadius`、`HeaderHeight`
- `ShowCollapsedIcon`、`ShowBadge`、`AttachToDesktop`
- `ThemeMode`、`RestoreAfterExplorerRestart`、`Language`
- `Groups[]`

`CategoryGroup`

- `Id`、`Name`、`Glyph`、`Accent`
- `Left`、`Top`、`Width`、`Height`
- `MonitorDeviceName`、`MonitorOffsetX/Y`、`SavedDpiX/Y`
- `State`: `Expanded | Collapsed | Minimized`
- `CollapseStyle`: `HorizontalCapsule | VerticalIcon | IconOnly`
- `IsEnabled`
- `Items[]`

`DesktopItem`

- `Id`、`Name`、`Path`
- `X`、`Y`（分类内容区 DIP 坐标）
- `VisibilityMode`、`OriginalDesktopPath`、`OriginalFileAttributes`（桌面可见性的可逆恢复信息）

配置保存到 `%LOCALAPPDATA%\MiniDesk\workspace.json`。写入先生成 `.tmp`，再通过替换移动提交，避免进程意外中止造成半截 JSON。

## Win32 API 方案

1. `FindWindow("Progman")` 找到桌面程序管理器。
2. 向 Progman 发送一次 `0x052C`，请求 Explorer 准备 `WorkerW`。
3. `EnumWindows` + `FindWindowEx("SHELLDLL_DefView")` 定位承载桌面图标的窗口，再取其后的 `WorkerW`。
4. 分类区域保持 WPF 创建的正常顶层 HWND，仅通过 `WindowInteropHelper.Owner` 将 `Progman` 设为 owner。禁止把透明 WPF 窗口改成 `WS_CHILD`，否则会破坏 WPF 的鼠标、拖放、焦点和弹出菜单路由。
5. `EnumDisplayMonitors`、`GetMonitorInfo` 和 `GetDpiForMonitor` 建立每屏工作区；`SetWindowPos` 使用物理像素移动 HWND，保存时换算回显示器内 DIP。
6. `SetWindowCompositionAttribute(WCA_ACCENT_POLICY)` 请求 Acrylic；失败时 WPF 半透明画刷仍可工作。
7. `SHGetFileInfo` 读取真实文件/快捷方式的关联图标，随后立即 `DestroyIcon`，WPF 位图被冻结并在进程内缓存。

应用清单声明 `PerMonitorV2`，现代 WPF 负责处理 `WM_DPICHANGED` 后的控件、文字和图像重排。分类位置按显示器设备名与显示器内 DIP 保存；系统通过 `DisplaySettingsChanged` 与 `PowerModeChanged(Resume)` 事件驱动恢复，不使用拓扑轮询。显示器临时断开时只把窗口移至当前可见工作区，不覆盖原显示器归属，因此重连后可恢复原布局。

## 桌面图标交互方案

Explorer/桌面拖入使用标准 OLE `CF_HDROP`。MiniDesk 的 `Drop` 事件只读取绝对路径和显示名称，并把 `DragDropEffects` 明确设为 `Link`。桌面 `.lnk` 只移动快捷方式容器到 `%LOCALAPPDATA%\MiniDesk\DesktopItems`，不移动其指向的程序；普通桌面文件保持原路径，只记录原始属性后设置 `Hidden`。从 MiniDesk 拖出时使用私有的 `MiniDesk.InternalItem` 数据格式：目标是另一分组则迁移归属，释放点是桌面则先恢复快捷方式/文件属性再解除归属，Esc 或其他应用取消时不变。删除分组、恢复快照和卸载也都先执行同样的恢复流程。

资源管理器右键入口以当前用户范围注册到 `HKCU\Software\Classes\*`、`Directory` 和 `DesktopBackground`，不需要管理员权限。Shell 只启动 `MiniDesk.exe --add "%1"`；若主进程已运行，第二实例写入当前用户本地请求队列并通过命名事件唤醒主实例。主实例展示分组选择器，用户确认后仍调用与拖放相同的 `DesktopItemService.AddPath`。

内部项目采用 WPF `Canvas`，每个项目的 `Canvas.Left/Top` 绑定到 `DesktopItem.X/Y`。拖动结束时保存坐标；双击时用 `ProcessStartInfo.UseShellExecute=true` 交回 Windows Shell。

## 性能策略

- 没有文件系统循环扫描
- 没有桌面枚举循环
- 没有常驻 DispatcherTimer
- Shell 图标按需加载并缓存
- 所有 JSON 保存通过 450ms 一次性安静期合并，并继续使用临时文件原子替换
- 外观滑块也只触发一次性取消令牌延迟，不是周期性计时器
- Explorer 恢复由 `RegisterShellHookWindow` 消息触发，再以一次性 900ms 安静期等待桌面稳定
- 收起/展开无复杂动画和持续 GPU 特效

## 后续演进接口

- 自动分类可通过用户显式选择的规则和 `FileSystemWatcher` 实现
- 后续可增加用户可视化的显示器布局编辑器，但当前多屏/DPI 恢复链路已经完成
- 若需要更低内存，可把多个分类区域合并为每显示器一个透明宿主窗口
