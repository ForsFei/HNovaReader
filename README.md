# HNovaReader

HNovaReader 是一个基于 C#、.NET 8 和 WPF 的完全离线 TXT 书库与阅读器。

当前已完成需求定义的 Phase 1–16，主要能力包括：

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
- Steam 式“自定义文件夹 → 系列 → TXT”目录树，支持收件箱/未分系列默认落点
- Ctrl/Shift 多选与拖放整理；书籍可移入其他系列，系列可跨文件夹移动，数据库归属和真实磁盘路径同步变更
- 收件箱、自定义文件夹和系列均可重命名，Windows 实际目录同步改名并清理空旧目录
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
- 阅读器右上角 TXT 编辑模式：有界片段编辑、文件版本冲突保护、流式安全替换及保存后会话刷新
- 书签、高亮、选择笔记与独立笔记，支持原文上下文锚点和文件变更复核提示
- 最近阅读、阅读中、未读、已读、收藏等真实阅读状态分区
- Continue Reading 卡片、按小数序号排序的系列视图，以及多对多标签分组
- 缺失 TXT 文件标记与重新定位；重定位保留书籍身份、元数据、阅读位置和标注
- `settings.json` 版本化、原子保存、损坏文件保留和安全默认值回退
- 窗口尺寸、默认编码、默认导入行为与管理型书库目录持久化
- 非阻塞状态与友好错误反馈；从书库移除和回收站删除保持明确分离
- 显式 `HNOVAREADER_APP_DATA_ROOT` 隔离根目录，便于开发与视觉验收且不改变默认用户路径
- 基于真实文件和临时 SQLite 的 xUnit v3 测试

## 运行

要求 Windows 和能够构建 `net8.0-windows` 的 .NET SDK。

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
dotnet ef migrations has-pending-model-changes --project HNovaReader/HNovaReader.csproj --startup-project HNovaReader/HNovaReader.csproj --no-build
```

生成可分发目录：

```powershell
dotnet publish HNovaReader/HNovaReader.csproj --configuration Release --self-contained false --output artifacts/publish/framework-dependent
```

该发布方式依赖目标机器已安装 .NET 8 Desktop Runtime。应用只读写本机文件，不需要网络、账号或云服务。

应用数据保存在 `%LocalAppData%\HNovaReader`：

```text
library.db
settings.json
covers\
cache\
library\
```

设置文件损坏时会保留为 `settings.json.corrupt-*` 并恢复安全默认值。原地管理的 TXT 不会被应用擅自移动；“从书库移除”只清理数据库记录，“删除磁盘文件”会二次确认并进入 Windows 回收站。

详细技术方案见 [`docs/architecture.md`](docs/architecture.md)，开发阶段记录见 [`task_plan.md`](task_plan.md)。
