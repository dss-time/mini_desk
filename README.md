# MiniDesk

**A local-first desktop organizer for Windows 11.** MiniDesk places lightweight, translucent groups on the desktop so shortcuts and files are easier to find. It is built with C#/.NET 10 LTS and WPF; it does not use Electron, WebView, or Chromium and has no network, ads, or telemetry features.

[简体中文](#简体中文) · [English](#english)

## 简体中文

### 获取并启动

在 [GitHub Releases](https://github.com/dss-time/mini_desk/releases/latest) 下载与你的设备架构对应的 `MiniDesk-win-x64.zip` 或 `MiniDesk-win-arm64.zip`，解压后运行 `MiniDesk.exe`。发布包为 .NET 10 自包含版本，不需要单独安装 .NET Runtime。请勿将两个架构的文件混合解压。

如需从源码构建，请在 Windows 10/11 的 x64 或 ARM64 设备上安装 **.NET 10 SDK**，然后在 PowerShell 中运行：

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

### 整理桌面（v1.0.3 第一阶段）

在首页或“分组管理”点击“整理桌面”，先检查逐项预览，可为每个项目选择目标分组或“留在桌面”。点击“确认整理”后才应用；取消不会改变桌面。自动建议只识别常见文件格式和已知快捷方式目标程序，未知项目、文件夹、隐藏项目和已分组项目默认不处理。已有 Word / Excel / PPT / PDF 专用组优先，否则归入办公；只按需创建非空分类，不重排已有分组。

确认前自动保存快照，并将本次整理记录写入本地 JSON。“分组管理 → 撤销上次整理”只恢复本次仍未被手动移动的项目，不回退其他布局变更。恢复失败时保留记录，可重试；新创建的空分组会保留，避免误删后续手动设置。文档真实路径不变，快捷方式沿用 MiniDesk 现有受控收纳及恢复机制。当前阶段不启用后台监听和新项目自动整理，也尚未加入个人偏好记忆。

English: Choose **Organize desktop**, review each destination, then apply. Cancel changes nothing. Unknown files and folders stay on the desktop. Existing dedicated Office categories take priority. **Undo last organization** restores only unchanged items from the latest batch; new empty groups remain. A local snapshot and persistent undo journal protect the operation. Background organization and preference memory are not enabled in this phase.

### 基本使用

v1.0.4 统一了新拟物界面及 Windows 系统分组图标。“新建分组”使用预先创建并复用的窗口内面板，保留后台页面，支持打开与关闭过渡；“创建快照”“整理桌面”和“撤销上次整理”显示执行状态、结果及失败原因。可运行 `MiniDesk.exe --preview` 在独立示例桌面中体验这些功能。

v1.0.6 优化常驻内存和外观页滚动：后台桌面采用低占用绘制，管理窗口或对话框可见时恢复系统默认加速；最小化或关闭交互窗口后回收临时缓存。停用分组不再保留隐藏窗口，上传图标按缩略尺寸解码，图标与阴影缓存均设上限。打开完整管理界面仍会增加内存，后台启动的低占用数字并非全天固定值。

v1.0.3 新增分组右键菜单：在分组内图标上右键，可打开、移动到现有分组、新建分组并移入、移回桌面、打开所在位置或查看 Windows 属性。当前分组标记为已选中；未启用的目标分组不可选择，目标分组没有空余位置时会提示先增大尺寸。移回桌面失败会保留原有分组记录，不会直接删除文件。

在分组标题、空白区域或收起卡片上右键，可展开/收起、选择收起样式、重命名、更换系统或上传图标、锁定位置，以及删除分组。“锁定位置”会禁止拖动和缩放，但不会影响展开、收起或文件操作。删除分组前会提示恢复桌面项目；任一项目恢复失败时不删除分组。

English: Right-click a grouped item to open it, move it to another group (or create one), return it to the desktop, open its original containing folder, or view Windows properties. Right-click a group to change its collapsed style, rename it, change its icon, lock its position, or delete the group safely. Locking prevents panel movement and resizing, not item interaction. Failed restoration keeps the item tracked.

“新建分组 → 系统图标”提供 18 种内置图标，包括财经、代理工具、Excel、PPT、PDF、Word。向下滚动图标列表可选择新增项目；选择后效果预览立即更新，也可切换“上传图标”使用自己的图标。

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

### 更新与语言设置

更新前请从托盘菜单选择“退出”，再运行新版。便携版更换目录后，请更新桌面快捷方式；若已开启开机启动，在新版里关闭后重新开启一次以更新路径。

在“外观设置 → 语言与区域 → 应用语言”中选择简体中文或 English，界面立即切换并保存。“区域格式”独立控制日期和数字格式，不改变界面语言。

最新修复说明见 [v1.0.6](Docs/Releases/v1.0.6.md)。

## English

### Build and run

Download the matching `MiniDesk-win-x64.zip` or `MiniDesk-win-arm64.zip` from [GitHub Releases](https://github.com/dss-time/mini_desk/releases/latest), extract it, and run `MiniDesk.exe`. The release builds are self-contained and do not require a separate .NET Runtime. Do not mix files from different architecture packages.

To build from source, install the **.NET 10 SDK** on Windows 10/11, then run:

```powershell
git clone https://github.com/dss-time/mini_desk.git
cd mini_desk
.\build-release.ps1
```

Run `Release\portable-net10-win-x64\MiniDesk.exe` on x64 or `Release\portable-net10-win-arm64\MiniDesk.exe` on ARM64. These are self-contained builds and do not require a separate .NET Runtime. Use `-Runtime win-x64` or `-Runtime win-arm64` to publish only one architecture.

To create an installer, run `.\build-installer.ps1 -Runtime win-x64` or `-Runtime win-arm64`. The installer can create Start Menu/Desktop shortcuts and configure startup. Uninstall offers to keep or delete MiniDesk configuration; it does not delete users' desktop files.

### Use MiniDesk

Before updating, choose **Exit** from the tray menu. If a portable build moves to a new folder, update your desktop shortcut and toggle startup off and on in the new build to refresh its path. In **Appearance → Language and region**, **App language** changes the UI immediately; **Regional format** independently controls date and number formatting.

- Create or rename groups in **Group Management**. Drag desktop shortcuts, files, or folders into a group, or use the Explorer context-menu command **Put in MiniDesk…** (on Windows 11 it may be under **Show more options**).
- Drag items within or between groups to arrange them. Drag an item back to the desktop to remove it from its group. Double-click opens it with its existing Windows file association.
- Collapse/expand a group from its header. **Appearance** offers capsule, vertical-icon, and icon-only collapsed styles, plus theme, opacity, corner radius, and header-height settings.
- Create, preview, restore, or delete layouts in **Layout Snapshots**. MiniDesk also creates recovery points before selected layout-changing operations.
- Closing the manager leaves MiniDesk in the system tray. Double-click the tray icon to reopen it; right-click and choose **Exit** to quit.

MiniDesk stores its JSON configuration and snapshots under `%LOCALAPPDATA%\MiniDesk\`. It does not move shortcut targets or delete real files. To avoid duplicate desktop icons, a desktop `.lnk` itself may be staged in MiniDesk's local data directory, while ordinary desktop files may be reversibly hidden with their original attributes recorded. Items are restored when removed from groups or during uninstall. Virtual Shell objects such as Recycle Bin are not currently supported.

Run `Release\portable-net10-win-x64\MiniDesk.exe --self-test` for the Windows UI/integration self-test. Results are written to `%LOCALAPPDATA%\MiniDesk\self-test-latest.json`. Architecture and design notes are in [`Docs/`](Docs/).

## License

MIT. See [`LICENSE`](LICENSE).
