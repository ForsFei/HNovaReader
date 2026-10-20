# HNovaReader

HNovaReader 是一款基于 .NET 8 和 WPF 的 Windows 离线 TXT 书库与阅读器，无需账号或云服务。

[下载安装包（Windows x64）](https://github.com/ForsFei/HNovaReader/releases/download/v1.0.0/HNovaReader-1.0.0-win-x64-Setup.exe) · [最新版本](https://github.com/ForsFei/HNovaReader/releases/latest) · [历史版本](https://github.com/ForsFei/HNovaReader/releases)

## 安装与使用

支持 Windows 10/11 x64。安装包已包含 .NET Desktop Runtime，无需单独安装 .NET。

1. 下载安装包并双击运行。Release 页的 Assets 中选择 `HNovaReader-版本号-win-x64-Setup.exe`；`Source code` 是源码归档。
2. 按向导安装，可选择创建桌面快捷方式。默认安装到 `%LocalAppData%\Programs\HNovaReader`，无需管理员权限。
3. 在设置中选择书库根目录，导入 TXT 文件或文件夹后即可阅读。

当前安装向导为英文，程序界面支持中文。安装包尚未代码签名，Windows 可能显示未知发布者提示。

## 功能

- **文件夹书库**：按照真实目录显示任意层级文件夹；支持新建、重命名、删除、Ctrl/Shift 多选及拖放整理，磁盘文件位置同步变化。
- **导入与同步**：默认复制到书库；导入文件夹保留目录结构，单文件默认进入收件箱。可手动更新书库，也可开启定期扫描，识别新增和删除的 TXT 文件。
- **检索与整理**：书架/列表视图、收藏、阅读状态、书籍信息与封面编辑；系列和标签作为独立标记辅助搜索，不决定文件位置。
- **阅读与编辑**：滚动/分页模式、明亮/深色/护眼阅读主题，以及字体、字号、行距和正文宽度设置。目录与笔记按需展开，阅读进度自动保存。
- **目录与标注**：自动识别中英文章节，支持书签、高亮和笔记；阅读器提供 TXT 编辑模式及文件冲突检查。
- **编码与大文件**：支持 UTF-8、UTF-16、GB18030、Big5 等编码，采用分块读取和有界缓存。
- **备份与恢复**：文件迁移、重命名及磁盘删除前备份；缺失文件支持重新定位，保留阅读记录和标注。

## 数据与备份

用户数据默认保存在 `%LocalAppData%\HNovaReader`：

| 路径 | 内容 |
| --- | --- |
| `library.db` | 书籍信息、阅读位置、书签和笔记等 |
| `settings.json` | 应用设置和书库根目录配置 |
| `covers/` | 自定义封面 |
| `cache/` | 派生缓存 |
| `library/` | 默认书库，可在设置中更改 |

书库根目录中的 `.hnova-backups/` 保存文件操作备份和恢复清单，不显示在书库界面，也不参与扫描。旧目录模型升级前会备份数据库。

“从书库移除”只移除数据库记录；“删除磁盘文件”需要确认，并送入 Windows 回收站。书库离线或扫描不完整时，更新操作跳过删除同步。设置损坏时会保留损坏文件并恢复默认值。

卸载程序保留用户数据、自定义书库和备份。备份个人资料时，应同时备份用户数据目录和实际书库目录。

## 源码开发

要求 Windows 和 .NET 8 SDK 或更新版本。WPF 只支持 Windows，当前发布目标为 `win-x64`。

```powershell
dotnet restore HNovaReader.sln
dotnet tool restore
dotnet run --project HNovaReader/HNovaReader.csproj
```

验证代码：

```powershell
dotnet build HNovaReader.sln -c Release
dotnet test HNovaReader.sln -c Release
dotnet format HNovaReader.sln --verify-no-changes
dotnet ef migrations has-pending-model-changes --configuration Release --project HNovaReader/HNovaReader.csproj --startup-project HNovaReader/HNovaReader.csproj --no-build
```

主要技术：CommunityToolkit.Mvvm、依赖注入、EF Core 8 和 SQLite。界面通过 ViewModel 调用服务；TXT 是正文来源，数据库保存元数据和阅读记录。

## 打包与发布

安装 [.NET SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 和 [Inno Setup 6.3+](https://jrsoftware.org/isdl.php)，在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Installer.ps1 -Version 1.0.1
```

脚本支持自动查找 Inno Setup 6/7。非默认安装位置可传入 `-IsccPath "D:\Inno Setup 7\ISCC.exe"`。

安装包输出到 `artifacts/installer/HNovaReader-1.0.1-win-x64-Setup.exe`。脚本在临时目录发布 Windows x64 自包含程序并编译安装包，结束后清理临时发布文件；已有同版本安装包不会自动覆盖。版本号支持三段或四段数字。

发布到 GitHub 时，在 [Releases](https://github.com/ForsFei/HNovaReader/releases) 创建对应标签（如 `v1.0.1`），填写更新说明并上传安装包附件，然后发布。同步更新本 README 的直接下载链接；安装包不提交到源码仓库。

如果只需要依赖本机运行时的发布目录：

```powershell
dotnet publish HNovaReader/HNovaReader.csproj -c Release -r win-x64 --self-contained false -o release
```

这种发布方式要求目标电脑安装 .NET 8 Desktop Runtime，`release/` 仅在手动发布后生成。

## 项目结构

| 路径 | 用途 |
| --- | --- |
| `HNovaReader/` | 应用源码、界面、资源和数据库迁移 |
| `HNovaReader.Tests/` | 回归测试 |
| `installer/HNovaReader.iss` | 安装、快捷方式及卸载配置 |
| `tools/Build-Installer.ps1` | 一键打包脚本 |
| `tools/Generate-AppIcon.ps1` | 图标生成工具 |
| `.config/dotnet-tools.json` | 固定 EF Core 命令行工具版本 |
| `Directory.Build.props` | 应用与测试共享的编译配置 |
| `artifacts/installer/` | 本地安装包产物，不提交 Git |

`bin/`、`obj/`、`.vs/` 和临时测试输出可重新生成。清理构建产物时，请保留实际书库、用户数据和 `.hnova-backups/`。
