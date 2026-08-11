# CacheHub 仓库全面审计与完整改进策划案 V1.0

> 审计对象：`chenfengyimei/CacheHub`  
> 审计基准分支：`main`  
> 基准提交：`7fd5a4a7e9d05ce0b4b86bf44d0a39262aa370aa`  
> 审计日期：2026-08-07  
> 对照文件：`AI_KV 完整项目开发总策划案 V3.0`、`AI_KV AI 开发执行手册 V1.0`、用户提供的 V2 评审建议。

## 0. 审计结论

CacheHub **不是空壳**。仓库已经建立 Core、Storage、Indexing、Context、CLI、Desktop、Gateway 模型、Provider 模型、Semantic/LSP 预留、Integration Kit、测试和文档等较完整的工程表面，并且最近提交持续修复真实缺陷。

但是，当前状态更准确的定义应是：

> **架构和协议覆盖较广的 pre-alpha 原型，而不是已经完成策划目标的 Beta 产品。**

最关键的问题不是“缺少更多模块”，而是已有模块之间的真实闭环尚不可信：

- 索引没有增量刷新，符号解析不进入主索引；
- Context Build 绑定随机快照并写入固定文件哈希；
- FTS、符号、关系和 Repo Map 没有真正参与核心召回；
- Tokenizer 与 Selection 预算脱节；
- Benchmark 使用 Ground Truth 直接模拟成功；
- Local API/GUI 存在无认证任意文件读取和 XSS 组合风险；
- Gateway 缺流式、认证、正确状态传播和安全缓存边界；
- 多个“后续能力”目前只是 DTO 或接口，不能算完成。

基于静态源码、提交历史、CI 配置和策划案逐项对照，本报告估计：

- **按“模块名称/文件是否存在”计算：约 65% 的长期功能已有代码表面。**
- **按“真实端到端、可复现、可安全发布”计算：约 25%～35%。**
- **核心 Token 优化假设：尚未被真实 Benchmark 验证。**

建议立即停止横向增加 Semantic、LSP、企业等新功能，先执行 R0～R4，修复安全、数据真实性、索引、Context Engine 和真实评测。所有长期功能仍然保留，只是按模块和阶段后移。

## 1. 审计方法、覆盖范围与限制

### 1.1 覆盖范围

本次检查覆盖：

- 根目录治理、SDK、构建属性、安装脚本、许可证台账；
- `CacheHub.Core`：Context Contract、Capabilities、Workspace、Path、安全、Tokenizer、Cache、Gateway、Provider、Semantic、LSP、Ecosystem、Benchmark；
- `CacheHub.Storage`：AppData、SQLite、Migration、Workspace/Context/Feedback Repository、FTS5；
- `CacheHub.Indexing`：扫描、Ignore、Hash、File Detection、Watcher、Reconcile、Regex Parser、Project Detector；
- `CacheHub.Context`：Task Parse、Recall、Ranking、Chunking、Selection、Engine、Payload、Expand、Explain、Cache、Export；
- `CacheHub.Cli`：Workspace、Index、Context、Repo、Gateway、Benchmark、Capability 等命令；
- `CacheHub.Desktop`：Minimal API、本地页面及前端调用；
- `integration`：Universal Skill 和接入材料；
- `tests`、GitHub Actions、状态文档和近期提交历史。

### 1.2 限制

本次环境无法将仓库完整克隆到本地运行，因此结论以 GitHub 连接器读取的当前源码、提交历史和 CI 元数据为主。没有把 README 中“测试通过”或提交信息中的自述当作独立验证。动态运行后仍可能发现额外问题；文中标为“确定”的问题均可直接从当前控制流或数据模型推导。

## 2. 总体评分

| 维度 | 评分 | 判断 |
|---|---:|---|
| 产品方向与策划一致性 | 8.5/10 | 命名和模块覆盖广，通用接入方向正确 |
| 工程骨架与可读性 | 7.0/10 | 项目分层、强类型 ID、迁移和注释较完整 |
| 索引可信度 | 3.0/10 | 全量原型存在，但增量/校验/符号持久化和多工作区快照有关键缺陷 |
| Context Engine 核心价值 | 3.0/10 | 结构齐全，但真实召回信号弱、版本/hash/预算不可信 |
| 安全性 | 2.5/10 | 理念完整，但 Local API/XSS/任意文件读取/外发强制等存在 P0 |
| Gateway 可用性 | 2.5/10 | 仅普通非流式代理原型，错误传播与认证不合格 |
| 测试与评测可信度 | 2.0/10 | 单元测试很多，但 E2E/Benchmark 大量模拟，CI 非自动 |
| GUI 开箱即用程度 | 3.5/10 | 页面可用原型，但无索引闭环且存在严重安全问题 |
| 通用性与跨平台 | 4.0/10 | 接口通用，但中文、Linux 路径、各平台 CI 尚不足 |
| 生产发布准备度 | 2.0/10 | 应定位 pre-alpha，不宜作为 beta 或已验证 Token 优化产品 |

**综合评价：4.2/10（pre-alpha，架构先行但核心闭环和安全未达 Beta）。**

## 3. 已经做得较好的部分

- Context Package 采用 Manifest/Payload 分离，并记录 Ranking、Budget、Safety 和版本字段，方向正确。
- 使用强类型 WorkspaceId、IndexSnapshotId、ContextPackageId，降低字符串 ID 混用风险。
- SQLite 使用 WAL、busy timeout、迁移版本表，已有数据库演进意识。
- 代码明确区分 Raw Exact Cache、Semantic Reference、LSP 可选能力和 Agent 无关 Integration Kit。
- Universal Skill 强调仓库内容不可信、先 Context Build、缺内容使用 Expand，符合最新产品定位。
- 安全设计中已经有 Offline/Restricted/Preview、SecretScanner、默认敏感扩展等概念。
- Ranking Profile 已版本化，FeatureScores 和理由解释的数据结构为后续评测提供了基础。
- 编译采用 nullable、warnings as errors、deterministic build，工程治理基础不错。
- 最近提交确实修复了 CLI 参数、路径逃逸、索引 metadata、Context 持久化和缓存边界等真实缺陷，说明项目正在进行实质调试。

## 4. 策划案需求完成度矩阵

| 需求/目标 | 当前状态 | 证据与判断 | 建议阶段 |
|---|---|---|---|
| 核心定位：版本感知 Context Package | 部分完成 | Manifest/Payload、Ranking/预算字段已建立，但版本绑定和实际检索不可信 | R0-R2 |
| 通用 Agent/客户端接入 | 部分完成 | CLI、Local API、Universal Skill 已有；教程与实现不一致，API 不安全 | R3 |
| 本地目录/现有 Git 工作区 | 部分完成 | 可注册和全量构建；无目录校验、重复管理和状态机 | R1 |
| GitHub/Gitee/普通 Git 导入 | 原型 | URL 解析和 clone 命令存在；默认 --no-lfs Bug、无计划/审批/凭据引用 | R3 |
| 增量版本感知索引 | 未完成 | refresh 未实现，Watcher 未接入，verify 错误 | R1 |
| FTS5 与实时搜索职责 | 部分完成 | FTS 可写/查，但未接入 Context Recall，查询编译欠缺 | R1 |
| 多语言确定性结构 | 原型 | C#/TS/Python/Markdown Regex Parser；非 Tree-sitter，且不进入主索引 | R2 |
| 关系置信度 | 部分完成 | 解析模型中有 Heuristic/confidence，但无真实语义图和持久化 | R2/R8 |
| Repo Map | 原型 | 有生成器，但预算参数无效、无目录树、未进入 Context Build | R2 |
| 任务相关候选召回 | 严重不完整 | 真实 CLI 只有路径/文件名/Git diff；符号为空，FTS/关系未接 | R1-R2 |
| 版本化 Ranking Profile | 部分完成 | Profile/version 存在；多特征为 0，归一化名实不符 | R2 |
| Token Budget | 部分完成 | 字段齐全，但 reserves 不参与计算，模型 tokenizer 未真正使用 | R2 |
| Context Expand | 未完成核心目标 | 仅按文件截断；symbol、child package、累计预算和持久化缺失 | R2 |
| Context Explain | 原型 | 展示原因与阈值；没有 feature breakdown 和真实候选来源 | R2 |
| 安全外发控制 | 高风险原型 | Secret scanner 和模式存在，但策略未强制阻止 Payload，审批不存在 | R0 |
| CLI 稳定 JSON 契约 | 部分完成 | 部分命令支持 JSON，错误/选项/输出不统一 | R3 |
| Local Context API | 有原型但不安全 | 主要 endpoint 存在；无认证、任意文件读取、无索引任务 | R0/R3 |
| 文件导出协议 | 部分完成 | 能导出 Manifest/Markdown；RepoMap 名称误导，仓库写入审批不足 | R3 |
| Context/解析/搜索缓存 | 原型 | 多个内存 Cache 类存在，未统一接入和持久化 | R6 |
| 可选模型 Gateway | 高风险原型 | 普通非流式转发可用；状态、认证、流式、限流、缓存安全不足 | R0/R5 |
| Provider/路由/预算 | 占位 | 接口和 DTO 为主，模型列表返回空，路由/预算不执行 | R6 |
| Semantic | 占位 | 内存向量表和接口 | R7 |
| LSP | 占位 | 模型与序列化器，生命周期伪实现 | R8 |
| 最小 GUI | 原型 | 基础页面存在；缺索引闭环并有 XSS/认证风险 | R0/R3 |
| 真实 Benchmark 与阶段门 | 未完成 | 当前完全模拟，不能作为证据 | R4 |
| 跨平台发布 | 未验证 | 仅 Ubuntu 手动 CI；路径语义在 Linux 有缺陷；无安装矩阵 | R0-R5 |
| 插件/团队/企业/更新 | 仅契约模型 | DTO 已预留，未实现运行逻辑 | R9 |

## 5. P0/P1 关键问题摘要

### 5.1 必须立即停止发布并修复的 P0

- **SEC-P0-001 未认证的任意本机文件读取接口**：恶意网页、本机低权限进程或错误暴露的端口可读取工作区外任意可访问文件
- **SEC-P0-002 GUI 存在 DOM/Stored XSS，可与 Local API 组合利用**：恶意仓库文件名、路径或任务文本可执行脚本，进而调用同源 Local API 读取/删除/导出数据
- **SEC-P0-003 符号链接未真正解析且可能逃逸工作区**：FollowSymlinks=true 时可重复遍历、越出根目录、索引外部敏感文件或形成深度循环
- **SEC-P0-004 Secret Scan 只是标记，不会从 Payload 中移除敏感文件**：调用方若未自行检查安全字段，敏感代码仍可被发送；PreviewRequired 也没有真实审批门
- **IDX-P0-001 激活新快照会取消所有其他工作区的 Active 快照**：任一工作区重建索引会使其他所有工作区失去活动索引，Context Build 返回空结果
- **CTX-P0-001 Context Build 使用随机不存在的 SnapshotId**：Manifest 无法复现真实索引版本，缓存键、审计、过期判断全部失真
- **CTX-P0-002 所有文件内容哈希被写成固定 pending 值**：任何文件变化都无法通过 Manifest 判断，旧上下文可能被错误复用
- **CTX-P0-003 真实 CLI/API 流程完全没有符号召回，FTS 也未接入 Recall**：产品最核心的“根据任务找到正确代码”能力远低于 README 和策划目标；中文任务可能返回 0 个候选
- **BENCH-P0-001 Benchmark 结果是模拟的，不是实际实验**：阶段门必然通过，无法证明节省 Token 或任务成功率，可能误导发布和投资决策
- **GW-P0-001 上游错误被改写为 HTTP 200，且错误正文可能进入缓存**：客户端无法正确重试/鉴权，429/401/500 被当成功；错误可能作为正常答案缓存
- **GW-P0-002 Gateway 没有本地访问认证且 Host 可配置为任意地址**：其他本机用户/进程或误绑定网络接口可借用用户 Provider Key 和费用额度

### 5.2 阻止核心目标成立的 P1

- **IDX-P1-001 index refresh 尚未实现**：策划案的增量索引、低延迟更新、跨会话缓存基础尚未成立
- **IDX-P1-002 verify 的绝对/相对路径模型不一致且只比较文件大小**：verify 大概率把所有文件判成新增/删除，并漏掉同大小修改
- **IDX-P1-003 .gitignore 语义实现不完整**：实际索引范围与 Git 不一致，可能索引敏感或巨量生成目录
- **IDX-P1-004 解析器未接入 index build，符号/关系/Outline 没有持久化**：代码结构模块存在但核心流程不使用
- **IDX-P1-005 每个文件多次打开 SQLite 连接且 FTS/metadata 非同事务**：大型仓库性能差；崩溃可能只写 FTS 或只写 metadata；Building 快照残留
- **IDX-P1-006 大文件快速指纹被丢弃并写成 pending**：不同大文件共享相同哈希，缓存/失效不可信
- **CTX-P1-001 选择引擎没有使用 Manifest 声称的 Tokenizer**：预算和 ActualEstimate 可能显著错误，尤其中文、工具消息和不同模型
- **CTX-P1-002 AgentReservedTokens/ResponseReservedTokens 未参与预算公式**：自定义预算可能超过模型窗口
- **CTX-P1-003 Chunks 模式会返回文件的全部分块，不是相关分块**：大文件无法真正压缩；仍可能发送全部内容，违背核心目标
- **CTX-P1-004 Outline 为空时可能选中 0 Token 的幽灵文件**：Manifest 声称选中但 Payload 无内容，解释与实际不一致
- **CTX-P1-005 任务解析仅适合英文**：中文用户与多语言任务无法产生关键词，召回质量极差
- **CTX-P1-006 “九维排序”实际多数维度永久为 0**：README 夸大，排序效果难以验证，权重意义失真
- **CTX-P1-007 RepoMap 不遵守 max token，也没有目录树或优先级**：大型仓库输出可能超预算且结构信息弱
- **CTX-P1-008 --symbol 实际被当作文件路径，且扩展不创建新包**：官方 Universal Skill 的符号扩展会失败；追加上下文无法审计和复现
- **CTX-P1-009 Context Package 读写仍丢失关键字段**：数据库重载后不能复现原构建条件
- **CTX-P1-010 缓存键不完整且缓存未接入核心流程**：命中不可靠或永远没有实际收益
- **SEC-P1-001 Restricted/PreviewRequired/BlockedPatterns/Traversal 等配置没有真正执行**：界面/配置给用户虚假安全感
- **SEC-P1-002 全平台使用 OrdinalIgnoreCase 和 ToLowerInvariant**：Linux 上不同路径发生碰撞、错误越界判断和缓存混淆
- **API-P1-001 Capability 声明与实际能力不一致**：Agent 会调用不存在或不可靠的能力
- **API-P1-002 错误格式不稳定**：Agent 无法可靠恢复，教程难长期兼容
- **API-P1-003 GUI 导入后没有 API 索引任务闭环**：普通用户不使用 CLI 时，导入后 Context Build 通常为空
- **REPO-P1-001 默认传递 git clone 不支持的 --no-lfs**：默认克隆可能直接失败
- **REPO-P1-002 URL/命令输出未脱敏，API Key 通过命令行传入**：Token 可能进入终端历史、进程列表和日志
- **REPO-P1-003 声称有输出限制但实际没有**：恶意/异常 Git 输出可占用大量内存，子进程环境继承敏感变量
- **GW-P1-001 检查和注册不是同一原子操作**：并发相同请求仍可能重复调用云端；第一请求取消会影响共享任务
- **GW-P1-002 请求大小、并发、缓存字节上限没有执行**：本地 DoS、内存爆炸、无法优雅停止
- **GW-P1-003 没有流式转发、Header 保留和真实 Usage**：无法兼容主流 Coding Agent，统计无意义
- **PROV-P1-001 Provider/路由/预算主要是数据模型占位**：README 中多 Provider 与预算能力未完成
- **CI-P1-001 CI 仅 workflow_dispatch，当前提交没有自动状态**：回归可直接进入 main；最近提交历史显示多个关键流程缺陷曾漏过
- **TEST-P1-001 “E2E”大多是内存模拟，不能验证真实 CLI/API/SQLite/Git**：测试数量高但对真实闭环信心不足
- **INSTALL-P1-001 安装脚本在测试失败后继续发布**：用户可能安装已知失败版本，排障困难

## 6. 分模块全面分析

### 6.1 工程治理、文档与 CI

- `Directory.Build.props` 的严格编译设置值得保留，但当前版本号在 Core、能力接口和 HTML 中手工重复，已经发生 0.2.0 与 0.1.0-beta 漂移。
- GitHub Actions 只允许手动触发；主分支没有每次提交的强制状态。对一个近期频繁修复关键集成缺陷的项目，这是高风险配置。
- `AI_DEV_STATE.json` 与 `ROADMAP_STATUS.md` 描述的阶段互相矛盾；README 将 scaffold 和 prototype 写成已支持能力。
- 安装脚本会在测试失败时继续发布，并吞掉构建/测试输出，不符合可诊断和安全发布要求。

### 6.2 Workspace、路径与存储

- Workspace 聚合目前只保存单根路径和状态字符串，尚未形成 Repository/Component/CacheNamespace 的真实聚合及状态转换约束。
- 路径服务没有区分 PhysicalPath 与 VirtualPath，普遍使用 OrdinalIgnoreCase；Linux 文件系统会发生碰撞。
- 符号链接和根目录边界没有统一可信实现；多个 CLI/API 又各自复制 StartsWith 检查，容易产生前缀绕过。
- SQLite 基础可用，但 ContextPackage 缺 FK，快照/文件写入没有批量事务，完整 Manifest 未 round-trip。

### 6.3 Indexer

- 当前 `index build` 更接近“一次性 FTS 导入器”，不是策划中的版本感知增量 Indexer。
- `refresh` 未实现，Watcher 和 Reconciler 也未被 Coordinator 使用。
- Build 不解析代码，Symbol/Relation/Chunk 表没有进入主流程。
- 快照激活 SQL 会跨工作区取消活动快照，这是必须首先修复的数据一致性缺陷。
- FTS5 已有按 snapshot 过滤的基础，但查询结果没有进入 Context Recall。

### 6.4 Parsing 与 Repo Map

- Regex Parser 明确只能提供 syntactic/heuristic 结果，这一定位正确；但 README 不应把它等价为 Tree-sitter 或真实关系图。
- C# parser 对 record、构造器、泛型、表达式体、别名 using 等覆盖有限，CallRegex 会制造噪声。
- RepoMapGenerator 的 token 参数未使用，没有真实目录层级和重要性排序；Export 生成的 repomap 甚至只是 Context 文件列表。

### 6.5 Context Engine

- 数据结构覆盖 Task→Recall→Rank→Select→Manifest→Payload，但真实流程中的召回来源很少。
- CLI/API 读出的 IndexedFileInfo 没有 Symbols，因此 SymbolMatch 实际无法工作。
- Chunking 按固定行窗口并返回全部 chunks，不满足“只发送相关代码块”。
- Tokenizer 只写入 Manifest，不参与 Selection；ActualEstimate 不能当成模型实际输入 Token。
- Context Expand 没有 symbol 解析、父子包、累计预算和持久化。
- 安全扫描不会阻止 Payload，违反“所有出口强制外发策略”。

### 6.6 CLI、Local API 与 Integration Kit

- CLI 命令面很广，适合作为通用接入基础；但 JSON 输出与错误模型不统一，很多命令仍是展示型原型。
- Local API 没有认证，暴露工作区根路径、删除、Payload 和任意文件 Outline。
- Universal Skill 的总体方向正确，但它引用了当前不可用的 symbol expand，需要协议契约测试。
- Capabilities 应动态报告成熟度，而不是只给布尔值。

### 6.7 Desktop GUI

- 基础工作区、Context、历史、搜索和 Integration 页面已存在。
- 导入页面不会触发索引，所以普通用户在 GUI 中无法完成完整闭环。
- 大量 innerHTML 插入不可信字段造成 XSS；结合无认证 API 和任意文件读取构成严重风险。
- 缺少后台任务、审批、错误恢复、Context 发送预览、Provider/Gateway 管理。

### 6.8 Repository Manager

- 使用 ProcessStartInfo.ArgumentList 避免 shell 拼接是正确做法。
- 默认 `--no-lfs` 是无效 Git 参数；URL 凭据未脱敏；没有统一 Clone Plan 审批或目标目录安全边界。
- Pull 使用 ff-only 方向正确，但状态/分叉/未提交文件应先独立检测并给机器可读计划。

### 6.9 Gateway、Provider 与缓存

- Gateway 可转发最简单的非流式 chat/responses 请求，但不能算兼容 Coding Agent 的稳定网关。
- 上游状态固定 200、/models 缺认证、无 streaming、无 local token、无 body/concurrency byte limit。
- Raw Cache 判断过宽，SingleFlight 有竞态，统计 Token 全为 0。
- Provider、Router、Budget、Semantic、LSP 和 Enterprise 当前主要是数据结构/接口占位。

### 6.10 测试与 Benchmark

- 测试文件数量多，单元层覆盖了不少模型和工具函数。
- 所谓 E2E 大量使用手工内存数据，未运行真实 CLI 进程、临时 Git 仓库、Desktop HTTP 或完整 SQLite 工作流。
- Benchmark 直接以 RequiredFiles 作为选中结果，因此不能用于阶段门。
- 需要把测试数量指标替换为核心风险路径、Mutation/coverage 和真实场景通过率。

## 7. 完整问题清单

| ID | 严重度 | 模块 | 问题 | 代码证据 | 影响 | 修复方向 | 阶段 |
|---|---|---|---|---|---|---|---|
| SEC-P0-001 | P0 致命 | Local API | 未认证的任意本机文件读取接口 | src/CacheHub.Desktop/Program.cs：GET /api/v1/outline 直接接受绝对 path 并 File.ReadAllText；API 无认证中间件 | 恶意网页、本机低权限进程或错误暴露的端口可读取工作区外任意可访问文件 | 立即删除任意绝对路径能力；只允许 workspaceId + virtualPath；统一 SafePathResolver；增加随机本地令牌、Origin/Host 校验、loopback 强制绑定 | R0 |
| SEC-P0-002 | P0 致命 | Desktop GUI | GUI 存在 DOM/Stored XSS，可与 Local API 组合利用 | src/CacheHub.Desktop/wwwroot/index.html：工作区名称、路径、任务、文件路径、搜索结果通过 innerHTML/template literal 插入 | 恶意仓库文件名、路径或任务文本可执行脚本，进而调用同源 Local API 读取/删除/导出数据 | 全面移除不可信 innerHTML；使用 textContent/DOM API；增加 CSP；对搜索 snippet 采用受控高亮组件；补充 XSS 夹具测试 | R0 |
| SEC-P0-003 | P0 致命 | 路径安全 | 符号链接未真正解析且可能逃逸工作区 | PathNormalizer 注释声称处理 symlink 但仅 Path.GetFullPath；DirectoryEnumerator.ResolveSymlink 同样只返回 GetFullPath | FollowSymlinks=true 时可重复遍历、越出根目录、索引外部敏感文件或形成深度循环 | 基于 FileSystemInfo.ResolveLinkTarget(true) 获取实际目标；维护真实 inode/目标集合；目标必须再次通过根目录边界校验；默认禁用 | R0 |
| SEC-P0-004 | P0 致命 | 安全外发 | Secret Scan 只是标记，不会从 Payload 中移除敏感文件 | ContextEngine 仅将 SecretsScanPassed=false 写入 Manifest；PayloadGenerator 与 /payload 仍读取并返回 SelectedFiles | 调用方若未自行检查安全字段，敏感代码仍可被发送；PreviewRequired 也没有真实审批门 | 在 Payload 层强制执行安全策略；阻止生成或将文件转为 Metadata；PreviewRequired 必须返回 approvalRequired 并拒绝内容输出 | R0 |
| IDX-P0-001 | P0 致命 | 索引快照 | 激活新快照会取消所有其他工作区的 Active 快照 | IndexCommands.ActivateSnapshotAsync：UPDATE index_snapshots SET status=Superseded WHERE status=Active，缺少 workspace_id 条件 | 任一工作区重建索引会使其他所有工作区失去活动索引，Context Build 返回空结果 | 事务内按 workspace_id 取消旧快照并激活新快照；增加多工作区并发集成测试 | R0 |
| CTX-P0-001 | P0 致命 | Context Package | Context Build 使用随机不存在的 SnapshotId | ContextCommands 与 Desktop /context/build 均 IndexSnapshotId.New()，而候选来自数据库 Active Snapshot | Manifest 无法复现真实索引版本，缓存键、审计、过期判断全部失真 | 查询并绑定实际 Active Snapshot；不存在时返回 INDEX_NOT_READY；Snapshot 必须外键校验 | R0 |
| CTX-P0-002 | P0 致命 | Context Package | 所有文件内容哈希被写成固定 pending 值 | CLI 与 Desktop 构建时 hashProvider => sha256:pending | 任何文件变化都无法通过 Manifest 判断，旧上下文可能被错误复用 | 从 files 表读取真实 hash；在输出 Full/Chunks 前对快速指纹文件补算 SHA-256，并核对读取前后文件状态 | R0 |
| CTX-P0-003 | P0 致命 | 核心召回 | 真实 CLI/API 流程完全没有符号召回，FTS 也未接入 Recall | GetIndexedFilesAsync 强制 Symbols=[]；RecallPipeline 的 keyword 只查路径，FullText/RepoMap/Import/Test/Config 枚举未实现 | 产品最核心的“根据任务找到正确代码”能力远低于 README 和策划目标；中文任务可能返回 0 个候选 | 索引阶段持久化 ParseResult/Symbol；Recall 接入 FTS5、符号表和关系表；无候选时必须有目录/最近文件安全降级 | R1 |
| BENCH-P0-001 | P0 致命 | Benchmark | Benchmark 结果是模拟的，不是实际实验 | BenchmarkCommands.run 直接 selectedFiles=RequiredFiles；report 硬编码 baseline、成功率、Token；EndToEndTests 同样“pretend”选择正确文件 | 阶段门必然通过，无法证明节省 Token 或任务成功率，可能误导发布和投资决策 | 将当前命令改名 demo-metrics 或删除假结果；实现固定仓库、真实 Context Engine、基线 Runner、重复运行、环境重置和结果归因 | R4 |
| GW-P0-001 | P0 致命 | Gateway | 上游错误被改写为 HTTP 200，且错误正文可能进入缓存 | GatewayServer.ForwardToProviderAsync 只返回 body；HandleChatCompletions statusCode 固定 200 | 客户端无法正确重试/鉴权，429/401/500 被当成功；错误可能作为正常答案缓存 | Provider 调用返回 status/headers/body；只缓存 2xx 且结构合法的响应；完整保留 Retry-After、Content-Type、request-id | R0 |
| GW-P0-002 | P0 致命 | Gateway | Gateway 没有本地访问认证且 Host 可配置为任意地址 | GatewayConfig.Host 可改，GatewayServer 未验证 loopback；无访问令牌 | 其他本机用户/进程或误绑定网络接口可借用用户 Provider Key 和费用额度 | 强制 loopback，除非显式危险模式；启动生成本地 bearer token；凭据放系统凭据库；所有请求验证 token | R0 |
| IDX-P1-001 | P1 高 | 增量索引 | index refresh 尚未实现 | IndexCommands.RefreshAsync 明确输出 Not yet implemented | 策划案的增量索引、低延迟更新、跨会话缓存基础尚未成立 | 实现文件状态表、变更队列、增量事务、删除/重命名、失败恢复和快照切换 | R1 |
| IDX-P1-002 | P1 高 | 一致性校验 | verify 的绝对/相对路径模型不一致且只比较文件大小 | ConsistencyReconciler.ScanDisk 返回绝对路径；数据库保存相对路径；CLI 不传忽略规则；同大小变化视为未变 | verify 大概率把所有文件判成新增/删除，并漏掉同大小修改 | 统一 VirtualPath；比较 size+mtime+fingerprint；应用相同 ignore hash；分支切换强制校验 | R1 |
| IDX-P1-003 | P1 高 | 忽略规则 | .gitignore 语义实现不完整 | IgnoreRuleEngine 不支持 !、**、根锚定、转义、最后规则优先；全平台不区分大小写 | 实际索引范围与 Git 不一致，可能索引敏感或巨量生成目录 | 引入经过验证的 gitignore matcher 或完整实现规范；建立官方 Git 测试向量 | R1 |
| IDX-P1-004 | P1 高 | 符号索引 | 解析器未接入 index build，符号/关系/Outline 没有持久化 | IndexCommands 只写 FTS 和 files；无 parser 调用、symbol 表写入 | 代码结构模块存在但核心流程不使用 | 为解析结果增加 symbols/imports/relations 表；按 contentHash+parserVersion 缓存；失败降级文本 | R1 |
| IDX-P1-005 | P1 高 | 原子性/性能 | 每个文件多次打开 SQLite 连接且 FTS/metadata 非同事务 | IndexCommands 每文件 Fts5Index.IndexFileAsync 后再 InsertFileAsync，均独立连接 | 大型仓库性能差；崩溃可能只写 FTS 或只写 metadata；Building 快照残留 | 批量连接+事务；每 N 文件 checkpoint；失败快照清理；只有全部质量门通过才激活 | R1 |
| IDX-P1-006 | P1 高 | 哈希 | 大文件快速指纹被丢弃并写成 pending | IndexCommands 对 !IsFullHash 使用字符串 pending，而非 FileHash.Hash | 不同大文件共享相同哈希，缓存/失效不可信 | 保存 fp: 指纹和 hash_kind；进入 Context Package 时再计算强哈希 | R1 |
| CTX-P1-001 | P1 高 | Token 预算 | 选择引擎没有使用 Manifest 声称的 Tokenizer | ContextEngine 选择 tokenizer 仅用于元数据；Selection/Chunking 始终 chars/4 | 预算和 ActualEstimate 可能显著错误，尤其中文、工具消息和不同模型 | 将 ITokenizer 注入 Selection/Chunking/Payload；消息开销单独计算；记录 measured/estimated/errorMargin | R2 |
| CTX-P1-002 | P1 高 | Token 预算 | AgentReservedTokens/ResponseReservedTokens 未参与预算公式 | TokenBudget.EffectiveBudget=min(target, hardLimit-safety)，忽略两个 reserve 字段 | 自定义预算可能超过模型窗口 | 验证 contextHard + agentReserved + responseReserved <= modelWindow；target 受全局可用预算约束 | R2 |
| CTX-P1-003 | P1 高 | Chunking | Chunks 模式会返回文件的全部分块，不是相关分块 | ChunkingStrategy 按行切完整文件；Selection.ApplyMode 汇总所有 chunks | 大文件无法真正压缩；仍可能发送全部内容，违背核心目标 | 以符号范围、FTS 命中行、Git hunk、错误栈行作为 anchor；邻近扩展并去重；全局预算按 chunk 逐个选择 | R2 |
| CTX-P1-004 | P1 高 | Selection | Outline 为空时可能选中 0 Token 的幽灵文件 | Selection fallback 到 Outline；ChunkOutline 无声明返回空；文件仍被加入 selected | Manifest 声称选中但 Payload 无内容，解释与实际不一致 | 0 chunk 视为失败并降级 Metadata 或排除；SelectedFile 必须含 payload item count/checksum | R2 |
| CTX-P1-005 | P1 高 | 任务解析 | 任务解析仅适合英文 | TaskParser WordRegex 只匹配拉丁词，操作词也仅英文；路径不支持 Windows 反斜杠/空格/连字符 | 中文用户与多语言任务无法产生关键词，召回质量极差 | 增加 Unicode tokenization、中文 n-gram/结巴可选、代码标识符独立抽取、多语言操作词典和测试集 | R2 |
| CTX-P1-006 | P1 高 | Ranking | “九维排序”实际多数维度永久为 0 | Dependency/Test/Config 均 0；TextMatch 查路径；Normalization 声称 minmax 实为 max scaling | README 夸大，排序效果难以验证，权重意义失真 | 只声明已实现特征；完成 FTS/BM25、导入图、测试映射、配置关系后再启用权重；保存 feature provenance | R2 |
| CTX-P1-007 | P1 高 | Repo Map | RepoMap 不遵守 max token，也没有目录树或优先级 | RepoMapGenerator 的 maxTokens 未使用，所有文件直接挂 root，KeySymbols 取前 5 | 大型仓库输出可能超预算且结构信息弱 | 建立真实树、重要性评分、预算裁剪、公共符号优先、稳定排序和版本/hash | R2 |
| CTX-P1-008 | P1 高 | Context Expand | --symbol 实际被当作文件路径，且扩展不创建新包 | CLI targetFile=file ?? symbol；ContextExpander 只有 ExpandByFile；不持久化 parent/delta | 官方 Universal Skill 的符号扩展会失败；追加上下文无法审计和复现 | 实现 symbol->definitions/references；扩展生成 child ContextPackage，记录 ParentPackageId、累计预算、增量哈希和安全扫描 | R2 |
| CTX-P1-009 | P1 高 | 持久化 | Context Package 读写仍丢失关键字段 | ContextPackageRepository 未保存 commit/branch/dirty、ExtractedSymbols/Paths、ParserVersions、RepoMapVersion、ParentPackageId | 数据库重载后不能复现原构建条件 | Schema v2：完整 JSON Manifest 原文+可查询索引列；读写 round-trip property-based test | R1 |
| CTX-P1-010 | P1 高 | Context Cache | 缓存键不完整且缓存未接入核心流程 | ContextPackageCache 内存字典、非线程安全；缺 currentFile/gitDiff/model/tokenizer/engine/chunking 等字段；CLI 未使用 | 命中不可靠或永远没有实际收益 | 以完整 BuildFingerprint 为 key；持久化元数据；加 TTL/size；通过 ContextService 统一使用 | R3 |
| SEC-P1-001 | P1 高 | 安全策略 | Restricted/PreviewRequired/BlockedPatterns/Traversal 等配置没有真正执行 | SecurityPolicyEnforcer：Restricted 与 Standard 无差；PreviewRequired allowed=true；BlockedPatterns 和 EnablePathTraversalCheck 未使用 | 界面/配置给用户虚假安全感 | 建立 PolicyDecision：Allow/Deny/ApprovalRequired；所有输出路径统一调用；规则有 ID、版本、审计事件 | R0 |
| SEC-P1-002 | P1 高 | 路径跨平台 | 全平台使用 OrdinalIgnoreCase 和 ToLowerInvariant | PathNormalizer、Recall、Reconciler、RepoMap 多处固定不区分大小写 | Linux 上不同路径发生碰撞、错误越界判断和缓存混淆 | 按 OS/卷语义选择 comparer；VirtualPath 始终区分大小写，PhysicalPath 使用平台比较器 | R0 |
| API-P1-001 | P1 高 | Local API | Capability 声明与实际能力不一致 | Desktop 声称 Gateway/Cache/Feedback，CLI 列表又不同；Refresh、Symbol Expand 等未被 limitations 表达 | Agent 会调用不存在或不可靠的能力 | 能力由模块健康检查动态生成；每项包含 maturity/endpoint/limitations；契约测试覆盖 Skill | R3 |
| API-P1-002 | P1 高 | Local API/CLI | 错误格式不稳定 | 大量 Console 文本、匿名 {error}，没有统一 ErrorCode/traceId/recoverable；解析失败可能 500 | Agent 无法可靠恢复，教程难长期兼容 | 所有 CLI JSON 和 HTTP 使用 ErrorEnvelope v1；参数校验返回确定错误码；stdout 只机器输出 | R3 |
| API-P1-003 | P1 高 | Local API | GUI 导入后没有 API 索引任务闭环 | Desktop 有 workspace import/context build，但没有 index build/refresh/job endpoint | 普通用户不使用 CLI 时，导入后 Context Build 通常为空 | 增加 /workspaces/{id}/index、jobs、cancel、progress；导入向导可自动排队但不执行仓库代码 | R3 |
| REPO-P1-001 | P1 高 | Git Clone | 默认传递 git clone 不支持的 --no-lfs | GitProcessWrapper.CloneAsync 在 IncludeLfs=false 时 args.Add(--no-lfs) | 默认克隆可能直接失败 | 用环境变量 GIT_LFS_SKIP_SMUDGE=1；先检测 git-lfs；通过集成测试验证 | R3 |
| REPO-P1-002 | P1 高 | 凭据 | URL/命令输出未脱敏，API Key 通过命令行传入 | Repo inspect 打印 OriginalUrl；Gateway start 示例/参数含 --provider-key | Token 可能进入终端历史、进程列表和日志 | 只接受 credential ref/环境变量/系统凭据库；URL 移除 userinfo；统一 Redactor | R0 |
| REPO-P1-003 | P1 高 | Git 进程 | 声称有输出限制但实际没有 | GitProcessWrapper StringBuilder 无上限；timeout Task 未取消；无环境清理 | 恶意/异常 Git 输出可占用大量内存，子进程环境继承敏感变量 | 流式限长、超限终止；最小环境白名单；取消后 Kill+Wait；记录截断状态 | R3 |
| GW-P1-001 | P1 高 | SingleFlight | 检查和注册不是同一原子操作 | SingleFlight 先锁查询，释放后调用 factory，再锁写入 | 并发相同请求仍可能重复调用云端；第一请求取消会影响共享任务 | ConcurrentDictionary<string, Lazy<Task<Result>>> GetOrAdd；共享任务独立于单调用取消 | R5 |
| GW-P1-002 | P1 高 | Gateway | 请求大小、并发、缓存字节上限没有执行 | MaxRequestSizeBytes 未使用；fire-and-forget HandleRequest；缓存只限制 10k 条不限制 bytes | 本地 DoS、内存爆炸、无法优雅停止 | ASP.NET Core/Kestrel 独立进程；body limit、concurrency semaphore、byte LRU、shutdown tracking | R5 |
| GW-P1-003 | P1 高 | Gateway | 没有流式转发、Header 保留和真实 Usage | stream 请求被判不可缓存但仍通过 ReadAsString 非流式；模型/Token 永远为 model/0；/models 无认证 | 无法兼容主流 Coding Agent，统计无意义 | 实现 SSE 流透传、断连取消、header allowlist、usage parser、endpoint/provider-aware hashing | R5 |
| PROV-P1-001 | P1 高 | Provider | Provider/路由/预算主要是数据模型占位 | ListModelsAsync 读取后直接 return []；Stream 被忽略；RoutingConfig/BudgetLimit 无执行器 | README 中多 Provider 与预算能力未完成 | 拆成 Optional Providers 项目；先完成一个严格 OpenAI-compatible provider，再做 router/health/budget | R6 |
| CI-P1-001 | P1 高 | CI | CI 仅 workflow_dispatch，当前提交没有自动状态 | ci.yml 不监听 push/pull_request；只 Ubuntu；无覆盖率、安全/打包/端到端 | 回归可直接进入 main；最近提交历史显示多个关键流程缺陷曾漏过 | 启用 PR/push CI；Ubuntu+Windows+macOS；format/build/unit/integration/security/package；保护 main | R0 |
| TEST-P1-001 | P1 高 | 测试 | “E2E”大多是内存模拟，不能验证真实 CLI/API/SQLite/Git | EndToEndTests 手工构造 indexedFiles；Benchmark 测试 pretend；单一 Tests 项目引用所有模块 | 测试数量高但对真实闭环信心不足 | 拆 Unit/Integration/Contract/Security/E2E；进程级运行 CLI/Desktop；临时 Git 仓库与 SQLite；测试真实 JSON 契约 | R0-R4 |
| INSTALL-P1-001 | P1 高 | 安装 | 安装脚本在测试失败后继续发布 | install.ps1 输出 Warning 并继续；构建/测试输出被完全吞掉 | 用户可能安装已知失败版本，排障困难 | 默认测试失败即终止；提供 --skip-tests 明确危险开关；保留日志；按 RID 发布并校验文件 | R0 |
| ARCH-P2-001 | P2 中 | 架构 | Core 承载 Gateway/Provider/Semantic/LSP/Ecosystem/Benchmark | src/CacheHub.Core 包含大量可选和运行时模块 | 职责与变化半径过大，违背“可选模块独立关闭” | 保留 Core.Contracts/Domain；将 Gateway、Providers、Semantic、LSP、Benchmarks、Ecosystem 拆成独立项目/进程 | R6 |
| ARCH-P2-002 | P2 中 | 依赖 | Indexing 直接依赖 Storage，Context 再传递依赖 Storage | CacheHub.Indexing.csproj 引用 Storage；Context 引用 Indexing | 领域算法难独立测试/替换，插件边界模糊 | 抽象 IIndexStore/IFileCatalog 在 contracts；存储适配器在 composition root 注入 | R1 |
| PARSE-P2-001 | P2 中 | 解析器 | Regex Parser 覆盖有限且存在错误分类 | CSharp parser 的 record 被识别为 Other；call regex 会匹配声明/control；方法/泛型/构造器覆盖不足 | Outline 和启发式关系噪声高 | 明确标注 regex-baseline；接入 Tree-sitter；建立每语言 fixture corpus；关系附 confidence/source | R2 |
| WATCH-P2-001 | P2 中 | Watcher | FileWatcher 未接入索引任务且 Rename 丢旧路径 | 仅有类，无应用服务使用；Renamed 只保存新路径；队列无上限 | 不会产生增量更新；重命名可能残留旧记录 | Watcher 只作为加速信号接入 IndexCoordinator；保存 old/new；队列溢出触发 reconcile | R1 |
| FTS-P2-001 | P2 中 | FTS5 | 自然语言查询未转义且 Recall 不使用 FTS | Fts5Index 直接 MATCH $query；Context Recall 未调用 FTS | 特殊符号可抛语法异常；搜索能力与核心选择脱节 | 实现 QueryCompiler，支持 literal/prefix；返回 BM25/命中行；RecallSource.FullText 接入 | R1 |
| WORK-P2-001 | P2 中 | Workspace | 导入不校验目录、重复根路径或状态迁移 | Workspace.Create 仅 GetFullPath；API/CLI 直接 Insert；状态 enum 无状态机方法 | 无效路径/重复工作区、状态漂移 | WorkspaceService 验证存在/可读、RootPathHash 唯一、状态转换规则和审计 | R1 |
| DATA-P2-001 | P2 中 | 数据库 | context_packages 缺少工作区/快照外键 | Migration0003 只有 TEXT 字段和普通索引 | 孤儿 Context Package 可以长期存在，随机 SnapshotId 不会被发现 | Schema v2 增加 FK 或显式 snapshot history retention；迁移前清理孤儿 | R1 |
| CACHE-P2-001 | P2 中 | 缓存 | 缓存多为内存模型，未形成跨会话统一缓存 | LruCache、ContextPackageCache、InMemoryVectorStore 各自独立；未持久化/未统一依赖 hash | 与产品“本地跨会话缓存”目标有差距 | 实现 CacheStore 接口和 SQLite/blob 后端；按类型独立配额；来源/命中原因可解释 | R6 |
| SEM-P2-001 | P2 中 | Semantic | Semantic 仅接口和内存向量表 | 无 Embedding Provider、持久化、失效、维度校验或安全策略 | 不能视为已完成能力 | 保持 capability=false；核心评测通过后再做 Optional Semantic | R7 |
| LSP-P2-001 | P2 中 | LSP | LSP 生命周期是伪实现 | Initialize 直接 State=Ready；无进程、framing reader、请求关联、sandbox | 能力模型存在但实际不可用；未来若误开启会虚假 Ready | 保持 capability=false；独立高风险模块，先做审批/沙箱/进程监管，再做协议 | R8 |
| ECO-P2-001 | P2 中 | 生态/企业 | 插件、团队、企业、更新仅为 DTO | EcosystemModels.cs 无签名验证、加载器、策略执行、同步或 updater | 文件存在不等于功能完成 | 标记 future contract only；不得在 README 宣称；等 1.0 核心稳定后实施 | R9 |
| DOC-P2-001 | P2 中 | 文档治理 | 状态文档互相矛盾且 README 过度声明 | AI_DEV_STATE 称 ENHANCED 全门通过；ROADMAP_STATUS 仍称 P01/后续未开始；README 声称增量、9 维、SSE、真实场景 | 开发 AI 和用户会基于错误状态继续开发或误判成熟度 | 从单一 roadmap YAML 生成状态和 README feature matrix；功能分 Implemented/Experimental/Scaffold/Planned | R0 |
| VERSION-P2-001 | P2 中 | 版本 | 版本号在多个位置漂移 | Directory.Build.props 0.2.0；HTML 显示 0.1.0-beta；文档/能力手写 0.2.0 | 客户端缓存/兼容判断不可靠 | 由 AssemblyInformationalVersion 单一来源生成 API/UI/CLI 版本 | R0 |
| GUI-P2-001 | P2 中 | GUI | GUI 没有索引任务、审批、进度和错误恢复闭环 | 导入仅注册，Context Build 假定已有索引；Export endpoint 仍写 placeholder repomap | 不符合“无需命令行开箱即用” | 在安全修复后加入 Index Job、状态页、审批中心、Context Preview、回滚与清理 | R3 |
| DETECT-P2-001 | P2 中 | 项目检测 | 探测逻辑过度简化且可能全树扫描 | DotNet 一律 Framework=ASP.NET Core；Node DetectLanguage 使用 AllDirectories；无权限/ignore/文件数限制 | 误报、性能问题、Monorepo 不准确 | 基于触发文件解析 JSON/XML/TOML；按组件边界扫描；证据和 confidence；未知框架不猜 | R3 |
| CONFIG-P2-001 | P2 中 | 配置 | 配置没有 Schema 校验、权限加固或凭据引用 | ConfigManager 直接反序列化；Gateway key 仍 CLI 参数；无文件权限设置 | 无效配置可能导致危险 host/预算；凭据泄漏 | JSON Schema+版本迁移+原子 fsync；系统凭据库；Unix 0600/Windows ACL | R0-R5 |
| FEEDBACK-P2-001 | P2 中 | 反馈 | CLI --id 与反馈 JSON 内 ContextPackageId 不校验 | FeedbackAsync 保存 parsed feedback，但输出使用命令行 ctxId | 反馈可能错误归属，污染评测数据 | 强制一致；验证 Context 存在和 Workspace；记录 client/version/model/run id | R3 |
| EXPORT-P2-001 | P2 中 | 导出 | ExportToRepository 自动修改 .gitignore，没有审批对象 | 方法注释说 opt-in，但内部直接 AppendAllText | .gitignore 属于用户仓库，违反默认不修改项目 | 拆 Plan/Apply；默认只输出建议；用户审批后原子修改并备份 | R3 |
| PAYLOAD-P2-001 | P2 中 | Payload | Payload 与 Manifest 可能不一致且缺少完整校验字段 | PayloadGenerator 重新分块，使用 ContextTarget 作为每文件预算；不按 Manifest ranges 统一生成；PayloadItem 无 hash/token/reason | Manifest 预算与实际 Payload Token 可不同，难复现 | Selection 直接产生 immutable payload plan；Payload 只物化计划；每项含 hash/range/tokens/encoding/checksum | R2 |
| PERF-P2-001 | P2 中 | 性能 | 多处同步/全量读取与无上限操作 | Desktop File.ReadAllText；Node AllDirectories；Directory/FTS 大内容；API 无文件大小限制 | 大型仓库卡顿、内存高、请求阻塞 | 异步流式、文件上限、分页、批量 DB、后台任务和取消 | R1-R3 |

## 8. 建议目标架构

```text
CacheHub.Contracts / Core.Domain
        │
        ├── CacheHub.Storage.Sqlite
        ├── CacheHub.Indexing
        │      ├── Scan / Ignore / Hash
        │      ├── Parser Runtime / Tree-sitter
        │      └── Index Coordinator / Snapshot
        ├── CacheHub.Context
        │      ├── Task Parse / Recall / Ranking
        │      ├── Chunk Selection / Budget
        │      └── Context Package / Feedback
        ├── CacheHub.Application
        │      ├── WorkspaceService
        │      ├── IndexJobService
        │      ├── ContextService
        │      └── Approval/SecurityService
        ├── CacheHub.Cli
        ├── CacheHub.LocalApi + Desktop UI
        └── Optional processes/modules
               ├── Gateway
               ├── Providers
               ├── Semantic
               ├── LSP
               └── Enterprise/Plugins
```

关键原则：Core 不再承载 Gateway/Semantic/LSP 的实现；CLI 与 GUI 只调用 Application Service；所有文件读取都经过 WorkspaceFileAccess + SecurityDecision；Context Build 只接受可信 Active IndexSnapshot；Manifest 与 Payload 共享同一不可变 PayloadPlan。

## 9. 完整改进路线图

### R0 止血、事实校准与安全基线

- **建议周期：** 1-2 周
- **目标：** 在继续开发功能前，消除数据泄露、跨工作区破坏和虚假阶段门。
- **进入条件：** 冻结 main 功能开发；建立修复分支；备份现有数据库。

#### 开发任务

- R0-W001 将产品状态统一标记为 pre-alpha/experimental，README 建立 Implemented/Experimental/Scaffold/Planned 矩阵。
- R0-W002 删除或封锁任意绝对路径 Outline；实现 workspace-scoped SafePathResolver，含 symlink 与平台大小写测试。
- R0-W003 Local API 强制 loopback + 随机访问令牌 + Origin/Host 校验；所有危险 endpoint 做认证。
- R0-W004 前端移除 innerHTML 注入，增加 CSP 与恶意文件名/FTS snippet XSS 测试。
- R0-W005 修复 snapshot 激活 SQL，增加两工作区交叉回归测试。
- R0-W006 Context Build 绑定真实 Active Snapshot 和真实 hash；不存在活动索引时失败。
- R0-W007 Gateway 在修复前默认 capability=false；修复状态码、只缓存合法 2xx、loopback/token/body limit。
- R0-W008 安全策略改为 Allow/Deny/ApprovalRequired，并在 Payload 出口强制执行。
- R0-W009 CI 改为 PR/push 自动执行；install.ps1 测试失败默认中止。
- R0-W010 将模拟 Benchmark 明确标记 demo，不允许产生 phase gate passed。

#### 退出验收

- [ ] 所有 P0 测试通过
- [ ] Local API 无工作区外读取能力
- [ ] 两个工作区索引互不影响
- [ ] Context Manifest 的 snapshot/hash 可在数据库中验证
- [ ] main 分支必须有自动 CI 状态

### R1 索引与持久化可信化

- **建议周期：** 3-5 周
- **目标：** 使索引、快照、增量和持久化成为可复现的真实数据基础。
- **进入条件：** R0 全部通过。

#### 开发任务

- R1-W001 定义 PhysicalPath/VirtualPath/PathComparer，完成 Windows/Linux/macOS 路径夹具。
- R1-W002 Schema v2：文件 mtime、hash_kind、parser_version；symbols/imports/relations；完整 Context Manifest JSON；FK。
- R1-W003 重写 index build 为单连接批量事务与工作区原子快照切换。
- R1-W004 实现 refresh：created/modified/deleted/renamed，按依赖失效 Parser/FTS/RepoMap/Context Cache。
- R1-W005 FileWatcher 接入 Coordinator；队列溢出、程序离线、branch switch 触发一致性对账。
- R1-W006 采用正确 gitignore matcher，索引与 reconcile 共用同一 IgnoreSnapshot。
- R1-W007 修复 reconcile 为相对 VirtualPath + size/mtime/fingerprint；增加 same-size mutation 测试。
- R1-W008 Parser 结果进入索引；失败降级文本并记录诊断，不阻塞整个快照。
- R1-W009 FTS QueryCompiler、BM25、命中行；Context Recall 正式使用 FullText。
- R1-W010 Context Repository 实现完整 round-trip 与 schema migration/rollback tests。

#### 退出验收

- [ ] 10k 文件仓库全量与增量测试通过
- [ ] 单文件修改仅更新必要记录
- [ ] 所有 Manifest 字段读写等价
- [ ] 重启/中断后旧 Active Snapshot 仍可用

### R2 Context Engine 正确性 0.2

- **建议周期：** 4-7 周
- **目标：** 让核心上下文选择真正小、准、可解释。
- **进入条件：** R1 索引可信。

#### 开发任务

- R2-W001 Unicode/中文任务解析，代码标识符、错误栈、路径独立通道。
- R2-W002 Recall 实现 FTS、Symbol、Git hunk、RepoMap、Import、Test、Config，并保存 source evidence。
- R2-W003 Ranking Profile 验证权重/归一化；只启用已实现特征；添加 efficiency/size 信号。
- R2-W004 Tree-sitter 接入 C#/TypeScript/Python，Regex 仅作 fallback；关系标注 syntactic/heuristic。
- R2-W005 语法/命中锚点 Chunking，只选择相关 chunks，不再全文件切块全部发送。
- R2-W006 Token Budget 使用真实 ITokenizer，校验完整窗口、reserved、margin；记录误差。
- R2-W007 Selection 产出 immutable PayloadPlan；Manifest/Payload 使用同一计划。
- R2-W008 RepoMap 实现真实目录树、重要性评分和严格预算。
- R2-W009 Context Expand 支持 file/symbol，生成 child package、累计预算和安全审计。
- R2-W010 Context Explain 保存完整 FeatureBreakdown、排除代码和潜在遗漏。

#### 退出验收

- [ ] 中文与英文任务均有稳定候选
- [ ] Payload 实际 Token 不超过硬限制
- [ ] 相同 snapshot/task/profile 输出确定
- [ ] Context Package stale 检测可用

### R3 通用协议、Repository 与安全 GUI Beta

- **建议周期：** 3-5 周
- **目标：** 提供真正无需特定 Agent 适配、无需命令行的安全接入闭环。
- **进入条件：** R2 核心稳定。

#### 开发任务

- R3-W001 CLI/API 统一 ErrorEnvelope、Capability maturity 和 JSON Schema。
- R3-W002 Local API 增加 index/job/cancel/progress、context build/payload/expand/feedback 的契约测试。
- R3-W003 Universal Skill 由契约测试驱动；教程示例运行 integration verify。
- R3-W004 GUI 实现导入→索引→Context→预览→导出闭环与审批中心。
- R3-W005 Git clone/pull 建立 Plan/Approve/Execute；修复 LFS、子模块、credential redaction。
- R3-W006 Project Detection 做组件化/Monorepo，未知信息不猜测。
- R3-W007 File Export 只写应用数据目录；仓库内写入必须审批、备份、原子更新。
- R3-W008 Feedback 校验 Context/Workspace，并记录客户端/模型/实际读取/追加读取。

#### 退出验收

- [ ] 新用户只用 GUI 可完成首个 Context Package
- [ ] 任意客户端可用 CLI/HTTP/文件协议接入
- [ ] Universal Skill 中每条命令均有自动契约测试

### R4 真实 Benchmark 与核心阶段门

- **建议周期：** 3-6 周
- **目标：** 用真实任务证明 Context Engine 的价值，而不是模拟指标。
- **进入条件：** R3 接口稳定。

#### 开发任务

- R4-W001 固定至少 20 个任务、5 类仓库、包含中文/Monorepo；Commit 可复现。
- R4-W002 Ground Truth 区分 Required/Helpful/Distractor，并双人审核。
- R4-W003 Baseline Runner 与 CacheHub Runner 使用相同 Agent/模型/权限/环境。
- R4-W004 每任务至少 3 次，重置工作区、对话、缓存和构建产物。
- R4-W005 记录完整生命周期 Token、额外读取、测试、补丁、失败归因和方差。
- R4-W006 阶段门：Recall@10、Missing Context、Test Pass、Token Reduction；结果签名并公开。

#### 退出验收

- [ ] 不存在硬编码 metrics
- [ ] 真实任务报告可复现
- [ ] 达到门槛后才能对外宣传 Token 优化

### R5 独立 Gateway 0.3

- **建议周期：** 4-7 周
- **目标：** 在不污染 Context Core 的前提下提供兼容、安全、可观测的 API 网关。
- **进入条件：** R4 核心价值通过或 Gateway 作为独立实验。

#### 开发任务

- R5-W001 从 Core 拆出 CacheHub.Gateway 进程，Kestrel/ASP.NET Core。
- R5-W002 OpenAI chat/responses/models 与 SSE 流式透传、断连取消。
- R5-W003 本地认证、loopback、请求/并发/响应上限、header allowlist。
- R5-W004 Provider 状态/headers/errors/usage 完整映射。
- R5-W005 Raw Exact Cache 只缓存确定安全的合法 2xx；key 含 endpoint/provider/config。
- R5-W006 原子 SingleFlight、byte LRU、TTL、持久化可选、准确统计。
- R5-W007 凭据系统库和本地审计；日志不记录请求正文/Authorization。

#### 退出验收

- [ ] 主流 API 客户端流式测试通过
- [ ] 401/429/500 正确传播
- [ ] 工具请求永不重放
- [ ] Gateway 关闭不影响 Context 功能

### R6 缓存与 Provider 模块

- **建议周期：** 3-5 周
- **目标：** 形成跨会话缓存和一个真实 Provider 基线。
- **进入条件：** R1-R5 对应契约稳定。

#### 开发任务

- 统一 CacheStore/BlobStore，按类型配额、依赖 hash、producer version、命中原因。
- Context/Parse/Search/RepoMap 缓存接入实际服务并做失效测试。
- 完成 OpenAI-compatible Provider 模型列表、usage、错误与 streaming。
- 再实现健康检查、显式/失败转移路由与预算执行。

#### 退出验收

- [ ] 重启后缓存可复用
- [ ] 过期数据不会命中
- [ ] Provider 预算实际阻止超限请求

### R7 Semantic Reference

- **建议周期：** 3-5 周
- **目标：** 只作为参考召回，不直接复用旧编程答案。
- **进入条件：** 真实 Benchmark 已稳定。

#### 开发任务

- 本地 Embedding provider 与模型版本管理。
- 持久向量索引、workspace 隔离、删除和失效。
- 历史任务/错误/Context/Feedback 召回并标记 semantic-reference。
- 对核心指标做增益/回归 A/B。

#### 退出验收

- [ ] 默认 Balanced 只作参考
- [ ] 无跨工作区泄漏
- [ ] 效果不降级再启用

### R8 LSP 高风险可选模块

- **建议周期：** 5-8 周
- **目标：** 在明确授权与沙箱中增加精确定义/引用。
- **进入条件：** Tree-sitter 基线成熟。

#### 开发任务

- 独立进程与审批模型。
- JSON-RPC framed reader、request correlation、cancel、notifications。
- 最小环境、工作目录和资源限制。
- C#/TS/Python 逐个接入并标注 semantic confidence。

#### 退出验收

- [ ] 关闭 LSP 时核心完全正常
- [ ] Crash 不破坏索引
- [ ] 未授权不启动/恢复依赖

### R9 跨平台发布与生态

- **建议周期：** 长期
- **目标：** 在核心稳定后实现安装、更新、插件、团队与企业能力。
- **进入条件：** 1.0 核心质量门通过。

#### 开发任务

- Windows/macOS/Linux 安装包和签名。
- 自动更新备份/回滚。
- 插件签名、权限和隔离。
- 团队共享索引、企业策略、内网部署和审计。

#### 退出验收

- [ ] 各平台真实安装升级测试
- [ ] 插件不能越权
- [ ] 企业策略在所有出口强制执行

## 10. 修复优先级与依赖

```text
R0 安全/事实/数据止血
  ↓
R1 索引与持久化可信
  ↓
R2 Context Engine 正确
  ↓
R3 通用协议与安全 GUI
  ↓
R4 真实 Benchmark 阶段门
  ├── R5 独立 Gateway
  ├── R6 缓存/Provider
  ├── R7 Semantic
  ├── R8 LSP
  └── R9 跨平台/生态
```

R0～R4 是产品核心链路，建议串行或小范围并行；R5～R9 必须保持可选模块，不得反向成为核心强依赖。

## 11. 新的质量门与 Definition of Done

- [ ] 任何“Implemented”能力必须有至少一个进程级/HTTP 级契约测试，不以文件或接口存在作为完成。
- [ ] 所有文件出口必须经过同一 SecurityDecision；PreviewRequired 没有审批 ID 时不得输出内容。
- [ ] Context Package 必须绑定数据库中存在的 Active Snapshot，所有 SelectedFile hash 可核验。
- [ ] Manifest 序列化→数据库→读取必须逐字段等价。
- [ ] Context Payload 实际 Token 必须 <= hard limit，并报告 tokenizer 与估算误差。
- [ ] 增量索引需证明同一未变化文件不会重新解析；同大小内容变化必须被发现。
- [ ] CLI JSON stdout 不含日志；失败返回稳定 errorCode、recoverable、suggestedAction。
- [ ] Local API 只能 loopback 且需要本地令牌；任意绝对路径参数禁止。
- [ ] GUI 不得将不可信数据注入 innerHTML；必须通过 XSS 安全夹具。
- [ ] Benchmark 不允许硬编码成功/Token/RequiredFiles；每个任务有固定 Commit 和环境重置。
- [ ] CI 必须在 PR/push 自动运行，main 受保护，三平台至少完成 build/unit。
- [ ] 文档能力状态由机器可读 Roadmap/Capability 测试生成，避免再次漂移。

## 12. 建议测试矩阵

| 测试层 | 必测内容 |
|---|---|
| Unit | Path comparer、Ignore 语义、Hasher、Task Parser、Ranking、Budget、Secret patterns、Cache keys |
| Storage Integration | 迁移 1→最新、崩溃回滚、并发读写、Manifest round-trip、FK、WAL |
| Indexer Integration | 全量/增量/删除/改名/同大小修改/分支切换/Watcher overflow/多工作区 |
| Context Integration | 真实 DB+FTS+symbols→Recall→Payload；中英文任务；stale snapshot；hard budget |
| Security | symlink escape、路径前缀、大小写、XSS、任意文件、Origin/Host、secret payload、API auth |
| CLI Contract | 每条 Universal Skill 命令、JSON schema、stderr/stdout、error codes、exit code |
| Local API Contract | auth、body limits、workspace scope、jobs、payload/approval、rate/concurrency |
| Gateway | streaming、cancel、401/429/500、tools、cache/no-cache、singleflight、large body、headers |
| Real E2E | 安装→导入→索引→Context→Agent 使用→反馈→增量→卸载 |
| Benchmark | 固定仓库/Commit、三次以上、完整 Token、测试结果、失败归因和方差 |

## 13. 推荐的近期 20 个执行任务

1. R0-W001 将产品状态统一标记为 pre-alpha/experimental，README 建立 Implemented/Experimental/Scaffold/Planned 矩阵。
2. R0-W002 删除或封锁任意绝对路径 Outline；实现 workspace-scoped SafePathResolver，含 symlink 与平台大小写测试。
3. R0-W003 Local API 强制 loopback + 随机访问令牌 + Origin/Host 校验；所有危险 endpoint 做认证。
4. R0-W004 前端移除 innerHTML 注入，增加 CSP 与恶意文件名/FTS snippet XSS 测试。
5. R0-W005 修复 snapshot 激活 SQL，增加两工作区交叉回归测试。
6. R0-W006 Context Build 绑定真实 Active Snapshot 和真实 hash；不存在活动索引时失败。
7. R0-W007 Gateway 在修复前默认 capability=false；修复状态码、只缓存合法 2xx、loopback/token/body limit。
8. R0-W008 安全策略改为 Allow/Deny/ApprovalRequired，并在 Payload 出口强制执行。
9. R0-W009 CI 改为 PR/push 自动执行；install.ps1 测试失败默认中止。
10. R0-W010 将模拟 Benchmark 明确标记 demo，不允许产生 phase gate passed。
11. R1-W001 定义 PhysicalPath/VirtualPath/PathComparer，完成 Windows/Linux/macOS 路径夹具。
12. R1-W002 Schema v2：文件 mtime、hash_kind、parser_version；symbols/imports/relations；完整 Context Manifest JSON；FK。
13. R1-W003 重写 index build 为单连接批量事务与工作区原子快照切换。
14. R1-W004 实现 refresh：created/modified/deleted/renamed，按依赖失效 Parser/FTS/RepoMap/Context Cache。
15. R1-W005 FileWatcher 接入 Coordinator；队列溢出、程序离线、branch switch 触发一致性对账。
16. R1-W006 采用正确 gitignore matcher，索引与 reconcile 共用同一 IgnoreSnapshot。
17. R1-W007 修复 reconcile 为相对 VirtualPath + size/mtime/fingerprint；增加 same-size mutation 测试。
18. R1-W008 Parser 结果进入索引；失败降级文本并记录诊断，不阻塞整个快照。
19. R1-W009 FTS QueryCompiler、BM25、命中行；Context Recall 正式使用 FullText。
20. R1-W010 Context Repository 实现完整 round-trip 与 schema migration/rollback tests。

## 14. 发布建议

- 当前版本建议标记为 `0.2.0-prealpha`，不要使用 Beta。
- 在 R0 完成前，不建议让用户运行 Desktop 或 Gateway 处理敏感代码/API Key。
- 在 R4 真实阶段门通过前，不应公开声称“已减少多少 Token”或“Recall 达到多少”。
- 所有未来功能继续保留在 Roadmap，但能力发现必须区分 `implemented`、`experimental`、`scaffold`、`planned`。

## 15. 最终结论

CacheHub 已经完成了一个覆盖面很广的工程原型，证明产品架构可以落地；但当前代码更偏“根据策划快速铺设所有模块”，缺少对核心链路的纵向收敛。最正确的下一步不是继续增加模块，而是：

> **先把 工作区版本 → 可信增量索引 → 真实召回 → 严格预算 Payload → 通用接口 → 真实 Benchmark 这一条链路做成可验证、可安全发布的产品。**

完成 R0～R4 后，再继续 Gateway、Provider、Semantic、LSP 和企业生态，原有功能无需删除，也不会因为模块拆分而丢失。

## 附录 A：关键源码证据索引

- `src/CacheHub.Cli/Commands/IndexCommands.cs`：全量索引、refresh 占位、全局快照取消、pending hash
- `src/CacheHub.Cli/Commands/ContextCommands.cs`：随机 Snapshot、Symbols=[]、固定 hash、symbol expand 错误
- `src/CacheHub.Desktop/Program.cs`：无认证 Local API、任意 Outline 路径、随机 Snapshot
- `src/CacheHub.Desktop/wwwroot/index.html`：不可信 innerHTML 和 GUI 工作流
- `src/CacheHub.Context/Engine/ContextEngine.cs`：Tokenizer/安全/Manifest 编排
- `src/CacheHub.Context/Recall/RecallPipeline.cs`：实际召回来源
- `src/CacheHub.Context/Ranking/RankingEngine.cs`：未实现特征和归一化
- `src/CacheHub.Context/Chunking/ChunkingStrategy.cs`：行窗口与 chars/4
- `src/CacheHub.Context/Selection/SelectionEngine.cs`：预算选择和幽灵文件
- `src/CacheHub.Context/Payload/PayloadGenerator.cs`：Manifest/Payload 再分块不一致
- `src/CacheHub.Storage/Repositories/ContextPackageRepository.cs`：Manifest 持久化字段缺失
- `src/CacheHub.Indexing/Reconciliation/ConsistencyReconciler.cs`：绝对/相对路径和 size-only
- `src/CacheHub.Indexing/Scanning/DirectoryEnumerator.cs`：symlink 伪解析
- `src/CacheHub.Indexing/IgnoreRules/IgnoreRuleEngine.cs`：简化 gitignore
- `src/CacheHub.Core/Gateway/Server/GatewayServer.cs`：状态固定 200、无认证/流式/限制
- `src/CacheHub.Core/Gateway/GatewayModels.cs`：SingleFlight 竞态和缓存安全
- `src/CacheHub.Core/Repository/GitProcessWrapper.cs`：--no-lfs、无输出限制、URL 模型
- `src/CacheHub.Cli/Commands/BenchmarkCommands.cs`：模拟 benchmark
- `tests/CacheHub.Tests/EndToEndTests.cs`：内存模拟 E2E
- `.github/workflows/ci.yml`：仅手动 CI

## 附录 B：审计判断说明

本文把“存在接口/记录类型/页面”与“功能已经完成”严格分开。只有当功能具有真实入口、真实数据流、失败行为、版本失效、安全边界和自动测试时，才判定为完成。Semantic、LSP、Provider 路由、企业能力等虽然已有代码文件，但目前被评为 scaffold，而非缺失或删除；它们仍完整保留在后续阶段。