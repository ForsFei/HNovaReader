# HNovaReader 调研与决策记录

## 2026-08-31 — Phase 8 pagination constraints

- Scroll 与 Paged 必须是可切换且解耦的阅读布局；分页首版只做无动画上一页/下一页。
- 键盘必须支持 `←`、`→`、`PageUp`、`PageDown`。
- Paged 仅对当前锚点附近的有界文本窗口分页，不做整本预分页；每个页边界映射为全书绝对 `CharacterOffset`。
- `LayoutSignature` 至少覆盖内容版本/编码、viewport 宽高/DPI、字体度量、行/段距、正文宽度与边距。
- Resize 或排版参数变化时保留当前页首个可见字符锚点，失效旧分页并从锚点重算；页码只属于派生 UI 状态，不入库。
- 现有 Reader 已把文件窗口读取/位置保存放在 ViewModel，把滚动布局与 `FlowDocument` 字符映射放在 View；Phase 8 延续该边界，以独立分页状态模型和分页 surface 行为接入，不把页码写入 ViewModel 持久化模型。
- WPF 分页应复用同一份源字符到 `TextPointer` 的映射，并用 `DynamicDocumentPaginator` 页位置反查页首绝对 offset；分页只对 96 KiB 当前窗口工作，跨窗口时仍由 ReaderViewModel 读取相邻窗口。
- ReaderView 顶部目前把“连续滚动 · Scroll”写死，正文区只有 RichTextBox；Phase 8 需要加入模式切换、独立分页承载区和上一/下一页控件，同时保留错误/警告/索引进度覆盖层。
- ReaderViewModel 的窗口读取已串行化且可保留 `RestoreCharacterOffset`；增加按精确锚点重载及向前补窗的公开入口即可服务分页跨窗，无需让 View 接触文件会话。
- 单元测试项目已覆盖 ReaderViewModel 的恢复、滑窗、章节跳转与文件版本变化；Phase 8 应在此基础上补模式切换/精确分页窗口测试，并为纯 `LayoutSignature`/页边界状态单独测试。
- 本机 .NET 8 WindowsDesktop 参考文档确认 `DynamicDocumentPaginator` 公开提供 `GetPageNumber(ContentPosition)` 与 `GetPagePosition(DocumentPage)`，可用公开 API 完成 TextPointer↔页号/页首映射。
- 产品与测试项目都面向 `net8.0-windows`，分页状态模型可直接从测试项目验证；无需新增 NuGet 包或 DI 服务。
- `FlowDocumentPageViewer` 原生支持文本选择，并继承 `GoToPage`、`NextPage`、`PreviousPage`、`PageCount` 与相邻页可用状态；相较裸 `DocumentPageView` 更利于 Phase 9 标注选择，因此采用它作为 Paged surface。
- 项目通用 Button 样式位于 `Resources/Styles/Controls.xaml`，已有 Icon/Secondary 样式足以实现模式切换与上下页控件，无需新造全局控件体系。
- Phase 8 最终实现采用“当前页首锚点后向懒分页 + 跨窗口补载”：正常重排从当前页首切片，避免 Resize/字号变化后跳回前文；向前翻过窗口边界时临时载入前导文本并定位上一页。
- `DynamicDocumentPaginator` 的真实 STA 集成测试确认页首 `ContentPosition` 为可映射的 `TextPointer`；因此分页页号始终可以转换为绝对 CharacterOffset，数据库仍无需页码字段。
- 原生可见 QA 主窗口成功创建，但窗口被用户最小化且两次检测到用户输入，按 computer-use 约束停止交互；最终以 STA 分页集成测试覆盖核心 WPF 分页 API，未声称完成被中断的人工翻页视觉检查。

## Requirements
- Windows 桌面端；C#、.NET 8、WPF、MVVM、DI、SQLite；完全离线。
- 第一阶段只支持 TXT，但 Reader Engine 架构需可扩展。
- 支持原地管理与复制到管理型书库，绝不擅自移动原文件。
- 提供书架/列表、元数据编辑、标签/系列/收藏、继续阅读、主题和安全删除。
- Reader 支持目录、Scroll/Paged、书签、高亮、笔记和字符偏移恢复。
- 大型 TXT（最高数百 MB）不得整体读入或阻塞 UI。
- 编码覆盖 BOM、UTF-8、UTF-16 LE/BE、GBK/GB2312/GB18030、Big5，并允许手动覆盖。
- 文件夹扫描、拖拽和解析均需异步、支持取消，不得使用 `.Result`、`.Wait()` 或 `Thread.Sleep`。
- 必须检测源 TXT 的大小/修改时间变化，并提示已有锚点可能漂移。
- 至少测试编码、章节、默认标题、系列排序、阅读进度、重复路径和阅读位置序列化。

## Research Findings
- 当前工作区只有 `开发需求.md`，尚无项目或 Git 元数据。
- 需求明确将技术分析列为 Phase 1，并要求当前不要一次生成大量代码文件。
- 产品数据虽然规模小，但 Book/Tag 多对多、Series、书签、高亮、笔记和阅读位置天然具有关系和查询需求。
- 视觉方向为克制的深色科技风；颜色需集中在 ResourceDictionary/DynamicResource。
- SeriesIndex 必须支持 `2.5` 等值，因此采用 `decimal?`，不能沿用最初示例中的 `int?`。
- Reader 为主窗口内导航的三栏布局，目录与标注侧栏可独立折叠，并提供专注阅读模式。
- Scroll/Paged 必须是同一内容源和锚点模型上的两种布局策略；任何布局参数变化均使分页缓存失效，但不能改变用户的语义阅读位置。
- Bookmark 保存单点位置；Highlight 保存区间；Note 可关联 Highlight 或单点位置。源文件变化时第一版只检测并警告，不承诺自动修复。
- 搜索范围仅限书名、作者、系列、标签，输入使用约 150–300ms debounce；不做全文搜索。
- EF Core SQLite 能胜任该模型；但 SQLite 原生不支持 `decimal` 的比较/排序语义，SeriesIndex 若在数据库排序应映射为 `REAL/double`，或在领域层保留 decimal 并使用 value converter。
- WPF 内置 `VirtualizingStackPanel` 只支持单轴虚拟化；内置 `WrapPanel` 不虚拟化。真正可扩展的封面网格需要虚拟化 WrapPanel/自定义 VirtualizingPanel，或先用按行虚拟化的固定列网格模型。
- WPF `FlowDocument` 自带回流、分页和文本模型，适合普通文档；但把数百 MB TXT 全部实例化为 Paragraph/Run 仍会形成巨大的对象树，因此不能作为整本书的内存模型。
- `RandomAccess.ReadAsync` 支持从指定字节偏移异步读取，适合基于块索引的视口窗口加载；MemoryMappedFile 支持大文件随机视图，但第一版没有足够收益，且多字节编码边界处理仍不可避免。
- .NET 需要注册 `CodePagesEncodingProvider.Instance` 才能完整使用 GB18030/Big5 等传统代码页。
- UTF.Unknown 是基于 Mozilla Universal Charset Detector/uchardet 的轻量检测器，覆盖 UTF-8、GB18030、Big5，可作为 BOM/严格 UTF-8 判定后的低置信度启发式候选，而不能替代用户手动纠正。
- SQLite 同时只允许一个待写事务；批量导入应短事务分批提交，避免长时间持有写锁。
- Phase 2 环境只安装 .NET SDK 10.0.302，但已安装 .NET 8.0.29 Windows Desktop Runtime；SDK 10 可以显式构建 `net8.0-windows`，需要用 `global.json` 固定允许的 SDK 行为并在项目中锁定目标框架。
- 当前 WPF 项目模板可用；工作区没有 `AGENTS.md` 或既有代码需要兼容。
- NuGet 查询确认 CommunityToolkit.Mvvm 8.4.2 和 Microsoft.Extensions.Hosting 8.0.1 可用；EF Core SQLite 需要进一步从 NuGet 版本索引精确选择最新 8.x 补丁。
- NuGet 版本索引确认 EF Core SQLite/Design 最新 8.x 为 8.0.30；Phase 2 固定 CommunityToolkit.Mvvm 8.4.2、DependencyInjection 8.0.1、EF Core 8.0.30。
- Solution、`net8.0-windows` WPF 项目和同目标框架的 xUnit 项目已经创建并建立项目引用。
- WPF 模板的 App 文件无法被基于行上下文的补丁稳定匹配；后续只对这几个模板文件采用删除后重建，避免继续重复失败。
- 模板文件带 UTF-8 BOM 与 CRLF；删除后通过 `apply_patch` 重建已解决匹配问题。
- Phase 2 当前源文件检查未发现 `.Result`、`.Wait()` 或 `Thread.Sleep()`；颜色值集中在 Dark/Light 主题字典中，业务 View 使用 DynamicResource。
- Release 构建已达到 0 警告、0 错误；路径、导航、SQLite 初始化 3 个测试全部通过。
- WPF 进程已稳定运行超过 10 秒，下一步通过 Windows 捕获做只读布局核验。
- 首屏视觉核验通过：系统浅色主题生效，Sidebar、顶部栏、继续阅读卡片、书架空状态在 1440×900 窗口中无重叠或截断；颜色和信息层级符合架构方向。
- 首次自动化点击“设置”时前台窗口抢占导致结果不确定；已按 computer-use 恢复流程重新定位唯一 HNovaReader 窗口，应用仍正常运行且保持书库页。
- 第二次设置页导航已通过可访问性树确认：返回按钮启用，页面显示应用主题和独立 Reader Theme 预留区。
- 深色按钮截图再次被其他前台窗口抢占，因此停止继续 UI 输入；主题切换命令改用确定性的 ViewModel 单元测试验证，初始 System→Light 资源切换已由首屏实际渲染证明。
- 应用项目 NuGet 漏洞检查为 0；测试项目旧模板传递引入 `System.Net.Http 4.3.0` 与 `System.Text.RegularExpressions 4.3.0` 两个 High 漏洞，需要升级测试工具链后复查。
- `dotnet nuget why` 确认两个旧传递包均来自 xunit 2.5.3 → NETStandard.Library 1.6.1，而不是应用依赖。
- 当前稳定测试工具版本包括 xunit.v3 3.2.2（4.0 为新主版本）、VS runner 3.1.5、Microsoft.NET.Test.Sdk 18.9.0、coverlet 10.0.1；将按 xUnit 官方配置选择兼容组合。
- xUnit 官方迁移指南要求 v2 的 `xunit` 改为 `xunit.v3`、测试项目改为 Exe，并使用 3.x VS runner；Phase 2 采用 xunit.v3 3.2.2 + runner 3.1.5 + Test SDK 18.9.0，移除暂不需要的 coverlet。
- 升级后两个项目的传递依赖漏洞扫描均为 0；格式验证无差异，最终 Release 构建 0 warning / 0 error，测试 4/4 通过。
- 真实启动已创建 `%LocalAppData%\HNovaReader\library.db`、`covers` 和 `cache`；冒烟进程已主动结束，没有遗留运行中的 HNovaReader 进程。
- Phase 3 会复用既有 `IDbContextFactory` 与迁移初始化器；服务层直接使用短生命周期 DbContext，不新增通用 Repository/UoW。
- 路径去重将保存“可显示的绝对路径”和“大小写归一化的唯一键”两列，以符合 Windows 文件路径不区分大小写的语义。
- 当前 `HNovaReaderDbContext` 只有空模型，仓库内没有 migrations；Phase 3 可以安全生成第一份初始迁移，既有空 `library.db` 会由 `MigrateAsync` 正常升级。
- `LibraryViewModel` 仍是 Phase 2 空状态页面；Phase 3 只注册领域服务，不让 ViewModel 直接接触 DbContext，也不提前改书库 UI。
- 实体和 LibraryService 首轮实现可在现有依赖下直接编译；`IDbContextFactory` 保证每个用例只持有短生命周期上下文。
- 本机没有全局 `dotnet-ef`；迁移工具需随仓库固定版本，避免项目构建依赖外部全局安装。
- 初始迁移包含 Authors、Series、Tags、Books、BookTags，路径/名称唯一索引和 BookTag 组合主键均与设计一致。
- xUnit v3 分析器在警告即错误模式下强制可取消 API 使用 `TestContext.Current.CancellationToken`；Phase 3 测试应完整传播该令牌。
- `BookDetails` 的 `IReadOnlyList<string>` 运行时为数组，record 自动相等比较不会逐元素比较；测试必须对 Tags 使用序列断言，不能用整个 record 的默认相等性判断数据库往返。
- Phase 3 首轮完整测试为 10/10：覆盖迁移建表、默认标题、关系创建、大小写路径去重、元数据/标签更新、2.5 系列排序、安全移除和输入校验。
- `dotnet-ef migrations has-pending-model-changes` 验证模型快照与代码一致，Phase 3 没有未生成的 schema 变更。
- 干净 Release 构建继续保持 0 warning / 0 error。
- Phase 3 新增 EF 工具清单和数据层代码后，两个项目的 NuGet 漏洞扫描仍为 0。
- 生产路径冒烟未触发启动错误，应用进程稳定；结束后没有遗留 HNovaReader 进程，EF 能从 `%LocalAppData%\HNovaReader\library.db` 读取初始迁移状态。
- 显式 `database update` 返回 “database is already up to date”，确认真实启动已成功将生产数据库推进到 Phase 3 schema。
- SQLite 的 DateTime 文本往返不保证保留 `Kind`；LibraryService 映射 DTO 时必须恢复 UTC 标记，避免调用方把 UTC 值误当成本地时间。
- UTC Kind 回归断言覆盖真实 SQLite 重载路径，修正后完整门禁继续通过。
- Phase 4 将复用 `ILibraryService.ContainsFilePathAsync` 与 `AddBookAsync` 作为持久化边界；扫描和编码检测不得直接访问 DbContext。
- 拖拽入口本质上只是把路径集合交给 ImportService；窗口事件接线属于 UI，但 Phase 4 需要提供可复用的多路径入口，使 Phase 5/主窗口无需复制导入逻辑。
- 文件夹扫描必须递归只收集 `.txt`、后台执行、报告扫描数量并可取消；不能在 UI 线程上同步递归大量目录。
- 重复判断第一版至少使用规范化完整路径，不能按书名，也不应在批量扫描时为所有文件预算 SHA256。
- 管理型模式必须复制而非移动源文件；目标文件名需要避免冲突，并且数据库记录只能在复制成功后创建。
- 编码检测优先级是 BOM → 严格 UTF-8 → 有限样本启发式；需覆盖 UTF-8/UTF-16 LE/BE/GB18030/Big5 并保留后续手动覆盖能力。
- Web 连接器本次未能返回 UTF.Unknown 页面内容；版本和 API 必须从 NuGet 官方 V3 数据与恢复后的程序集/XML 文档验证，不能凭记忆硬编码。
- NuGet 官方 feed 当前稳定版 UTF.Unknown 为 2.7.0，官方 README 明确支持 .NET 8 / .NET Standard 2.0。
- UTF.Unknown 2.7 API 可用 `CharsetDetector.DetectFromBytes` 或异步流方法，结果包含 `Detected.EncodingName`、`Encoding`、`Confidence` 和候选明细；支持无 BOM 的 GB18030 与 Big5。
- Phase 4 不让第三方检测器接管确定性路径：BOM 和严格 UTF-8 由本项目先判定，UTF.Unknown 只处理剩余样本并把置信度暴露给上层。
- 管理型复制后 `Book.FilePath` 指向内部副本；若不保存源路径，同一源文件再次导入会被误认为新书。Phase 4 需增加 `SourceFilePath`/`NormalizedSourceFilePath`，原地模式两者等同，管理型模式分别指向源与副本。
- `NormalizedSourceFilePath` 应有唯一索引并作为导入去重键；`NormalizedFilePath` 继续保证实际可读文件位置唯一。这避免读取整文件或计算 SHA256，也符合“至少规范化完整路径”的要求。
- UTF.Unknown 2.7.0 包含专用 net8.0 程序集，在 net8 目标组中没有额外依赖；许可证元数据为 MPL-1.1。
- 64 KiB 头/中/尾采样将单次检测读取限制在约 192 KiB；BOM 和严格 UTF-8 路径不依赖统计检测结果。
- 扫描器在后台线程按目录迭代、跳过 reparse point、逐项检查取消并汇总不可访问/无效路径，不会在 UI 线程递归遍历。
- `AddBookSourcePath` 自动迁移会把既有行初始化为空字符串；必须在创建唯一索引前用 FilePath/NormalizedFilePath 回填，否则含两本以上旧书的数据库会因重复空键升级失败。
- 管理型复制使用 `FileMode.CreateNew` 原子选择目标文件名，冲突时追加数字后缀；复制或保存失败时只清理由本次导入创建的副本，绝不删除源文件。
- ImportService 顺序处理已扫描文件，避免 SQLite 并发写入；每个文件独立汇总 Imported/SkippedDuplicate/Failed，取消则停止批次并清理当前未入库副本。
- UTF.Unknown 对当前长文本样本能稳定区分 GB18030 与 Big5；BOM、无 BOM UTF-8、批量手动覆盖和统计置信度路径均已由实际文件字节测试覆盖。
- 管理型复制冲突后缀、源文件保留、重复源跳过和“复制完成但入库前取消”的孤儿副本清理均已通过真实文件/SQLite 测试。
- 严格 UTF-8 校验只有随机落在多字节字符中间的中/尾样本可跳过开头续字节；头样本从 byte 0 开始，若首字节是孤立续字节必须判为无效，否则可能把传统编码误判为 UTF-8。
- 默认封面在数据层保持 `CoverPath = null`，Phase 5 由 BookCard 基于标题生成确定性占位封面；Phase 4 不为每本书预生成无文字价值的位图文件。
- Phase 4 引入 UTF.Unknown 2.7.0 后两个项目的 NuGet 漏洞扫描仍为 0。
- 架构已允许 MainWindow 承担窗口级拖拽入口；实际导入仍应由 ShellViewModel 调用 `ITextImportService`，code-behind 只负责把 WPF `FileDrop` 路径转交给 ViewModel。
- Phase 4 最小 UI 反馈应包含 `IsImporting`、状态文本和取消命令；书架卡片与完整导入对话框仍留在 Phase 5。
- Phase 4 最终对照需求的四项交付均已闭合；Phase 5 可以直接复用 `ShellViewModel.ImportPathsAsync`，无需在书库视图重复实现导入流程。
- Phase 5 的精确需求分散在元数据编辑、封面、书架、右键菜单、搜索和性能章节；实现前需要按这些章节逐段建立验收清单。
- Phase 5 验收项：Grid 显示封面/书名/作者且 Hover 只露出继续阅读、收藏、更多；List 显示封面、书名、作者、系列、标签、文件大小、最后打开时间。
- 元数据编辑必须覆盖书名、作者、简介、系列、系列序号、标签、封面；右键菜单还需收藏、打开文件位置、重新读取文件信息、从书库移除与显式磁盘删除入口。
- 第一版搜索仅匹配书名、作者、系列、标签，采用约 150–300ms debounce，不实现 TXT 全文搜索。
- 书库需面向 1000–5000 本保持基本可用；Grid 不能使用普通 WrapPanel 一次创建全部 BookCard，封面必须缩略图化或动态轻量呈现。
- 当前 `LibraryViewModel` 和 `LibraryView` 仍是静态空状态；`ILibraryService` 已有全量异步查询、更新和移除边界，可在 ViewModel 层过滤/分组，不能让 UI 直接访问 DbContext。
- `UpdateBookRequest` 已覆盖 Phase 5 的全部可编辑元数据；`IAppPaths` 已提供 covers/cache 目录，因此元数据无需新增迁移。
- Grid 采用“外层虚拟化行 + 每行固定少量卡片”的方案：外层 `VirtualizingStackPanel` 只创建可见行，每行 `UniformGrid` 最多创建固定数量卡片，避免普通 WrapPanel 对 5000 本全部实例化。
- 自定义封面选择后复制到应用 Covers 目录，数据库只保存内部副本路径；UI 按需以 DecodePixelWidth 加载并使用弱引用缓存，避免原图尺寸和 5000 张强缓存造成内存压力。
- LibraryViewModel 作为单例在应用启动完成数据库迁移后异步加载；导入完成、编辑、收藏、刷新和移除后统一刷新内存快照与虚拟化行。
- 现有 Dark/Light 主题均集中提供 surface/text/accent/danger/focus 色刷；新增书库控件应继续通过 DynamicResource 消费，并只为默认封面补充成对的主题 Accent 色刷。
- 当前只有 NavigationServiceTests 直接 `new LibraryViewModel()`；引入依赖后需给该测试注入轻量替身，避免隐藏服务定位器或无参构造旁路。
- 仓库尚无文件选择、确认提示、编辑对话框或 Explorer 打开抽象；Phase 5 要新增可替换接口，使 ViewModel 测试不启动原生窗口或外部进程。
- Phase 5 静态审计确认 Grid/List 外层都启用了 Recycling virtualization；Grid 每行仅 4 个卡片，且没有 WrapPanel。自定义封面按 240px 解码并以弱引用缓存。
- 同步阻塞搜索的唯一 `.Result` 命中是编辑窗口自己的 `BookEditorWindow.Result` 属性访问，不是 Task.Result；生产代码仍未引入同步等待。
- 1440×900 真实 WPF 首屏显示侧栏、标题/搜索/导入工具栏、动态默认封面卡片与状态栏布局完整；标题过长按预期省略，卡片比例和留白自然。
- 子进程覆盖 `LOCALAPPDATA` 后仍加载到既有 1 本书，表明 Windows `Environment.SpecialFolder.LocalApplicationData` 未被该覆盖可靠隔离；本轮视觉 QA 禁止执行导入、编辑、收藏或删除，只做瞬时视图/搜索/菜单检查。
- 真实 List 视图展示了封面、书名、作者、系列、标签、文件大小、最后打开时间七列，列头与数据行无重叠；Grid/List 状态提示和选中样式正常。
- 输入无匹配关键字后约 225ms 正确显示 `找到 0 / 1 本书` 和专用空结果文案，证明 UI 绑定层的实时搜索路径工作。
- Phase 6 原文边界只有 `TxtReaderEngine`、`ChapterDetector` 与 Large TXT Loading；Scroll 阅读界面、位置保存/恢复和章节跳转从 Phase 7 开始，Paged 从 Phase 8 开始。
- 大文件硬约束是打开数百 MB TXT 时不得 UI 卡死、整本常驻内存、OOM 或长时间白屏；应以异步流式/分块读取、字符偏移索引和有界缓存完成基础 Reader。
- 章节检测第一版允许版本化正则规则，至少覆盖中文数字/阿拉伯数字的章、卷、回及英文 `Chapter N`，并需要独立自动化测试。
- 文件版本第一版由 FileSize 与 LastWriteTimeUtc 组成；打开时必须检测变化并使派生索引失效，复杂 diff/anchor recovery 明确不在当前范围。
- Reader Session 首次打开应先提供锚点附近的有界文本窗口，再在后台完成全量索引；取消索引不应关闭仍可读取的会话。
- `CharacterOffset` 的精确定义是去除 BOM 后、保留源换行符的 .NET UTF-16 code-unit 索引；byte offset 只用于同一文件版本和编码下的定位加速。
- 章节、字符总数与安全 byte↔char 检查点应共享一次后台顺序扫描；缓存键至少包含 FileVersion、Encoding 与章节规则版本。
- 需求明确要求多个可扩展 `ChapterPattern`，而非一个万能 Regex；检测器应先约束独立行和标题长度，再由规则决定章节层级。
- 现有 Book 使用 Guid 主键并已保存 FilePath/FileSize/LastWriteTimeUtc/FileEncoding；Reader 打开请求可直接携带 BookId 与这些事实源，索引持久化适合按 Book 一对一建模并随删除级联。
- 架构基线明确把章节/稀疏索引放入 SQLite；Phase 6 将实现 BookTextIndex + TextCheckpoint + Chapter 三张派生缓存表，而不是另建与数据库状态分离的 JSON 索引。
- `AddTextReaderIndex` 迁移保持索引为派生缓存：每本书最多一份完整索引，章节与检查点使用稳定复合键并随 Book 级联删除，不增加对源 TXT 的所有权。
- 24MB 合成大文件在测试中只返回 4096 字符首窗，完整扫描后生成 300+ 稀疏检查点；随机尾窗仍受 256K 硬上限约束，证明基本读取路径不需要整本字符串。
- 后台索引取消与会话生命周期已解耦：取消扫描后同一会话仍可分块读正文；索引期间文件版本变化会抛出专用异常并拒绝写入缓存。
- Phase 7 原文要求主窗口内导航到 ReaderView，提供连续滚动正文、目录侧栏、可收起侧栏/专注模式，并可靠保存/恢复 CharacterOffset 与章节跳转；Phase 8 分页重算不在当前范围。
- Scroll 表现层采用有界滑动 FlowDocument/RichTextBox：只承载锚点附近正文，接近窗口边缘异步换窗；VerticalOffset 仅用于当前画面稳定，绝不持久化。
- 阅读位置必须以首个稳定可见字符的 UTF-16 CharacterOffset 为权威锚点，采用滚动 debounce + 离开/切书立即保存；总字符数已知后用 CharacterOffset/TotalCharacterCount 计算进度。
- Reader 三栏要求左目录、中正文、右侧标注预留；Phase 7 可实现目录与正文并保留右栏结构，但 Bookmark/Highlight/Note 的数据与交互属于 Phase 9。
- 现有 `MainWindow` 固定 244px 应用侧栏与 58px 顶栏；Reader 要获得三栏沉浸布局，应让 Shell 在 Reader 路由隐藏外层侧栏/顶栏，由 Reader 自带返回与阅读工具栏。
- `LibraryViewModel.OpenBookAsync` 仍是占位提示且没有导航依赖；为避免 DI 环，书库可发布异步打开请求，由 `ShellViewModel` 协调 Reader 打开与路由切换。
- `ITextDocumentSession` 已提供初始有界窗口、任意 CharacterOffset 读取、后台索引和文件版本，足以支撑 Phase 7；Reader 表现层只维护窗口起点与可见字符锚点，无需读取整书。
- Phase 7 静态审计仅发现一个有界 `new FlowDocument`，未出现 ReadAllText/ReadToEnd、Task.Result/Wait、Sleep、MemoryMappedFile 或持久化 VerticalOffset。
- 锚点边界需采用半开区间选择文本段，避免刚好落在换行结束/下一 Run 开始处时恢复到上一行；导航返回也应在离开生命周期成功后再弹出返回栈。
- 默认 AppPaths 直接使用 Windows LocalApplicationData，普通 `LOCALAPPDATA` 子进程覆盖无法可靠隔离 SpecialFolder；增加仅在显式设置时生效的 `HNOVAREADER_APP_DATA_ROOT` 覆盖可让开发/QA 使用独立数据库，正常用户路径保持不变。

## Technical Decisions
| Decision | Rationale |
|----------|-----------|
| Phase 1 先输出架构分析，不创建实现代码 | 遵守需求文档的显式开发顺序与范围控制 |
| 以 CharacterOffset 作为跨布局的阅读位置主锚点 | 字体、窗口、行距和阅读模式变化不会破坏语义位置 |
| 数据库和设置分离 | 关系型用户内容进入 SQLite；少量界面偏好进入 settings.json |
| 自定义封面复制到应用 `covers` 目录 | 避免用户移动原图后封面失效，并可统一生成缩略图；书籍原 TXT 仍遵循导入模式，不擅自移动 |
| 采用 EF Core SQLite，不额外叠加通用 Repository/UoW | 模型有多对多、级联和组合查询，EF 映射与迁移能显著减样板；直接 DbContext 只在 Data/Service 层使用即可保持轻量 |
| SeriesIndex 在存储与排序层采用 `double?` | 系列序号只需 1、2.5 等展示/排序精度；规避 SQLite 对 decimal 比较和排序的限制 |
| 第一版以 FileStream/RandomAccess 分块读取，不默认使用 MemoryMappedFile | 流式和指定偏移读取足以支撑 300MB 文件；MMF 不能消除解码边界/索引复杂度，作为后续基准证明必要时的优化 |
| 编码检测采用 BOM → 严格 UTF-8 → UTF.Unknown 采样评分 → 默认候选/用户确认 | 中文传统编码不存在百分之百自动识别；分层策略能提供可解释置信度并保留人工纠错路径 |
| Scroll 使用有界滑动 FlowDocument，Paged 对有界文档懒分页 | 复用 WPF 的选择/高亮/排版能力，但不让整本大文件变成 WPF 文档对象树 |
| 标注保存 Offset/Length、原文、上下文和 FileVersion | 未变化文件可精确定位；变化后能警告并为未来局部锚点恢复保留数据 |
| Grid 第一版采用按行分组的单轴虚拟化 | 避免普通 WrapPanel 实例化全部卡片，也避免过早实现复杂双轴 VirtualizingPanel |
| Shell + ViewModel NavigationService + DataTemplate | 保持 MainWindow/code-behind 干净并支持 Reader 主窗口内导航与返回栈 |

## Issues Encountered
| Issue | Resolution |
|-------|------------|
| 首次读取需求的终端输出因过长被截断 | 后续按行号分段读取，确认完整规格后再定稿架构 |
| 目录不是 Git 仓库 | 暂不改动；Phase 2 再确定是否初始化版本库 |
| Phase 7 首次按错误的顶层项目路径读取文件，随后 findings 补丁上下文也未命中 | 改用实际 `HNovaReader/`、`HNovaReader.Tests/` 路径，并先读取尾部后使用精确上下文补丁；两次失败均未修改源代码 |

## Resources
- `开发需求.md`
- `task_plan.md`
- [EF Core SQLite provider limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)
- [WPF control performance and virtualization](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls)
- [WPF FlowDocument overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/flow-document-overview)
- [.NET memory-mapped files](https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files)
- [.NET RandomAccess API](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess)
- [.NET EncodingProvider](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodingprovider)
- [UTF.Unknown repository](https://github.com/CharsetDetector/UTF-unknown)
- [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
- `docs/architecture.md`（Phase 1 架构基线）

## Visual/Browser Findings
- 官方资料确认：WPF 的内置虚拟化是单轴的；FlowDocument 提供回流/分页；SQLite/EF 对 decimal 排序有限制；.NET 提供按文件偏移异步读取 API。
## Phase 9 — 书签、高亮与笔记（2026-08-31）

### 需求硬约束

- 书签保存 `BookId`、`CharacterOffset`、章节快照、预览文本与创建时间；右侧书签列表可跳转。
- 高亮由阅读区文本选择创建，保存 `StartOffset`、`Length`、原始选中文本、样式与创建时间；首版支持 Yellow、Blue、Green 三种低刺激颜色。
- 笔记既可关联高亮，也可关联独立阅读位置；右侧笔记列表展示原文片段和笔记正文并支持跳转。
- 三类标注都必须保存锚点前后文、创建时的文件大小和最后修改时间，供文件变化检测与人工复核使用。
- 原 TXT 发生插入、删除或编辑后，首版不实现全文 diff 或自动锚点迁移；允许按旧偏移近似跳转或夹取到合法范围，但必须显示非阻断警告：`检测到原始 TXT 已发生修改。部分书签、高亮或笔记的位置可能不准确。`
- 删除书籍时，书签、高亮和笔记应随书籍级联删除。

### 实现方向

- SQLite 增加 `Bookmark`、`Highlight`、`Note` 实体；复核状态由标注保存的文件版本与当前打开文件版本比较得到，避免持久化状态失效。
- 阅读器已有 Scroll/Paged 两套 `FlowDocument` 文本面，可将 `TextPointer` 通过现有字符段映射转换为绝对字符偏移；选中文本必须从当前原始窗口按偏移截取，不能直接依赖 WPF `TextRange.Text` 的换行规范化结果。
- 跳转继续复用字符偏移窗口加载；过期锚点只做合法范围夹取，并在右侧面板标记“需复核”。
- 右侧标注面板需要覆盖创建、浏览、跳转、编辑和删除；选择浮层提供高亮、添加笔记、复制入口。

### 现有代码接缝

- `ReaderViewModel` 当前仅依赖阅读引擎工厂和阅读位置服务，持有活动 `BookDetails`、`ITextDocumentSession`、96 KiB 有界窗口、章节与当前位置；新增标注服务可沿用相同的打开/关闭生命周期。
- `GetAnchorContext` 已按当前位置提取 64 字符前后文，可泛化为任意偏移并复用于三类标注。
- `LoadWindowAsync` 与 `UpdateVisibleAnchor` 已提供字符偏移跳转所需全部能力；标注跳转不应新建第二套读取路径。
- `Book` 当前只有 `ReadingPosition` 一对一导航，需要增加三类集合导航；`HNovaReaderDbContext` 与程序集配置扫描支持直接添加实体/配置。
- DI 使用 `IDbContextFactory<HNovaReaderDbContext>` 与单例业务服务，标注服务应保持同样生命周期和异步 DbContext 使用方式。
- `EfReadingPositionService` 已建立可复用惯例：输入验证、字符串截断、SQLite UTC Kind 恢复、`TimeProvider` 时间戳和每次操作独立 DbContext。
- 阅读器的 `renderedSegments` 已同时覆盖 `Run` 与源换行段，可将 WPF 选择首尾分别映射到绝对字符偏移，再从 `CurrentWindow.Text` 获取原始选择快照。
- 当前 XAML 右栏仍是 Phase 9 占位；阅读面已启用选择且 Paged 使用 `FlowDocumentPageViewer`。现有 `CreateDocument` 是叠加高亮背景的唯一渲染入口。
- Reader XAML/code-behind 合并输出再次因体积被截断；后续只读取右栏与事件/渲染精确区间，不依赖缺失片段。

### Phase 9 最终实现结论

- 数据库使用 `Bookmarks`、`Highlights`、`Notes` 三表；Note 恰好关联 Highlight 或独立 CharacterOffset，高亮删除级联其关联笔记，Book 删除级联全部标注。
- 三类记录均持久化 FileSize、LastWriteTimeUtc、EncodingName、Prefix/Suffix；`NeedsReview` 在读取 DTO 时由保存版本与当前版本比较派生。
- Scroll/Paged 共用源字符段映射：选择首尾分别转换为绝对 CharacterOffset，SelectedText 从当前有界源窗口截取，避免 WPF 换行规范化破坏长度。
- 选择完成后自动显示浮动菜单，并保留右键入口；支持 Yellow/Blue/Green、添加笔记和复制。高亮只叠加到当前 96 KiB FlowDocument 窗口，没有整本物化。
- 右栏提供书签/高亮/笔记三页、当前位置创建、选择笔记、笔记编辑删除与锚点跳转；文件变化时显示规格原文警告，并逐条显示“需复核”。
- `20260831082211_AddReaderAnnotations` 迁移已通过 EF model drift；clean Release build 0 warnings / 0 errors，89/89 tests passed，格式检查和直接/传递依赖漏洞扫描通过。
## Phase 10 — 完善与发布准备（2026-08-31）

### 原始验收边界

- 左侧导航必须提供全部书籍、最近阅读、阅读中、未读、已读、收藏、系列、标签和设置；阅读状态只按 ReadingPosition 简单判断：无位置为未读，`0 < Progress < 95%` 为阅读中，`Progress >= 95%` 为已读。
- 首页“继续阅读”显示最近打开的一本书、作者、章节快照、进度和继续按钮；不增加复杂阅读行为统计。
- Series 页面需按系列展示，并按允许 1/2/2.5 的 SeriesIndex 排序；Tags 是真实多对多页面，不能把标签保存成逗号字符串。
- 设置页第一版覆盖 Theme、Reader Theme、Font、Font Size、Line Height、Reading Mode、Library Folder、Import Behavior、Remember Window Size、Default Text Encoding；应用设置保存到 `%LocalAppData%/HNovaReader/settings.json`。
- 应用主题支持 Dark/Light/System；Reader Theme 独立支持 Light/Dark/Sepia，阅读字体、字号、行距、段距、最大正文宽度、左右边距、文字/背景主题和 Scroll/Paged 需要可配置，但首版不复杂化。
- 安全删除严格区分“从书库移除”（只删数据库）与“删除磁盘文件”（明确二次确认并优先移入 Windows 回收站）。
- 数据库记录对应 TXT 丢失时，BookCard 必须显示“文件丢失”，提供“重新定位”和“从书库移除”，不得自动删除记录。
- 普通异常通过页面/Toast/Dialog 友好呈现，需覆盖删除/移动/无权限/占用、编码识别、超大文件、数据库损坏、封面缺失、重复导入、书库目录缺失和源 TXT 修改等主要路径。

### 首轮缺口审计

- 当前 `SettingsViewModel` 仅在内存中切换应用主题，`SettingsView` 的 Reader Theme 仍是明确占位；`IAppPaths` 尚无 SettingsPath，项目没有 settings.json 模型/服务。
- ThemeService 已正确集中切换 Dark/Light/System ResourceDictionary，可保留为应用主题执行器；持久化与完整阅读偏好应放到独立设置服务，不能塞进 ThemeService。
- LibraryViewModel/LibraryView 规模分别约 475/395 行，需要精确审计现有继续阅读、导航筛选、Series/Tags 和文件缺失能力，避免继续膨胀或重复实现已存在的编辑/删除逻辑。
- `LibraryViewModel` 已具备搜索、收藏、元数据/系列/标签编辑、刷新文件信息、数据库移除和回收站删除；安全删除的语义与二次确认已闭合。
- `LibraryBookItemViewModel` 只有开始/继续阅读文案，尚无阅读进度、章节快照或文件存在状态；打开缺失文件会到 Reader 失败后才弹错误。
- MainWindow 左侧九项导航齐全，但 NavigationService 将 Recent/Reading/Unread/Finished/Favorites/Series/Tags 全部映射为静态 `SectionPageViewModel` 占位，尚未形成真实书库筛选或聚合页面。
- LibraryView 的完整输出因体积被截断；已确认其正文只有搜索与 Grid/List，不含首页继续阅读卡片。后续按精确区间读取，避免依赖缺失片段。
- App 启动目前固定应用 `AppTheme.System`，MainWindow 只负责 Reader flush，没有窗口尺寸恢复/保存；这两个生命周期点正好可接入先加载设置、再应用主题和退出时保存窗口尺寸。
- `ILibraryInteractionService` 已隔离 WPF 对话框、Explorer、回收站和消息框，文件重定位应继续扩展该接口，而不是在 ViewModel 中直接创建 OpenFileDialog。
- `BookDetails` 尚未携带 ReadingPosition；LibraryService 查询也未 Include 它。继续阅读与状态筛选应在同一次轻量元数据查询中映射进度/章节快照，而不是为每本书单独查询。
- ManagedBookFileStore 的目标目录由导入请求提供；Shell 当前始终使用 TextImportRequest 默认值。ImportBehavior、LibraryFolder 可在 Shell 组装请求时从设置注入，编码默认值则应只作为检测失败 fallback，不能覆盖 BOM/严格 UTF-8/统计检测。
- Reader 排版参数目前有多处 code-behind 常量（字体名、1.75 行距、1040 最大宽度、固定边距），但分页签名已经包含对应字段；改为绑定设置后能够自然触发既有重排机制，无需改变分页状态模型。
- LibraryService 的 `WithDetails` 可直接 Include 一对一 ReadingPosition；扩展 BookDetails 尾部可选字段能保持测试工厂与多数调用兼容。
- 文件重定位需要由 LibraryService 在单个事务中同时更新 FilePath/NormalizedFilePath、SourceFilePath/NormalizedSourceFilePath、StorageMode 和文件元数据，并继续复用唯一路径约束；无需数据库迁移。
### Phase 10 library implementation notes

- Status classification is now derived from persisted reading progress: no position is Unread, below 95% is Reading, and at least 95% is Finished.
- Series and Tags use the same library collection but expose real group overview/detail states. Series entries are ordered by fractional `SeriesIndex`; tag membership is many-to-many.
- File relocation belongs in the library service and deliberately keeps the existing book identity, annotations, reading position, and metadata relationships.
- `LibraryView.xaml` should be changed in small, targeted slices because broad reads are truncated in this workspace.
- The actual view path is `HNovaReader/Features/Library/LibraryView.xaml`. Its shared book context menu already has file reveal/refresh actions and is the correct place to add relocation; the card cover grid can host a top-right missing-file badge.
- The page has six stable rows: header, spacer, search/toolbar, spacer, content, footer. Continue Reading can be inserted above search inside row 2 without rewriting the outer grid; group overview can share the row-4 content layer, while grid/list visibility must also require `IsBookCollectionVisible`.
- Existing list columns have room for a compact file-state column, and the header’s right-side action area can hold a Back-to-groups action alongside Import.
- `LibraryGroupViewModel` exposes `Name`, `Books`, and `Count`. Book items expose `ReadingProgressText`, `ReadingChapter`, `FileAvailability`, and `RelocateFileCommand`, so the remaining work is presentation wiring rather than new state.
- `BookStorageMode` lives in `HNovaReader.Data.Entities`; test doubles that construct relocation results need that namespace explicitly.
- Existing Phase 9 tests cover library search, large-list row bounds, favorite/edit/delete/open behavior and core metadata operations, but not the new Phase 10 status classification, Continue Reading, group navigation, relocation, or route-to-section mapping.
- `TestBookFactory` currently fixes `SeriesIndex` to 1 and omits reading fields/last-open overrides. Extending it with optional trailing parameters is the least disruptive way to express Phase 10 scenarios while preserving every existing caller.
- The interaction stub already records messages and accepts a replacement path; the library stub should also record the last relocation request so file recovery assertions remain direct.
- `BookDetails` appends optional reading offset/progress/chapter fields, so tests can extend the factory without changing production constructors.
- The filter implementation uses `< 0.95` for Reading and `>= 0.95` for Finished, orders Recent and Continue Reading by `LastOpenedAtUtc`, and sorts books inside Series by null-index last, then fractional index, then title.
- `LibraryServiceTests` uses an isolated SQLite database and a shared `CreateRequest`, making it suitable for verifying that relocation changes only file fields and rejects a path already owned by another book.
- Navigation implementation is at `HNovaReader/Infrastructure/Navigation/NavigationService.cs`; it maps all library-category routes to the shared instance and applies the corresponding `LibrarySection` after navigation.
- Settings Save command already converts persistence/encoding errors into inline status text, but Apply Theme calls the same persistence path without a catch. Theme persistence failure should also remain non-blocking.
- Invalid JSON is preserved as `.corrupt-*`, but a syntactically valid settings file containing an invalid library path can throw `ArgumentException` during normalization and currently escape startup recovery.
- `AppSettingsServiceTests` already owns safe isolated-directory creation/cleanup and corrupt-file assertions, so malformed-path recovery can reuse that fixture directly.
- Reader settings are applied both in the constructor and through the `SettingsChanged` event; a focused test should assert independent reader theme, typography, margins, and paged/scroll mode changes, plus event unsubscription through disposal if practical.
- `ReaderViewModelTests` can add this settings-event test without opening a file/session; it only needs the settings namespace and existing reader test doubles.
- The repository already has `README.md` and `docs/architecture.md` for release documentation.
- `SectionPageViewModel` and its MainWindow data template are now unused remnants of the old placeholder navigation; no production route references the type.
- `README.md` is stale at “Phase 7”, omits Phase 8–10 capabilities and `settings.json`, and should become the final delivery/run/validation guide.
- `docs/architecture.md` is explicitly a Phase 1 architecture baseline, so it should remain historical rather than being rewritten as release status.
- `task_plan.md` still says Phase 10 is not started in its final note; completion work must update both checkboxes and that note after gates pass.
- `MainWindow.xaml` still imports `Features.Shared` only for the now-unused placeholder template; both can be removed together.
- The app project does not declare Nullable in its own project file, so the release audit must confirm whether it is inherited from a repository-level build props file before judging compliance.
- A repository-level `Directory.Build.props` exists; its contents still need direct verification because the first combined search did not print a Nullable match.
- Shared `PageViewModel` remains actively used, but only `SectionPageViewModel`/`SectionPageView` and their MainWindow template are obsolete; removal must not drop the shared namespace from C# ViewModels.
- `Directory.Build.props` confirms Nullable, implicit usings, deterministic output, and warnings-as-errors for both projects.
- The validated publish is framework-dependent `net8.0-windows`; it contains the app executable and all application dependencies while relying on the .NET 8 Desktop Runtime.
## 2026-09-01 — Phase 11 requested optimizations

- 收藏入口需要更大的星形，并使用区别于普通文字/按钮的收藏色，让空心与实心状态更容易辨识。
- Reader 顶栏需要直接切换独立的 Light/Dark/Sepia 阅读主题，同时持久化到现有 `settings.json`。
- Reader 顶栏需要编辑模式，可直接修改当前 TXT 内容并显式保存；实现必须继续避免整本大文件默认载入内存，并在保存后安全刷新文件版本、章节索引、阅读锚点和标注复核状态。
- 默认导入行为改为 ManagedCopy，设置页需要可选择书库存书根目录，而不只是手工输入路径。
- 收藏按钮目前是 32×32、白色文字的 `HNova.IconButton`，仅通过 `☆/★` 区分，确认了用户所述的辨识度问题；可在卡片局部样式中放大字形并用独立主题色区分已收藏状态。
- `AppSettings.ImportBehavior` 当前默认 `OriginalLocation`，而 Shell 已经把 `ImportBehavior` 与 `LibraryFolder` 传入导入请求；第 4 项主要是默认值、目录选择交互和空目录回退策略，不需要重写导入管线。
- Reader 顶栏右侧已经承载布局、字号、标注栏和专注模式；主题快捷入口可作为紧凑下拉菜单加入同一区域，并通过现有 `IAppSettingsService.SaveAsync` 持久化。
- Settings 页已允许手工输入管理型书库目录，但没有文件夹选择按钮或设置交互抽象；应增加可测试的文件夹选择服务。`IAppPaths.ManagedLibraryDirectory` 已提供可靠默认目录。
- Reader 当前只有有界 `CurrentWindow` 和活动 `ITextDocumentSession`。编辑模式应编辑当前有界文本窗，并使用流式“替换字符区间”保存，避免正常阅读或编辑数百 MB 文件时整本载入内存。
- `ReaderFileVersion` is a file-size/UTC-last-write tuple and `ITextDocumentSession.OpenedFileVersion` already provides an optimistic concurrency token for edits.
- `TextImportService` already falls back to `IAppPaths.ManagedLibraryDirectory` when the configured root is blank, so the settings UI can safely display/choose a custom root without making it mandatory.
- Reader's two reading surfaces share one center `Grid`; edit mode can add a third layered `TextBox` surface and collapse/disable paging input while active without disturbing the existing source-offset rendering.
- `CloseSessionAsync` cancels indexing, disposes the active session, clears the current book/window and annotations. Saving must capture the book/version/edit range first, close the session to release file handles, stream the replacement, refresh library metadata, then reopen at a deliberate anchor.
- Existing `OnPreviewKeyDown` handles page keys only for Paged mode; it must early-return during edit mode so arrow/PageUp/PageDown remain normal text-editing keys.
- Existing `TextEncodingUtilities` already centralizes encoding creation and BOM/preamble detection, so the streaming editor can reuse its encoding rules instead of introducing a second codec map.
- Adding file editing and folder picking will affect DI plus 10 explicit `ReaderViewModel` test construction sites; shared test doubles/factories should absorb the new dependency rather than adding production default constructors.
- Dark/Light theme dictionaries have no favorite-specific color. Add a shared `FavoriteBrush` key (warm amber with suitable contrast), enlarge the card star to about 20px/38px, and use a trigger so only the filled state receives the accent color while the empty star stays neutral.
- `TextEncodingUtilities` exposes internal encoding creation and exact preamble length inside the Reader namespace. The editor can preserve the original BOM bytes, stream characters with `StreamReader`/`StreamWriter`, and keep CharacterOffset semantics aligned with the reader.
- `OpenBookAsync` currently derives its initial anchor exclusively from persisted position and resets to zero when the file version changes. After an intentional edit it needs a private reopen path with an explicit anchor and suppressed generic external-change warning, while annotations should still reload as “needs review.”
- The streaming range-editor patch is present and preserves the source BOM while using a no-preamble writer encoding. Reader integration points are centralized in `OpenBookAsync`, `SavePositionCoreAsync`, `LoadAnnotationsAsync`, and `CloseSessionAsync`, so edit save can safely close the active session before atomic replacement and then reopen at an explicit anchor.
- Reader open currently clears UI state and loads the saved position inline, while close nulls `currentBook` and the active window. The edit workflow therefore needs to capture book/session/window state before close, and the open path should accept an optional explicit character anchor for the intentional post-save reopen.
- `ILibraryService.RefreshBookFileInfoAsync` returns an updated immutable `BookDetails` and only needs file size, UTC write time, and encoding. This is the correct post-edit metadata boundary and lets the reader reopen with the new file version without mutating title/author/tags.
- Reader tests construct the view model directly in 12 places. The existing shared `StubLibraryService` is reusable; a small `StubTextFileEditService` plus mechanical dependency insertion will keep tests deterministic and allow direct assertions on range/version requests.
- Reader XAML top-right has enough structure to insert compact Edit/Cancel/Save controls before the theme menu. The center grid can host a bound multiline `TextBox`; existing scroll, paged, and page-navigation visibility triggers must additionally collapse when `IsEditMode` is true.
- Reader theme brushes are resolved in code-behind while building the document and currently applied only to `ReaderBox`/`PagedReader`. The edit `TextBox` should be named and receive the same background/foreground in `CreateDocument`; input handlers should early-return in edit mode so editing keys and scrolling stay local to the editor.
- The reader test fixture already has isolated temp-file helpers and the library stub mutates its in-memory `Books` list on metadata refresh. A save/reopen test can use `StubReaderEngineFactory.SessionFactory` to return a fresh session for each open and assert the second request anchor plus refreshed version.
- Annotation review is derived from individual snapshot flags rather than a mutable service flag. Existing annotation coverage already verifies old-version warnings, so the edit test can focus on range/version/reopen behavior while preserving that established reload path.
- Phase 11 implementation and 114-test regression suite are complete; remaining delivery work is README/plan status, formatter/model-drift checks, and a refreshed framework-dependent publish directory.

## 2026-09-01 — Phase 12 library UI refinement

- 用户随后明确更正：只删除“继续阅读”横向卡片；搜索框、书籍数量、刷新和 Grid/List 切换工具栏必须完整保留。
- 书籍右键菜单已有统一“编辑书籍信息”入口，单独的“添加标签”和“设置系列”属于重复功能，应从菜单移除，但编辑窗口内的标签与系列字段继续保留。
- “右键编辑界面做好看一点”指向现有书籍元数据编辑窗口，需要先审计其 XAML、主题资源和交互边界，再在不改变保存语义的前提下重做视觉层级。
- Historical requirements originally included search and Continue Reading, but the latest explicit user feedback只取消 Continue Reading 卡片的页面呈现。保留搜索和视图工具栏，并仅移除 Continue Reading 对应 XAML，底层 ViewModel 状态暂不删除，以免无谓影响阅读状态与既有测试。
- `LibraryView.xaml` places Continue Reading and the preserved toolbar in the same row-2 `StackPanel`; delete only the first conditional `Border` and leave the following toolbar `Grid` untouched. The context menu has exactly two redundant `EditCommand` items (“添加标签”“设置系列”) plus their surrounding separator.
- `BookEditorWindow.xaml` is a flat two-column form with no cover preview, section grouping, field hints, or surfaced validation message. Its existing ViewModel/save semantics can stay unchanged while the window is restyled into a header, live cover/identity panel, grouped metadata cards, and a fixed action footer using existing theme resources.
- `BookEditorViewModel` already owns all validation and cover commands, but only exposes a display filename. A polished live cover panel needs an observable preview path initialized from the current cover and updated by choose/remove; the existing `CoverImageConverter` can render it without a new image stack.
- Existing global controls already provide rounded themed text boxes, cards, primary/secondary buttons, and all required surface/text/accent brushes. The editor redesign can be entirely local XAML plus one observable cover path; no new global theme keys or dependencies are needed.

## 2026-09-01 — Phase 13 context menu redesign

- 用户截图中的左侧空白列来自 WPF 默认 `MenuItem` 模板为图标/勾选状态预留的 gutter，而不是菜单数据本身。需要为书籍菜单提供无图标列的专用模板，而不是仅调整 Padding。
- 目标采用紧凑 Windows 11 风格：单列文字、圆角菜单容器、8px 项间距/内边距、悬停使用主题 SurfaceHover/Focus、细分隔线，以及红色“删除磁盘文件”。
- `BookContextMenu` is local to `LibraryView.xaml` and contains only command items—no checkmarks, icons, gestures, or submenus—so a deliberately simple one-column `MenuItem` control template is safe and will not regress nested-menu behavior elsewhere in the app.
- Global ContextMenu/MenuItem styles currently set only colors and padding, leaving the default WPF template (and its gutter) intact. Keep global menus unchanged and scope the new container/item/separator templates to `BookContextMenu` resources.
- The finished local template renders only each item's header, so the default WPF icon/check gutter is structurally absent rather than visually hidden. Existing commands and bindings remain unchanged, including the themed danger foreground on permanent file deletion.

## 2026-09-01 — Phase 14 application and UI icon

- 用户要求“程序和界面加一个图标”，范围按统一品牌处理：Windows EXE/任务栏图标、主窗口标题图标，以及应用内左上角品牌标识使用同一套视觉语言。
- 现有界面已有简化菱形标记，因此优先延展为清晰的 H/N 书页符号并制作多尺寸 `.ico`；这是代码/矢量资源任务，不需要使用位图生成技能。
- 项目当前没有任何 `.ico`/`.png`/`.svg` 品牌资源，`HNovaReader.csproj` 也未设置 `ApplicationIcon`，因此发布的 EXE、任务栏和窗口仍使用默认 WPF 图标。
- 主窗口左上角已有硬编码的 AccentBrush 菱形 Path 与“HNOVA / LOCAL READER”文字；主窗口和书籍编辑窗口都没有 `Icon`。最佳接入点是新增共享矢量品牌资源供界面复用，并用同一造型生成多尺寸 `.ico` 接入项目与所有顶层窗口。
- 本机没有 ImageMagick/Pillow，但 Windows `System.Drawing` 可用。可用一个可复现的 PowerShell 生成器绘制透明圆角蓝色底、白色展开书页和青色书脊，并组装 16/20/24/32/40/48/64/128/256px PNG 帧的 ICO，无需新增 NuGet 或运行时依赖。
- 现有主题强调色是浅色 `#0284C7`、深色 `#38BDF8`。品牌图标采用二者之间的蓝色渐变与白色书页，能同时适配浅/深窗口；侧栏把 18px 菱形升级为约 34px 图标即可保持文字区不变。
- 生成的 256px 预览视觉复核通过：透明圆角、蓝色层次、白色展开书页与青色书脊轮廓清晰，细节线克制；造型在小尺寸仍主要依靠高对比书页轮廓识别。
- 从隔离 Release EXE 提取出的实际关联图标复核通过：Windows 选择的小尺寸帧正确显示为清晰的蓝底白色书本，而不是默认 WPF 图标，证明 `ApplicationIcon` 已进入可执行文件资源。
- 实际 WPF 主窗口复核通过：标题栏左上角显示小尺寸书本图标，侧栏品牌区显示 34px 同款图标，并与 HNOVA/LOCAL READER 文字在视觉上垂直居中；导航、书架卡片和工具栏布局未受影响。
- 最终发布 EXE 的关联图标与隔离验证 EXE 提取图标哈希一致，确认发布目录包含同一品牌图标而非仅开发构建生效。

## 2026-09-01 — Phase 15 Steam-style managed library tree

- 用户确认采用“书库根目录 → 自定义书架文件夹 → 系列 → TXT”的真实目录结构，并授权开始实现；未分类书籍默认进入系统收件箱的“未分系列”。
- 已确认的交互包括 Steam 式快速访问与树形目录、Ctrl/Shift 多选、拖动选中书籍到文件夹/系列、同步真实文件位置，以及管理型复制作为默认导入行为。
- 设计原则保持数据库中的 Book.Id 为稳定身份；目录是由书架/系列元数据驱动的磁盘镜像，移动路径不能改变阅读位置、标注、封面和其他关联数据。
- 当前 Series 只有全局唯一的 NormalizedName，Book 只持有 SeriesId；要允许不同书架下同名系列，需要新增 LibraryFolder、Series.FolderId，并把系列唯一约束改为 `(FolderId, NormalizedName)`。Book 还需直接持有 FolderId 才能表达“该书架下未分系列”。
- `TextImportService` 目前将管理型副本平铺到根目录；默认目标应改为 `<根>/收件箱/未分系列`，并把 FolderId/SeriesId 随 AddBookRequest 一起保存。现有 ManagedBookFileStore 已具备安全 CreateNew 自动重名机制，可复用。
- `RelocateBookFileAsync` 是“用户重新定位丢失源文件”的语义，并会把 StorageMode 改为 OriginalLocation，不适合目录拖放。需要独立的组织移动服务，负责 ManagedCopy 文件移动与数据库路径/Folder/Series 原子更新。
- 当前 Grid 用虚拟化行 ListBox 包装每行 ItemsControl，卡片本身不是可选容器；ListView 也未声明 Extended selection。多选需要统一的选择协调器，并在卡片模板、列表容器与拖放逻辑之间共享选中 Book.Id 集合。
- MainWindow 现有拖放只识别外部 FileDrop 并全局导入；书库内部拖放应使用自定义 DataFormat，限定在 LibraryView 中处理，避免与外部 TXT 导入冲突。
- DI 集中在 `Composition/ServiceCollectionExtensions.cs`，新增目录查询/组织移动服务可按现有 singleton + DbContextFactory 模式注册；LibraryViewModel 是 singleton，选择与树展开状态可在导航切换时自然保留。
- 书架 Grid 当前以四本一行的 `LibraryBookRowViewModel` 虚拟化；为避免推翻大书库性能结构，可让每张卡维护 `IsSelected`，由 LibraryView code-behind 把 Ctrl/Shift 指针语义交给 ViewModel，并让同一选中集合驱动 ListView 的视觉状态。
- 系列元数据编辑目前直接调用 `UpdateBookAsync`，未来若改变系列会绕过真实文件移动。Phase 15 应让编辑窗口保存继续更新非组织字段，而系列变更改走组织服务，或者在 UpdateBookAsync 内明确协调 ManagedCopy 移动；优先集中到组织服务以保持单一安全路径。
- 测试支架已有真实临时 SQLite、真实文件导入测试和 StubLibraryService；迁移后需要机械扩展 BookDetails/TestBookFactory/StubLibraryService，并新增真实批量移动回滚与默认收件箱路径测试。
- 测试项目已有可设置 Current 的 StubAppSettingsService，可直接为真实 OrganizationService 提供隔离书库根目录，不需要给生产服务增加测试专用构造器。
- EF migration `AddLibraryFolders` naturally creates nullable FolderId columns and the composite series index. It was augmented to insert the deterministic Inbox row and backfill existing Series/Books before foreign keys are used; no Book primary/annotation relationship is rewritten.
- 用户追加安全要求：迁移、删除或重命名前必须先备份；备份目录位于当前配置的书库根目录，但不能作为书架/系列出现在 UI，也不能被递归 TXT 导入重新扫描。
- 备份采用 `<根>/.hnova-backups/<UTC时间-操作>/<原相对路径>`，并写入 manifest 记录 BookId、源路径、目标路径和操作类型。备份失败应中止原操作，第一版不自动清理历史备份。
## Phase 15 UI interaction seam

- `ILibraryInteractionService` already centralizes modal UI, so folder/series naming can be added there without coupling the library view model to WPF windows.
- `WpfLibraryInteractionService` consistently owns dialogs with `Application.Current.MainWindow`; the new naming dialog should follow that pattern.
- Existing editor windows use app dynamic resources, the packaged icon, center-owner behavior, and simple code-behind validation; the naming dialog can reuse the same conventions without a separate view model.
- `LibraryViewModel` currently rebuilds flat/group projections from `_allBooks`; the least disruptive tree integration is to add an independent active tree target and apply it before the existing section switch.
- Destructive disk deletion currently recycles before removing the database record and has no safety copy; it must call organization backup first so backup failure aborts recycling.
- Organization contracts already expose all tree metadata and a batch move result with per-book failures; UI can display partial failures without reimplementing disk logic.
- Tree nodes carry stable folder/series IDs, while each `BookDetails` now carries folder and series IDs, so filtering and drag targets do not need name-based matching.
- `ShellViewModel` is the correct bridge for imports and navigation; it needs to expose the singleton library VM and pass an optional tree target into `TextImportRequest`.
- The app-level drop handler must explicitly reject internal book drag data; otherwise bubbling from the tree can incorrectly treat internal moves as file imports.
- A single sidebar tree can handle both file imports (copy effect) and selected-book moves (move effect), with the closest `TreeViewItem` supplying the stable target IDs.
- Root migration can be made recoverable by saving the new root first, running the transactional organization migration, and restoring the previous settings if migration fails; the move service itself reverses completed file moves on failure.
- Theme quick-save must not persist an edited-but-unconfirmed library root, so it should save only the theme field from current settings.
- Integration tests can reuse the existing migrated SQLite fixture and concrete `LibraryService`; managed files created beneath its temporary root allow identity, disk move, backup manifest, and DB path assertions in one test.

## 2026-09-02 — Phase 16 rename and series drag corrections

- Inbox rename is explicitly blocked twice: `LibraryTreeNodeViewModel.CanRename` excludes system nodes and `LibraryOrganizationService.RenameFolderAsync` throws for `IsSystem`; both guards conflict with the latest requirement.
- Folder/series renames currently move tracked book files but never create the renamed directory for an empty node or remove the now-empty old directory. This explains why SQLite/UI names can change while Windows still shows the old folder.
- Series-to-folder drag needs its own tree data format; book drag data must remain distinct so the drop handler can route series moves only to folder nodes and book moves to folder/series/unsorted nodes.
- A safe series move should retain Series.Id, reject a same-name series collision in the destination folder, update every child Book.FolderId, move managed TXT files through the existing backup/rollback path, then remove only empty obsolete directories.
- The current drop router only recognizes the book data format and external FileDrop. Series drag can be added entirely in `MainWindow` by recording a series node on pointer-down, emitting a second data format after the system drag threshold, and accepting it only over folder nodes.
- The hierarchy template dedicates a fixed 22px first column to `Glyph`; removing both that column and TextBlock eliminates the requested icons without changing TreeView expanders, indentation, names, or count badges.
- Navigation does not introduce a persistent selection race for the library tree: when already on Library it returns synchronously, and when changing pages it sets the route/section before `SelectLibraryNodeAsync` applies the clicked node. The rename failure was therefore caused by eligibility/service guards and physical directory handling, not command generation.

## 2026-09-02 — Phase 17 custom unsorted name and safe deletion

- “未分系列” is currently a virtual node backed by `Book.SeriesId == null`, not a `Series` row. Renaming it correctly requires a persisted `LibraryFolder.UnsortedName`; converting it into a normal series would recreate an empty fallback node and produce confusing duplicate semantics.
- Safe deletion semantics: deleting a series moves its books to that folder's unsorted bucket; deleting a non-system folder moves all books to the system Inbox unsorted bucket. Neither action deletes TXT files or book records, and Inbox itself remains undeletable.
- `UnsortedName` is hardcoded only in the organization service path helper, tree construction, and focused tests. Adding one required `LibraryFolder.UnsortedName` column and carrying it in `LibraryFolderDetails` is a contained migration rather than a broad book-schema change.
- `LibraryTreeNodeViewModel.CanRename` 是“未分系列”无法重命名的直接 UI 原因；树节点名称也仍硬编码为默认值，没有使用 `LibraryFolderDetails.UnsortedName`。
- 树右键菜单当前只有新增与重命名，适合在同一菜单追加按节点类型启用的安全删除入口。
- `Book.FolderId`/`SeriesId` 外键删除行为为 `SetNull`，而 `Series.FolderId` 为级联删除；删除服务必须先显式把书籍迁往目标书架并清空 `SeriesId`，再删除元数据，避免孤儿记录。
- 书籍信息编辑会通过 `LibraryService.ResolveSeriesAsync` 按名称隐式新建系列；这里也必须禁止名称与目标文件夹的 `UnsortedName` 相同，避免树中出现两个同名组织节点。
- EF 自动生成迁移把既有文件夹的 `UnsortedName` 回填成空字符串；必须手动改为“未分系列”，否则老数据库升级后树节点为空且路径解析错误。
