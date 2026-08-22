# DesktopOrganizer 使用说明

DesktopOrganizer 是一款面向 Windows 10/11 的桌面图标收纳工具。它把常用应用、文件、文件夹、快捷方式和网址集中到一个半透明收纳区中，同时保留一键打开能力。

## 1. 系统要求

- 64 位 Windows 10 22H2 或 Windows 11。
- 建议使用 Windows 11。
- 安装版已经包含运行环境，不需要另外安装 .NET。
- Windows 7 及更早版本不受支持。

## 2. 下载与安装

1. 从 GitHub Releases 下载 `DesktopOrganizer-Setup-1.0.0-x64.exe`。
2. 双击安装程序。
3. 在“安装位置”中输入目录，或点击“浏览...”选择目录。
4. 根据需要选择是否创建桌面快捷方式、安装完成后是否立即启动。
5. 点击“安装”。

默认安装位置为：

```text
%LocalAppData%\Programs\DesktopOrganizer
```

安装程序不要求管理员权限。若选择了当前账户无权写入的位置，安装会停止并显示错误，请改用当前用户有写权限的目录。

当前发布包没有数字签名。Windows SmartScreen 可能显示“Windows 已保护你的电脑”。请确认文件来自本项目 GitHub Releases，并核对同一 Release 中 `SHA256SUMS.txt` 的 SHA-256 值后再运行。不要使用来源不明的转载安装包。

## 3. 首次启动

启动 `DesktopOrganizer` 后，软件会：

1. 创建并显示一个默认 `2 x 2` 收纳区。
2. 在通知区域显示 DesktopOrganizer 图标。
3. 自动打开当前收纳区的设置窗口。

设置完成后点击“保存”。点击“取消”会撤销本次预览修改，但收纳区仍保持运行。

如果此前已经保存过收纳区，软件会恢复图标、尺寸、方向、位置和外观，然后打开设置窗口。

## 4. 添加图标

从文件资源管理器把以下项目拖到收纳区：

- 应用程序或普通文件。
- 文件夹。
- `.lnk` 快捷方式。
- `.url` 网址快捷方式。

普通文件和文件夹只会被引用，不会从原位置移动。

第一次导入桌面上的 `.lnk` 快捷方式时，软件会询问处理方式：

- **是：移动到托管目录**。桌面上的快捷方式移入 DesktopOrganizer 的 `ManagedShortcuts`，避免桌面和收纳区重复显示。
- **否：只保存引用**。原快捷方式继续留在桌面上。
- **取消**。停止本次导入。

托管操作只适用于快捷方式，不会移动快捷方式指向的应用或文件。

## 5. 打开图标

- 单击收纳区中显示的图标即可打开项目。
- 快速双击同一图标只会启动一次，防止重复打开。
- 文件和文件夹使用 Windows 默认打开方式。
- 快捷方式使用其保存的目标、参数和工作目录。

## 6. 展开全部图标

- 左键单击三点图标，全部图标会在屏幕中间展开。
- 展开窗口中的图标同样可以单击打开。
- 按 `Esc`、点击展开窗口的关闭按钮或点击窗口外区域，只会收起展开窗口，不会隐藏或关闭正常收纳区。

## 7. 打开设置

右键单击三点图标可打开当前收纳区设置。每次主动启动软件时也会自动打开设置。

设置窗口提供：

- 收纳区名称。
- `2 x 2`、`1 x 4`、`2 x 4` 三种尺寸。
- `1 x 4`、`2 x 4` 的横向或纵向排列。
- 图标重命名、排序、删除和外露选择。
- 边框粗细。
- 背景透明度。
- 边框、背景、三点入口和悬停颜色。
- 跟随系统主题、自动派生或自定义十六进制颜色。
- 恢复系统主题。

设置会即时预览。点击“保存”后写入配置；点击“取消”会恢复打开设置前的状态。

从设置中删除一个托管快捷方式并保存时，该快捷方式会优先回到原目录；同名文件不会被覆盖，而是生成 `名称 (2).lnk`、`名称 (3).lnk` 等名称。

## 8. 隐藏、关闭、收起与退出

这些操作含义不同：

### 隐藏收纳区

右键收纳区空白处选择“隐藏收纳区”，或使用通知区域菜单。隐藏不会移动图标、删除配置或退出软件。可从通知区域选择“显示收纳区”恢复。

### 收起展开窗口

只关闭居中的全部图标窗口，正常收纳区继续存在。

### 关闭收纳区

点击收纳区右上角关闭按钮、按 `Alt+F4`，或从右键菜单选择“关闭收纳区”。包含图标时会先显示释放预览；确认后删除当前收纳区，但软件继续在通知区域运行。

### 退出软件

从通知区域选择“退出”。软件结束进程并保留收纳区配置和托管图标，下次启动时恢复。

### 彻底退出收纳区

设置窗口左下角的“彻底退出收纳区”用于不再使用当前收纳区：

1. 未保存的设置预览会先撤销。
2. 软件显示释放位置和数量预览。
3. 用户确认后释放图标并删除当前收纳区。
4. 释放成功后 DesktopOrganizer 进程退出。

空收纳区也会要求确认。取消确认不会修改文件或配置。

以后再次主动启动 DesktopOrganizer 时，会创建新的空收纳区并进入设置界面。

## 9. 图标释放规则

关闭或彻底退出包含图标的收纳区时，确认窗口显示：

- 图标总数。
- 释放到桌面的数量。
- 添加到开始菜单的数量。
- 已经存在于桌面、不需要重复创建的数量。
- 开始菜单目标目录。

用户可以调整释放到桌面的数量，但不能超过估算的安全上限。软件根据显示器工作区、图标网格和现有桌面项目估算容量，预留至少两个空位；无法可靠统计时，本次最多向桌面释放八个项目。

释放方式：

- 托管 `.lnk` 优先移动回桌面。
- 普通文件、文件夹和应用只创建 `.lnk`，不移动原文件。
- 网址创建 `.url`。
- 已经存在于桌面的引用不重复创建。
- 桌面容量不足的项目进入：

```text
%AppData%\Microsoft\Windows\Start Menu\Programs\DesktopOrganizer
```

释放过程使用恢复日志和可回滚文件事务。任何关键步骤失败时，收纳区配置会保留，已完成操作会尝试逆序回滚。程序意外中断后，下次启动会读取 `release-journal.json` 恢复。

## 10. 通知区域菜单

右键通知区域中的 DesktopOrganizer 图标：

- **显示收纳区**：显示被隐藏的收纳区。
- **新建收纳区**：当前没有收纳区时创建一个空收纳区。
- **隐藏收纳区**：临时隐藏。
- **退出**：保留配置并结束进程。

双击通知区域图标也可以显示或新建收纳区。

## 11. 卸载

推荐步骤：

1. 在设置中点击“彻底退出收纳区”，确认图标已经释放。
2. 打开 Windows“设置 > 应用 > 已安装的应用”。
3. 找到 DesktopOrganizer 并选择卸载。

也可以从“开始菜单 > DesktopOrganizer > 卸载 DesktopOrganizer”运行卸载程序。

卸载程序会询问是否删除用户数据。选择“否”会保留配置、图标缓存和日志，以便以后重新安装后继续使用。

卸载程序不会自动释放仍在收纳区中的托管图标，因此卸载前应先使用“彻底退出收纳区”。

## 12. 数据位置

用户配置和运行数据位于：

```text
%AppData%\DesktopOrganizer\
```

主要内容：

- `config.json`：收纳区、图标、位置、尺寸、方向和样式。
- `ManagedShortcuts\`：选择由软件托管的桌面快捷方式。
- `IconCache\`：提取后的图标缓存。
- `Logs\app.log`：运行与错误日志。
- `release-journal.json`：仅在释放事务进行中存在的恢复日志。

配置损坏时，原文件会保存为 `config.bad.json`，软件再创建默认配置。

## 13. 故障排查

### 收纳区不见了

检查 Windows 通知区域，右键 DesktopOrganizer 图标并选择“显示收纳区”。

### 图标打不开

确认原文件、文件夹或应用仍存在。查看：

```text
%AppData%\DesktopOrganizer\Logs\app.log
```

### 释放失败

不要手动删除 `%AppData%\DesktopOrganizer\release-journal.json`。重新启动 DesktopOrganizer，让软件自动回滚中断操作；配置会在释放完全成功前保留。

### 安装程序被 SmartScreen 阻止

确认安装程序来自项目 GitHub Releases，并用 PowerShell 校验：

```powershell
Get-FileHash .\DesktopOrganizer-Setup-1.0.0-x64.exe -Algorithm SHA256
```

结果应与 Release 中的 `SHA256SUMS.txt` 一致。

## 14. 项目文件说明

### 根目录

- `README.md`：GitHub 项目首页、下载、开发和发布入口。
- `USER_GUIDE.md`：本文档，面向最终用户和源码阅读者。
- `DesktopOrganizer.sln`：Visual Studio/.NET 解决方案。
- `.gitignore`：忽略构建、中间和发布输出。

### `src/DesktopOrganizer.App`

- `App.xaml`、`App.xaml.cs`：应用启动、窗口生命周期、托盘、设置与释放流程协调。
- `DesktopOrganizer.App.csproj`：WPF 项目、版本、图标和构建配置。
- `app.manifest`：Windows 10/11、Per-Monitor V2 DPI 和长路径声明。
- `Assets/`：应用图标资源。
- `Models/`：配置、图标、布局、主题和释放计划数据结构。
- `Services/`：配置持久化、导入、Shell Link 解析、启动、主题、容量估算、快捷方式创建、释放事务、日志和托盘服务。
- `ViewModels/`：收纳区、图标、设置和释放确认的状态及命令。
- `Views/`：正常收纳区、展开窗口、设置窗口和释放确认窗口的 WPF 界面。
- `Resources/`：收纳区公共主题和控件样式。
- `Native/`：窗口样式相关 Win32 调用。

### `tests/DesktopOrganizer.Tests`

- `Program.cs`：无第三方测试框架的自动化测试入口，覆盖配置、导入、启动、设置、释放、回滚和崩溃恢复。
- `DesktopOrganizer.Tests.csproj`：测试项目配置。

### `scripts`

- `Build-Release.ps1`：构建、测试、发布、打包和生成 SHA-256。
- `Generate-AppIcon.ps1`：可重复生成 PNG/ICO 应用图标。
- `Stage4.UiSmoke.ps1` 至 `Stage7.UiSmoke.ps1`：隔离的 Windows UI 自动化测试。

### `installer`

- `Install.cmd`：IExpress 解包后启动安装向导。
- `Install.ps1`：安装目录选择、文件复制、快捷方式和卸载注册。
- `Uninstall.ps1`：关闭进程、删除安装文件并选择是否保留用户数据。

### `artifacts`

- 开发阶段截图和视觉检查产物。
- `artifacts/release/`：本地发布脚本生成的安装程序、便携版、用户手册和校验值；该目录不提交到 Git。

## 15. 从源码构建

要求安装 .NET 8 SDK，并使用 Windows 10/11：

```powershell
dotnet build .\DesktopOrganizer.sln --configuration Debug
dotnet run --project .\tests\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj --configuration Debug
```

生成正式安装包：

```powershell
.\scripts\Build-Release.ps1
```

输出位于：

```text
artifacts\release\
```

发布脚本使用 Windows 自带 IExpress，不依赖 Inno Setup、NSIS 或 WiX。GitHub 上推送形如 `v1.0.0` 的标签时，工作流也会构建并上传同样的发布文件。
