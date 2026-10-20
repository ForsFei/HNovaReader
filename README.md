# HNovaReader

HNovaReader 是一个基于 C#、.NET 8 和 WPF 的完全离线 TXT 书库与阅读器。

[下载安装包（Windows x64）](https://github.com/ForsFei/HNovaReader/releases/download/v1.0.0/HNovaReader-1.0.0-win-x64-Setup.exe) · [最新版本与历史版本](https://github.com/ForsFei/HNovaReader/releases/latest)

安装包已包含 .NET 运行时，下载后双击安装即可。在版本页面的 Assets 中选择 `HNovaReader-版本号-win-x64-Setup.exe`；`Source code` 是源码归档。

主要能力包括：

- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- EF Core 8 + SQLite
- ViewModel 驱动的主窗口内导航
- Dark / Light / System 应用主题
- 现代化书库 Shell 与设置页
- 统一的 HNovaReader 应用、任务栏、窗口与界面品牌图标
- Book、Author、Series、Tag 与 BookTag 关系模型
- 可复现的 EF Core 初始迁移与 Windows 路径唯一约束
- 异步 LibraryService 查询、新增、编辑、系列排序和安全移除
- 主窗口 TXT 文件/文件夹拖放入口、状态反馈和运行中取消
- TXT 文件、多路径和后台递归文件夹扫描
- BOM、严格 UTF-8、UTF-16、GB18030、Big5 分层编码检测
- 默认复制到管理型书库、可选择书库存书根目录，也可显式选择原地管理
- 完全按真实文件夹组织的任意层级目录树，支持在任意目录中新建子目录；外部文件夹复制导入保留目录层级，单文件默认放入收件箱
- Ctrl/Shift 多选与拖放整理；书籍可移到任意目录，文件夹可整体移到其他目录，数据库归属和真实磁盘路径同步变更
- 收件箱和自定义文件夹可重命名，Windows 实际目录同步改名；系列重命名只更新元数据，不改变文件位置
- 系列与标签均为独立元数据，可用于搜索/筛选；更改系列不移动文件，移动目录或书籍也不清除系列标记
- 书库工具栏“更新书库”扫描设置中的书库根目录，同步新增和已删除 TXT；设置可开启自动更新（默认关闭，每 10 分钟，范围 1–1440 分钟），开启后启动时也更新一次
- 更新按完整相对目录识别归属，根目录 TXT 原位入库，支持空目录及深层目录，不再推断系列。备份目录不参与扫描；目录离线或扫描不完整时跳过删除同步，书库外的原位置书籍不受影响
- 老版本升级先备份 SQLite，再从真实路径重建目录归属；旧系列目录保留为普通目录，同名系列合并为全局标记，书籍 ID/阅读进度不变
- 迁移、重命名和磁盘删除前创建隐藏 `.hnova-backups` 会话及 JSON 恢复清单
- 导入进度、取消、孤儿副本清理与逐文件错误汇总
- 可切换的虚拟化书架/列表视图与 225ms 元数据实时搜索
- 基于书籍 ID 的动态默认封面、按需缩略图缓存与内部自定义封面副本
- 紧凑的 Windows 风格单列书籍右键菜单、高辨识度收藏星标、文件信息刷新、回收站删除和元数据编辑器
- 精简的书库首页（保留搜索与双视图工具栏）和分组式书籍信息编辑界面
- 可扩展的 IReaderEngine/ITextDocumentSession 边界与 TXT Engine Factory
- 64 KiB 安全分块解码、256K 单窗口硬上限和 1M 字符 LRU 视口缓存
- UTF-16 CharacterOffset、稀疏 byte↔char 检查点与异步随机窗口读取
- 多规则中文章/回/卷/部/篇/集及英文 Chapter 自动目录检测
- 单遍后台章节/检查点扫描、进度/取消和首窗优先返回
- SQLite 章节/索引派生缓存，以及文件大小/修改时间/编码/规则版本失效检测
- 主窗口内沉浸式 ReaderView、可折叠目录/阅读信息侧栏与专注模式
- 有界 96K FlowDocument 滑动视口、前后换窗与源文本 CharacterOffset 映射
- 章节跳转、后台索引状态、阅读进度、字号调节与 Ctrl+Shift+L 目录快捷键
- SQLite ReadingPosition、750ms 滚动 debounce、离开/退出立即保存与可靠恢复
- Scroll / Paged 双阅读模式，以及字体、字号、行距、段距、正文宽度和边距设置
- 独立于应用主题的 Light / Dark / Sepia Reader Theme，并可从阅读器右上角即时切换
- 阅读器默认收起目录和笔记；整页跟随阅读主题，正文居中留白，模式/主题/只读与编辑采用统一分段开关
- 主题化圆角设置下拉框、轻量滚动条和标注标签页
- 阅读器右上角 TXT 编辑模式：有界片段编辑、文件版本冲突保护、流式安全替换及保存后会话刷新
- 书签、高亮、选择笔记与独立笔记，支持原文上下文锚点和文件变更复核提示
- 最近阅读、阅读中、未读、已读、收藏等真实阅读状态分区
- 按小数序号排序的系列视图，以及多对多标签分组
- 缺失 TXT 文件标记与重新定位；重定位保留书籍身份、元数据、阅读位置和标注
- `settings.json` 版本化、原子保存、损坏文件保留和安全默认值回退
- 窗口尺寸、默认编码、默认导入行为与管理型书库目录持久化
- 非阻塞状态与友好错误反馈；从书库移除和回收站删除保持明确分离
- 显式 `HNOVAREADER_APP_DATA_ROOT` 隔离根目录，便于开发与视觉验收且不改变默认用户路径
- 基于真实文件和临时 SQLite 的 xUnit v3 测试

## 运行

项目仅保留一个最新发布目录：`release/`。安装 .NET 8 Desktop Runtime 后，直接运行 `release/HNovaReader.exe`。

源码开发要求 Windows 和 .NET 8 SDK（支持 `net8.0-windows`）。直接使用 Release 不需要 SDK、测试目录或工具脚本。

```powershell
dotnet restore HNovaReader.sln
dotnet tool restore
dotnet run --project HNovaReader/HNovaReader.csproj
```

## 验证

```powershell
dotnet build HNovaReader.sln --configuration Release
dotnet test HNovaReader.sln --configuration Release
dotnet format HNovaReader.sln --verify-no-changes
dotnet ef migrations has-pending-model-changes --configuration Release --project HNovaReader/HNovaReader.csproj --startup-project HNovaReader/HNovaReader.csproj --no-build
```

生成可分发目录：

```powershell
dotnet publish HNovaReader/HNovaReader.csproj --configuration Release --runtime win-x64 --self-contained false --output release
```

该发布方式依赖目标机器已安装 .NET 8 Desktop Runtime。应用只读写本机文件，不需要网络、账号或云服务。

## 生成 Windows 安装包

安装 .NET 8 SDK 或更新版本，以及 [Inno Setup 6.3 或更新版本](https://jrsoftware.org/isdl.php)，然后在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Installer.ps1 -Version 1.0.0
```

如果编译器安装在非默认位置，增加 `-IsccPath "D:\Inno Setup 6\ISCC.exe"`。

输出为 `artifacts/installer/HNovaReader-1.0.0-win-x64-Setup.exe`。脚本在临时目录发布 Windows x64 自包含程序，包含 .NET Desktop Runtime 和 SQLite 依赖，用户无需另装 .NET；不会覆盖现有 `release/`，临时发布文件在结束时清理。版本号必须为三段或四段数字；已有同版本安装包不会被静默覆盖。

安装包默认按当前用户安装到 `%LocalAppData%\Programs\HNovaReader`，不申请管理员权限。提供开始菜单入口、可选桌面快捷方式和卸载入口；固定 AppId 支持后续同一应用升级。安装向导目前为英文，程序自身仍支持中文。卸载仅移除安装文件和快捷方式，不删除 `%LocalAppData%\HNovaReader`、自定义书库或备份。

目前脚本未配置代码签名，分发未签名安装包时 Windows 可能显示安全提示。正式发布前，应在干净的 Windows 环境验证安装、启动、升级和卸载。

### 在 GitHub 发布下载版本

打包后，在仓库的 Releases 页面选择 Draft a new release，为本次发布创建标签（例如 `v1.0.1`），填写版本说明，并将对应的 `HNovaReader-版本号-win-x64-Setup.exe` 拖入附件区域，然后发布。用户即可从 Releases 页下载安装包。版本标签、脚本的 `-Version` 参数和安装包版本应保持一致；安装包作为 Release 附件上传，不提交到源码仓库。发布新版本后同步更新 README 顶部的直接下载链接。

应用数据保存在 `%LocalAppData%\HNovaReader`：

```text
library.db
settings.json
covers\
cache\
library\
```

设置文件损坏时会保留为 `settings.json.corrupt-*` 并恢复安全默认值。原地管理的 TXT 不会被应用擅自移动；“从书库移除”只清理数据库记录，“删除磁盘文件”会二次确认并进入 Windows 回收站。

## 项目结构与维护

- `HNovaReader/`：WPF 应用源码、图标资源和数据库迁移。
- `HNovaReader.Tests/`：回归测试，覆盖真实文件操作、旧库升级、导入同步、阅读器与界面行为；建议开发时保留，不随 Release 分发。
- `tools/Generate-AppIcon.ps1`：重新生成应用图标的工具；图标资源已包含在源码中，正常构建和运行不需要执行它。
- `tools/Build-Installer.ps1` 和 `installer/HNovaReader.iss`：一键发布自包含程序并编译 Inno Setup 安装包。
- `.config/`：仓库级 EF Core 工具版本配置。
- `release/`：唯一最新发布目录，包含运行必需的程序和依赖；该目录不提交 Git，可按上面的发布命令重新生成。

应用采用 MVVM：界面通过 ViewModel 调用服务，SQLite 保存书籍元数据、阅读位置和标注，TXT 文件仍是正文来源。阅读引擎按块解码并使用有界缓存，阅读位置以字符偏移保存。

书库按真实文件夹组织，支持任意层级；系列和标签只是检索标记，不决定磁盘路径。手动或自动更新扫描设置中的书库根目录，备份目录不参与扫描。目录离线或扫描不完整时不会执行删除同步。

文件迁移、重命名及磁盘删除前的备份保存在书库根目录的 `.hnova-backups/`，并附有恢复清单；旧目录模型升级前也会备份数据库。请勿将该目录当作构建缓存删除。

本地规划记录、早期需求和架构参考文件已移除，维护与使用说明统一保留在本 README 中。`bin/`、`obj/`、`artifacts/` 和 IDE 缓存均可重新生成，不属于发布必需文件。
