# MiniDesk

**A local-first desktop organizer for Windows 11.** MiniDesk places lightweight, translucent groups on the desktop so shortcuts and files are easier to find. It is built with C#/.NET 10 LTS and WPF; it does not use Electron, WebView, or Chromium and has no network, ads, or telemetry features.

[简体中文](#简体中文) · [English](#english)

## 简体中文

### 获取并启动

MiniDesk 目前提供源码构建。请在 Windows 10/11 的 x64 或 ARM64 设备上安装 **.NET 10 SDK**，然后在 PowerShell 中运行：

```powershell
git clone https://github.com/dss-time/mini_desk.git
cd mini_desk
.\build-release.ps1
```

构建完成后，直接运行对应架构的自包含程序（不需要另外安装 .NET Runtime）：

- x64：`Release\portable-net10-win-x64\MiniDesk.exe`
- ARM64：`Release\portable-net10-win-arm64\MiniDesk.exe`

也可以只构建一种架构：

```powershell
.\build-release.ps1 -Runtime win-x64
.\build-release.ps1 -Runtime win-arm64
```

### 安装版

安装包会为指定架构先发布应用，再生成独立安装器：

```powershell
.\build-installer.ps1 -Runtime win-x64
# 或
.\build-installer.ps1 -Runtime win-arm64
```

安装包输出到 `Release\MiniDesk-Setup-x64.exe` 或 `Release\MiniDesk-Setup-arm64.exe`。安装器可创建桌面和开始菜单快捷方式，并可选设置开机启动。卸载时会询问保留布局数据还是删除 MiniDesk 配置；无论选择哪项，都不会删除用户的桌面真实文件。

### 基本使用

1. 启动 MiniDesk 后会打开管理窗口，并显示桌面分组。首次使用可在“分组管理”中创建分组，也可以使用音乐、开发工具、通讯、办公、临时文件等默认分组。
2. 将桌面快捷方式、文件或文件夹拖入分组；也可在资源管理器中右键选择“放入 MiniDesk…”。在 Windows 11 的新式右键菜单中，传统 Shell 命令可能位于“显示更多选项”里。
3. 在分组窗口中拖动项目可调整位置；拖到其他分组会改变归属，拖回桌面会移出分组。双击项目仍由 Windows 按原有关联打开。
4. 使用分组标题栏的收起按钮收起或展开。可在“外观设置”选择横向胶囊、竖向图标或仅图标样式，并调整主题、透明度、圆角和标题栏高度。
5. 在“布局快照”中手动创建快照，预览并恢复先前布局，或删除不再需要的快照。重要布局操作也会自动创建恢复点。
6. 关闭管理窗口后，MiniDesk 仍在系统托盘运行。双击托盘图标可重新打开管理窗口；要完全退出，请在托盘图标上右键并选择“退出”。

### 文件安全与数据位置

- MiniDesk 保存分组关系、布局和设置，不会移动快捷方式所指向的应用程序，也不会因整理而删除用户文件。
- 为了让桌面上的项目在分组内管理时不重复显示，桌面快捷方式可能会将 **`.lnk` 快捷方式本身**暂存到 MiniDesk 的本地数据目录；桌面普通文件则通过记录原始属性并可逆隐藏来管理。移出分组或卸载时会尝试恢复桌面项目。
- 设置文件：`%LOCALAPPDATA%\MiniDesk\workspace.json`
- 布局快照：`%LOCALAPPDATA%\MiniDesk\Snapshots\`
- 暂存的桌面快捷方式及自定义分组图标也保存在 `%LOCALAPPDATA%\MiniDesk\` 下。

删除分组、恢复布局或卸载前，建议先确认桌面项目可正常访问。MiniDesk 只支持有普通文件系统路径的桌面快捷方式、文件和文件夹；回收站、此电脑等虚拟 Shell 对象不在当前支持范围内。

### 开发、测试与文档

在 Windows 上安装 .NET 10 SDK（仓库通过 `global.json` 指定 SDK 版本及兼容的自动滚动策略）后，可运行 UI 自测：

```powershell
.\Release\portable-net10-win-x64\MiniDesk.exe --self-test
```

自测结果默认写入 `%LOCALAPPDATA%\MiniDesk\self-test-latest.json`。架构、数据模型及视觉规范见 [`Docs/`](Docs/)。

### 当前说明

- 桌面图标仍由 Windows Explorer 管理；MiniDesk 提供的是基于路径和可逆桌面可见性管理的分类界面。
- MiniDesk 不扫描用户文件内容，不包含自动分类规则引擎。
- Explorer 恢复、多显示器与 Per-Monitor DPI 行为依赖 Windows Shell 和显示器驱动；欢迎提交问题时附上 Windows 版本、缩放比例和复现步骤。

## English

### Build and run

Install the **.NET 10 SDK** on Windows 10/11, then run:

```powershell
git clone https://github.com/dss-time/mini_desk.git
cd mini_desk
.\build-release.ps1
```

Run `Release\portable-net10-win-x64\MiniDesk.exe` on x64 or `Release\portable-net10-win-arm64\MiniDesk.exe` on ARM64. These are self-contained builds and do not require a separate .NET Runtime. Use `-Runtime win-x64` or `-Runtime win-arm64` to publish only one architecture.

To create an installer, run `.\build-installer.ps1 -Runtime win-x64` or `-Runtime win-arm64`. The installer can create Start Menu/Desktop shortcuts and configure startup. Uninstall offers to keep or delete MiniDesk configuration; it does not delete users' desktop files.

### Use MiniDesk

- Create or rename groups in **Group Management**. Drag desktop shortcuts, files, or folders into a group, or use the Explorer context-menu command **Put in MiniDesk…** (on Windows 11 it may be under **Show more options**).
- Drag items within or between groups to arrange them. Drag an item back to the desktop to remove it from its group. Double-click opens it with its existing Windows file association.
- Collapse/expand a group from its header. **Appearance** offers capsule, vertical-icon, and icon-only collapsed styles, plus theme, opacity, corner radius, and header-height settings.
- Create, preview, restore, or delete layouts in **Layout Snapshots**. MiniDesk also creates recovery points before selected layout-changing operations.
- Closing the manager leaves MiniDesk in the system tray. Double-click the tray icon to reopen it; right-click and choose **Exit** to quit.

MiniDesk stores its JSON configuration and snapshots under `%LOCALAPPDATA%\MiniDesk\`. It does not move shortcut targets or delete real files. To avoid duplicate desktop icons, a desktop `.lnk` itself may be staged in MiniDesk's local data directory, while ordinary desktop files may be reversibly hidden with their original attributes recorded. Items are restored when removed from groups or during uninstall. Virtual Shell objects such as Recycle Bin are not currently supported.

Run `Release\portable-net10-win-x64\MiniDesk.exe --self-test` for the Windows UI/integration self-test. Results are written to `%LOCALAPPDATA%\MiniDesk\self-test-latest.json`. Architecture and design notes are in [`Docs/`](Docs/).

## License

MIT. See [`LICENSE`](LICENSE).
