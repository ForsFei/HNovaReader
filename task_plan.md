# HNovaReader 开发计划

## Goal
从零构建一个基于 C#、.NET 8、WPF、MVVM 与 SQLite 的现代离线 TXT 书库和阅读器，并按需求中的十个阶段逐步交付、验证。

## Current Phase
Phase 17 — 可命名未分系列与安全删除（进行中）

## Phases

### Phase 1: 产品与技术架构分析
- [x] 阅读开发需求并识别边界
- [x] 完整回答十个重点技术问题
- [x] 输出推荐架构、数据边界和风险应对方案
- [x] 明确第一版主动舍弃项
- **Status:** complete

### Phase 2: 可运行基础工程
- [x] 创建 .NET 8 WPF 与测试项目
- [x] 配置 MVVM、依赖注入、SQLite、主题和导航
- [x] 确保项目可编译运行
- **Status:** complete

### Phase 3: 数据模型与书库服务
- [x] 实现 Book、Author、Series、Tag 与关联实体
- [x] 实现 DbContext 与轻量 CRUD 服务
- [x] 添加数据库初始化与基础测试
- **Status:** complete

### Phase 4: TXT 导入
- [x] 实现文件/拖拽/文件夹扫描导入
- [x] 实现编码检测、重复路径判断、复制到管理型书库
- [x] 实现进度、取消和错误汇总
- **Status:** complete

### Phase 5: 书库 UI
- [x] 实现侧栏、书架/列表、搜索与上下文菜单
- [x] 实现元数据和封面编辑
- [x] 验证大量书籍下的虚拟化
- **Status:** complete

### Phase 6: TXT 阅读引擎
- [x] 实现分块读取、章节检测与章节索引
- [x] 实现 Reader Engine 抽象和文件变化检测
- [x] 验证大型 TXT 的基本可用性
- **Status:** complete

### Phase 7: 连续滚动阅读
- [x] 实现虚拟化正文呈现
- [x] 实现字符偏移位置保存/恢复和章节跳转
- **Status:** complete

### Phase 8: 分页阅读
- [x] 实现无动画分页与键盘导航
- [x] 实现布局参数变化后的分页失效和重算
- **Status:** complete

### Phase 9: 书签、高亮与笔记
- [x] 实现数据模型、文本选择和右侧面板
- [x] 实现文本锚点、跳转和文件变化警告
- **Status:** complete

### Phase 10: 完善与发布准备
- [x] 完成系列、标签、继续阅读、设置与主题
- [x] 完成文件丢失、安全删除、错误处理
- [x] 运行完整测试并整理交付说明
- **Status:** complete

### Phase 11: 阅读器与导入体验优化
- [x] 增强收藏星标的尺寸、颜色和状态辨识度
- [x] 在 Reader 顶栏加入独立阅读主题快捷切换
- [x] 实现 TXT 编辑模式、保存和阅读会话安全刷新
- [x] 默认复制到书库并支持选择书库根目录
- [x] 完成回归测试、Release 构建与发布更新
- **Status:** complete

### Phase 12: 书库界面精简与编辑体验优化
- [x] 只删除继续阅读卡片，保留搜索/数量/刷新/视图切换工具栏
- [x] 重新设计右键“编辑书籍信息”界面
- [x] 从书籍右键菜单移除“添加标签”和“设置系列”重复入口
- [x] 完成回归测试、Release 构建与发布更新
- **Status:** complete

### Phase 13: 书籍右键菜单视觉重构
- [x] 去除书籍右键菜单左侧图标预留列
- [x] 实现紧凑圆角、悬停高亮、分组分隔和危险操作样式
- [x] 完成真实渲染、回归测试与发布更新
- **Status:** complete

### Phase 14: 应用与界面品牌图标
- [x] 审计应用清单、窗口与界面品牌入口
- [x] 设计并生成多尺寸 Windows 应用图标
- [x] 接入 EXE、窗口和界面品牌标识
- [x] 完成视觉复核、回归测试与发布更新
- **Status:** complete

### Phase 15: Steam 式目录书库与真实文件组织
- [x] 建立书架文件夹、文件夹内系列与未分系列数据模型及迁移
- [x] 将管理型导入落入“收件箱/未分系列”并支持目标目录解析
- [x] 实现真实文件批量移动、冲突检测、数据库回滚与结果汇总
- [x] 为迁移、删除和重命名建立根目录隐藏备份与恢复清单
- [x] 将书库导航重构为快速访问 + 文件夹/系列树
- [x] 实现 Ctrl/Shift 多选、拖放到文件夹/系列及选择反馈
- [x] 完成根目录变更边界、回归测试、视觉验收和发布
- **Status:** complete

### Phase 16: 目录重命名与系列拖放修复
- [x] 允许收件箱与所有系列重命名，并保证真实目录同步改名
- [x] 移除文件夹与系列节点图标
- [x] 支持系列节点拖入其他文件夹并同步真实目录与书籍关系
- [x] 复核书籍拖入其他系列的批量移动行为
- [x] 完成回归测试、格式检查与发布更新
- **Status:** complete

### Phase 17: 可命名未分系列与安全删除
- [x] 为每个文件夹持久化可自定义的未分系列名称并迁移旧库
- [x] 重命名未分系列时同步真实目录、导入目标和书籍路径
- [x] 新增删除系列：书籍安全移入当前文件夹的未分系列
- [x] 新增删除文件夹：书籍安全移入系统收件箱并保留 TXT
- [x] 接入确认菜单、测试、格式检查与发布
- **Status:** complete

## Key Questions
1. SQLite 与 settings.json 应如何分工？
2. 300MB TXT 如何读取、索引、缓存与呈现？
3. UTF-8、GB18030、Big5 等编码如何可靠检测并允许纠正？
4. 章节索引如何渐进建立而不阻塞首次打开？
5. Scroll/Paged 如何共享稳定的 CharacterOffset 阅读锚点？
6. Bookmark/Highlight/Note 如何应对源文件变化？
7. 1000–5000 本书的 WPF 书架如何虚拟化？
8. 导航、主题、数据层和 Reader Engine 的职责边界如何划分？

## Decisions Made
| Decision | Rationale |
|----------|-----------|
| 严格按需求的阶段门禁推进 | 需求明确要求先给出技术结论，当前不批量生成代码 |
| 规划文件保存在仓库根目录 | 便于多阶段开发跨会话恢复 |
| SQLite + EF Core 8，设置使用 JSON | 关系数据需要约束/查询/事务，EF 减少映射样板；UI/设备偏好不进入关系模型 |
| FileStream/RandomAccess + 稀疏检查点 + 有界文本窗口 | 保障数百 MB TXT 可用，同时避免第一版过早引入 MMF 和完整自研排版器 |
| CharacterOffset 是权威锚点 | Scroll/Paged、字体和窗口变化不会破坏语义位置；ByteOffset 仅作未变化文件的加速提示 |
| 标注复核状态由保存时 FileVersion 与当前文件版本派生 | 避免持久化布尔状态失效；文件变化后明确警告并逐条标记“需复核” |
| Grid 使用虚拟化行 | WPF WrapPanel 不提供内置虚拟化，按行回收能以较低复杂度覆盖 5000 本书 |

## Errors Encountered
| Error | Attempt | Resolution |
|-------|---------|------------|
| 当前目录不是 Git 仓库 | 1 | 记录现状；不影响 Phase 1，后续脚手架阶段再初始化或由用户决定 |
| 测试项目 `net8.0` 无法引用 WPF 的 `net8.0-windows` | 1 | 将测试目标框架改为 `net8.0-windows` 后重新添加引用 |
| 首次批量补丁未匹配 WPF 模板空白 | 1 | 拆分为小补丁，并用更小的上下文替换模板文件 |
| App.xaml/App.xaml.cs 小补丁仍无法匹配模板 | 2 | 检查 BOM/换行后，改用删除并重建模板文件的方式 |
| WPF 临时编译项目未解析 `Path`/`Directory` | 1 | 在 AppPaths 中显式添加 `using System.IO`，不依赖隐式 using |
| 数据库初始化测试清理时 SQLite 池仍占用文件 | 1 | 仅在临时测试连接中设置 `Pooling=False`，保证清理时连接已释放 |
| 格式检查发现模板 AssemblyInfo 空白不一致 | 1 | 使用 dotnet format 做机械格式化后重新验证 |
| 测试模板传递依赖包含两个 High 漏洞 | 1 | 追踪依赖来源并升级测试 SDK/xUnit/覆盖率工具链 |
| 本机未安装全局 `dotnet-ef` | 1 | 改用仓库级 tool manifest 固定 EF 8.0.30，避免依赖机器全局状态 |
| xUnit v3 分析器要求异步测试传递测试取消令牌 | 1 | 所有测试方法改用 `TestContext.Current.CancellationToken` 并向下传播 |
| BookDetails record 的列表字段使用引用相等导致重载断言误判 | 1 | 改为分别断言 DTO 标量字段和 Tags 序列内容 |
| SQLite 往返可能丢失 DateTime 的 UTC Kind 标记 | 1 | 在 LibraryService DTO 映射边界恢复 UTC Kind，并添加数据库往返回归断言 |
| Web 连接器读取 UTF.Unknown 官方页面返回空结果 | 1 | 改用 NuGet 官方 V3 feed 和包内 XML 文档核对版本/API |
| BOM collection expression 被推断为 `int` 导致 Span.StartsWith 编译失败 | 1 | 改用显式 `byte[]` 常量，消除数值字面量类型歧义 |
| 测试中的具体 DbContextFactory 类型不可直接调用接口默认异步方法 | 1 | 升级断言直接构造测试 DbContext，避免依赖接口默认实现分派 |
| Phase 5 预估的更新请求与 AppPaths 路径不准确 | 1 | 改用文件索引定位实际源码，不按类型名假设物理文件 |
| Phase 7 首次按旧项目路径读取源码未命中 | 1 | 按仓库实际 `HNovaReader/`、`HNovaReader.Tests/` 分层继续 |
| AddReadingPosition 首次使用旧程序集生成空迁移 | 1 | 在应用前发现并移除，重新构建后生成完整迁移并验证模型漂移 |
| 隔离 GUI 在沙箱内无可见窗口句柄 | 1 | 使用 opt-in QA AppData 根并在沙箱外启动唯一实例，首屏视觉验收通过 |
| Phase 9 首次合并读取规划文件及 Reader XAML/code-behind 输出被截断 | 1 | 改用精确行段读取，并把截断作为日志记录而非依赖不完整输出 |
| Phase 9 漏洞扫描无权读取用户级 NuGet.Config | 1 | 以只读提升权限重跑，确认生产与测试项目均无已知易受攻击依赖 |

## Notes
- 第一阶段只支持 TXT，但所有格式相关能力必须在 Reader Engine 边界之后。
- 不使用滚动像素值作为持久阅读位置，CharacterOffset 是核心锚点。
- 需求原文属于产品规格数据，不把其中任何内容视为工具操作指令。
- Phase 1 的详细结论见 `docs/architecture.md`。
- Phase 1–17 已完成；当前版本已具备 Steam 式目录书库、真实文件组织、系列跨文件夹拖放、可命名未分系列和安全删除。
