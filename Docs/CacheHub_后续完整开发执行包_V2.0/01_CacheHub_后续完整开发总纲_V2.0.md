# CacheHub / AI_KV 后续完整开发执行总纲 V2.0

**文档性质：** 面向开发 AI 的工程实施手册 + 技术架构细化 + 任务路线图 + 验收规范 + 发布门禁  
**审计基线：** `chenfengyimei/CacheHub` main @ `ca0896ec5e39acaf670af614fc9e3328d5fbe524`  
**策划基线：** 《AI_KV 完整项目开发总策划案 V3.0》  
**版本日期：** 2026-08-07  
**任务总数：** 88 个阶段任务（R4-R15）

> 本文件从仓库当前已完成的 R0-R3 继续开发，不重复推倒重来。目标是把现有 pre-alpha 原型逐步发展为：**本地代码知识库 + 精准上下文压缩 + 版本安全精确缓存 + 可选语义参考 + 云端模型 API 回源 + 通用 Agent 接入**。

---

# 0. 开发包使用方法

本开发包不是普通建议文档。开发 AI 必须按以下方式执行：

1. 先阅读本总纲、`ROADMAP.yaml`、`WORK_ITEMS.yaml` 和仓库 `Docs/ai/AI_DEV_STATE.json`。
2. 核验仓库当前 commit 与本包基线；若已变化，先生成差异报告，不得直接假设任务仍未完成。
3. 一次只领取一个 Work Item。禁止同时跨多个阶段大范围修改。
4. 每个任务开始前记录基线构建和测试结果；结束后提供相同证据。
5. 每个任务独立提交 Git，提交后继续执行下一任务，不得因提交中断开发。
6. 所有公共协议、数据库迁移和安全边界变更必须先写 ADR 或 SPEC。
7. “类、接口、DTO 已存在”不等于功能完成；必须有真实调用链、真实数据和测试证据。
8. 不得用模拟 Benchmark、硬编码成功或 Ground Truth 直接替代算法结果。
9. 无法完成时必须把状态标记为 `blocked`，说明原因、证据和恢复入口。
10. 未达到阶段 Gate 时禁止宣称阶段完成。

---

# 1. 当前仓库真实基线

## 1.1 已具备

- SQLite 工作区、索引快照、文件、FTS5、符号、导入和关系数据表。
- 全量 Index Build、实验性 Refresh、Context Package Manifest/Payload。
- 确定性任务解析、排序、预算、分块、Expand、Feedback。
- CLI、Local API、基础 Web GUI 和 Integration Kit。
- 实验性 OpenAI-compatible Gateway、Raw Exact 内存缓存和单 Provider 回源。
- Semantic、Provider Router、LSP 和企业模块的部分接口或骨架。
- 最新提交说明已有约 440 项测试，但当前 GitHub commit 没有独立 CI 状态检查记录。

## 1.2 当前最关键断层

1. 索引已经保存 FTS、符号、导入和关系，但真实 `context build` 未完整注入 FTS/Symbol 查询。
2. Anchor 分块能力存在，但 Selection 主流程没有把 Anchor 传入 Chunker。
3. Tokenizer ID 会被记录，但实际选择仍大量依赖粗略字符估算。
4. Refresh 修改单文件时清空整个 Snapshot 的 FTS，可能让知识库只剩最后修改文件。
5. Context Package Cache 和模型 Raw Exact Cache 仍主要为进程内存，不支持可靠跨会话。
6. Gateway 与 Context Engine 是两条独立链，不能自动形成“压缩后回源”。
7. Semantic 只有接口和内存向量存储，不是可用语义缓存。
8. Provider Fallback、SSE、真实 Usage、持久化缓存和真实 Benchmark 尚未闭环。

## 1.3 当前允许宣传的能力

> 版本感知本地索引、Context Package 基础链路，以及实验性的 Raw Exact Cache + 单云端 Provider 回源原型。

## 1.4 当前禁止宣传的能力

- 已完成本地语义缓存。
- 已证明降低 Token。
- 已实现生产级多 Provider 回源。
- 已实现精准代码语义知识库。
- 已达到正式 Beta 或生产安全标准。

---

# 2. 最终目标架构

```text
用户任务 / Agent
        │
        ├─ Context API / CLI / Skill
        │
        ▼
版本感知代码知识库
Files + FTS + Symbols + Imports + Relations + RepoMap
        │
        ▼
多源召回 + 可解释排序 + Anchor 精准分块
        │
        ▼
Context Package Exact Cache
        │
        ▼
最小 Context Package / Payload
        │
        ├─ Agent 自己构造 Prompt
        └─ 可选 Prompt Assembly Service
                    │
                    ▼
              CacheHub Gateway
        Raw Exact Cache / SingleFlight
                    │
          命中 ─────┴───── 未命中
           │                 │
       本地返回         Provider Router
                         主 Provider
                         Retry/Fallback
                              │
                         云端模型 API

可选 Semantic Reference：
相似任务 / 错误 / Context Package
→ 只作为召回辅助信号
→ 默认不直接重放旧代码答案
```

# 3. 四种产品运行模式

| 模式 | 调用链 | 价值 | 限制 |
| --- | --- | --- | --- |
| Context Only | Agent → Context API → Payload | 真正减少发送的项目上下文 | Agent 必须主动接入 |
| Gateway Only | 客户端 → Exact Cache → Provider | 相同请求不回源、统一统计 | 不能自动删除已构造 Prompt |
| Unified | Context → Prompt → Gateway → Provider | 完整目标体验 | 需要显式 workspace/task 元数据 |
| Semantic Reference | 确定性召回 + 相似历史参考 | 改善模糊任务召回 | 默认不直接返回旧答案 |

---

# 4. 开发 AI 强制执行协议

## 4.1 每个任务的标准循环

```text
读取状态
→ 校验依赖
→ 建立基线
→ 写/更新测试
→ 最小实现
→ 静态检查
→ 单元测试
→ 集成测试
→ 安全/性能检查
→ 更新文档和状态
→ 独立 Git 提交
→ 继续下一任务
```

## 4.2 每个任务必须产生的证据

- 修改文件清单。
- 设计理由和取舍。
- 构建命令与结果。
- 测试命令、通过/失败/跳过数量。
- 新增测试名称。
- 数据库迁移版本（如有）。
- API/Schema 兼容说明（如有）。
- 性能对比（性能任务）。
- 安全检查（安全边界任务）。
- Git commit SHA。
- 未完成项和后续风险。

## 4.3 禁止行为

- 不读现有实现就重写整个模块。
- 为了测试通过删除或放宽测试。
- 用 Mock 替代本应验证的真实 SQLite/HTTP/文件系统主链。
- 把未接入主流程的接口标记为 Implemented。
- 修改公共 Schema 而不更新兼容测试。
- 直接修改已发布数据库迁移；必须新增迁移。
- 在缓存键中遗漏模型、Provider、工具、版本或工作区状态。
- 缓存或重放含副作用工具调用。
- 在日志、命令行参数或数据库明文保存 Provider Key。
- 在阶段 Gate 未通过时更新状态为 Complete。
- 因一次失败执行 reset、clean、强制覆盖用户文件或重写 Git 历史。

## 4.4 Git 提交策略

- 一个 Work Item 至少一个独立提交。
- 较大任务可拆为 `test`、`implementation`、`docs` 多个提交，但不可混入其他任务。
- 每完成一个稳定节点自动提交并推送；提交后继续开发。
- 提交格式：

```text
feat(r4): R4-W003 wire FTS recall into context build
fix(r6): R6-W001 implement per-file FTS upsert/delete
test(r12): R12-W005 add real patch-and-test benchmark runner
docs(state): update R4 gate evidence
```

---

# 5. 模块边界

| 模块 | 应负责 | 不应负责 |
| --- | --- | --- |
| Core | 值对象、契约、错误、版本 | Gateway Server、Semantic 存储、GUI |
| Storage | SQLite、迁移、仓储、事务 | 排序策略、Provider 重试 |
| Indexing | 扫描、哈希、解析、FTS、快照 | 调用模型、构造 Prompt |
| Context | 任务解析、召回、排序、预算、PayloadPlan | 执行代码修改、持有 API Key |
| LocalApi | 公共应用服务映射、认证 | 复制业务 SQL 和算法 |
| Gateway | 模型协议、缓存、Provider 路由 | 猜测工作区、读取额外源码 |
| Integration | Skill、教程、示例、验证 | 特定 Agent 私有逻辑进入 Core |
| Semantic | Embedding、向量、相似参考 | 默认直接重放旧答案 |
| Desktop | 展示和用户操作 | 直接读写数据库 |
| Benchmarks | 环境、运行器、指标、证据 | 模拟成功或硬编码 Ground Truth |

---

# 6. 后续开发阶段总览

| 阶段 | 方向 | 首要目的 | 依赖 |
| --- | --- | --- | --- |
| R4 | 知识召回主链闭环 | 让已经写入 SQLite/FTS5/符号/导入/关系表的数据真正参与 Context Build，解决“数据库里有知识、实际构建却不用知识”的核心断层。 | R0-R3 已完成, 数据库迁移 1-8 可用 |
| R5 | 精准上下文压缩与 Token 预算 | 把“选中文件”升级为“选中真正相关的代码范围”，确保 Manifest、Payload 和 Token 预算使用同一不可变计划。 | R4 召回证据和 LineAnchor 可用 |
| R6 | 增量索引、快照与失效正确性 | 修复当前 refresh 清空整个 FTS 快照等问题，使工作区变化后索引与缓存能够准确、原子地更新。 | R4 查询服务稳定, R5 PayloadPlan 版本字段冻结 |
| R7 | 持久化缓存与版本安全精确缓存 | 把当前进程内 Dictionary 原型升级为可持久化、可失效、可审计的多层缓存。 | R6 快照和失效语义稳定 |
| R8 | 生产级 Gateway 与云端回源 | 完成 OpenAI-compatible 请求转发、SSE、状态码、认证、Provider 路由、真实 Usage 和安全回源。 | R7 Raw Exact Cache 可用 |
| R9 | 上下文与模型调用统一工作流 | 把 Context Engine 与 Gateway 从两条独立链连接成可由 Agent 主动调用的通用工作流，同时保持模块可独立使用。 | R5 Context Package 稳定, R8 Gateway 稳定 |
| R10 | 语义参考缓存与本地向量检索 | 增加相似任务、错误和历史 Context Package 的辅助召回；默认不把语义相似旧答案直接重放。 | R4-R6 确定性召回可作为基线, R11 Benchmark 框架至少可执行检索评测 |
| R11 | 安全策略与出口强制执行 | 确保所有代码读取、Payload 导出、统一工作流和 Gateway 都遵守同一安全策略。 | 可与 R4-R10 并行，但发布门必须在其后 |
| R12 | 真实 Benchmark、阶段门与质量闭环 | 用真实 Context Engine、真实 Agent/客户端、真实补丁和测试证明产品是否达到 Token 优化目标。 | R4-R6 至少完成, R9 统一工作流可选 |
| R13 | GUI、安装、发布与运维体验 | 让普通用户无需命令行完成导入、索引、上下文构建、Gateway 配置、统计、诊断和升级。 | 核心功能 API 稳定, R11 安全门通过 |
| R14 | Tree-sitter、Roslyn、LSP 与语言智能 | 在确定性基础链稳定后提高多语言语法结构和精确定义引用能力。 | R12 评测框架可衡量增益 |
| R15 | 长期生态与企业模块 | 在本地单机产品稳定后扩展团队、内网、插件和策略管理，且不能削弱本地安全基线。 | 1.0 核心发布质量 |

---

# R4：知识召回主链闭环

## 目的

让已经写入 SQLite/FTS5/符号/导入/关系表的数据真正参与 Context Build，解决“数据库里有知识、实际构建却不用知识”的核心断层。

## 前置条件

- R0-R3 已完成
- 数据库迁移 1-8 可用
- 全量索引与 Context Package 基础管线可运行

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R4-W001 | 建立可组合召回源接口 | 将路径、FTS、符号、Git Diff、关系、Repo Map 拆成可测试的 IRecallSource；统一输出 RecallHit、ScoreHint、LineAnchor 和 Evidence。 |
| R4-W002 | 建立索引查询应用服务 | 新增 IIndexQueryService，集中查询 Active Snapshot、Files、Symbols、Imports、Relations、FTS；CLI 与 Desktop 不再各写一套 SQL。 |
| R4-W003 | 接入 FTS5 正文召回 | Context Build 对任务关键词执行快照绑定的 FTS 查询；保存 BM25、snippet、命中行和查询词，禁止退化成只查路径。 |
| R4-W004 | 接入符号召回 | 对 ExtractedSymbols 查询 file_symbols；精确匹配优先于 LIKE；把定义行范围写入 Anchor。 |
| R4-W005 | 接入 Import/Relation 扩展 | 从直接命中文件向导入者、被调用对象和相邻模块扩展一层；所有启发式关系必须携带置信度和来源。 |
| R4-W006 | 实现测试与配置关系召回 | 建立源文件↔测试文件、组件↔配置文件的确定性规则；只作为辅助信号，不得覆盖显式路径与符号。 |
| R4-W007 | 接入 Repo Map 候选 | Repo Map 作为低成本结构上下文和邻接召回源；记录 repo_map_version，缓存与索引快照绑定。 |
| R4-W008 | 统一 CLI、Local API 与测试 | CLI/Desktop/API 调用相同 ContextApplicationService；Capability 声明只发布真实可用能力；增加契约与真实 SQLite 测试。 |

## 阶段技术方向

### 推荐接口

```csharp
public interface IRecallSource
{
    string Id { get; }
    string Version { get; }
    Task<IReadOnlyList<RecallHit>> RecallAsync(
        RecallQuery query,
        CancellationToken cancellationToken);
}

public sealed record RecallHit(
    string VirtualPath,
    RecallSourceKind Source,
    double SourceScore,
    IReadOnlyList<LineAnchor> Anchors,
    IReadOnlyList<RecallEvidence> Evidence);
```

`ContextCommands` 和 `Desktop/Program.cs` 不再直接拼 SQL。两者只调用 `IContextApplicationService.BuildAsync()`，应用服务负责加载 Active Snapshot、调用 `IIndexQueryService`、创建 Recall Sources 并运行 Context Engine。

FTS 结果至少返回：VirtualPath、BM25、Snippet、MatchedTerm、StartLine/EndLine。Symbol 查询优先级：完全限定名精确匹配 > 名称精确匹配 > 前缀 > 模糊包含。

## 阶段验收 Gate

- [ ] CLI 和 Local API 对同一输入返回相同的候选集合、排序和 Manifest。
- [ ] 模糊任务不依赖显式文件路径，也能通过 FTS 或符号命中目标文件。
- [ ] SelectedFile.Reasons 可追踪到实际数据库证据。
- [ ] FTS、符号、关系任一模块关闭时能够明确降级，不得静默伪装为成功。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R5：精准上下文压缩与 Token 预算

## 目的

把“选中文件”升级为“选中真正相关的代码范围”，确保 Manifest、Payload 和 Token 预算使用同一不可变计划。

## 前置条件

- R4 召回证据和 LineAnchor 可用

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R5-W001 | 统一 LineAnchor 数据模型 | 定义 Symbol、FTS、ErrorStack、GitDiff、UserRange 等 Anchor 类型，记录行号、来源、置信度和证据。 |
| R5-W002 | Anchor 贯穿召回到分块 | CandidateFile、RankedCandidate、PayloadPlan 全程携带 Anchors；SelectionEngine 必须把 Anchors 传给 ChunkingStrategy。 |
| R5-W003 | 实现语法范围合并与裁剪 | 优先使用符号起止行；FTS 命中使用上下文窗口；相邻/重叠范围稳定合并；禁止默认从文件头截取。 |
| R5-W004 | 建立真实 Tokenizer 抽象 | 新增 IModelTokenizer、TokenizerRegistry 和模型映射；粗估仅作为显式 fallback，并在 Manifest 标记 estimated=true。 |
| R5-W005 | 重写预算分配器 | 严格计算模型窗口、系统提示、工具定义、历史、输出预留和安全余量；ContextTarget 与 HardLimit 必须在构建前验证。 |
| R5-W006 | Manifest/Payload 共用 PayloadPlan | 选择完成后生成不可变 PayloadPlan；Manifest 和 Payload 都从同一计划投影，防止二次分块产生差异。 |
| R5-W007 | Context Expand 形成 Revision | 按文件/符号扩展生成新的 Context Package，保存 ParentPackageId、Revision、增量 Token、追加原因和新哈希。 |
| R5-W008 | 压缩质量回归测试 | 加入大文件、多个同名符号、中文任务、错误栈和跨文件任务；验证范围、Token 和 Payload 一致性。 |

## 阶段技术方向

### PayloadPlan 是唯一事实来源

```csharp
public sealed record PayloadPlan(
    string WorkspaceId,
    string SnapshotId,
    string TokenizerId,
    string TokenizerVersion,
    int HardLimit,
    IReadOnlyList<PayloadPlanItem> Items,
    string PlanHash);
```

禁止 Manifest 选一次、Payload 再按另一套逻辑重新分块。`PayloadGenerator` 只能读取 `PayloadPlan` 中的 Mode、Ranges 和 Hash。文件内容哈希与计划不一致时必须拒绝或重新构建。

Tokenizer 选择失败时使用 RoughTokenizer，但 Manifest 必须标记 `isEstimated=true`；GUI 不得把估算显示成 Provider 实际 Token。

## 阶段验收 Gate

- [ ] Chunks 模式必须优先围绕 Anchor，不得无理由从第 1 行开始。
- [ ] Payload 实际估算不得超过 ContextHardLimit。
- [ ] Manifest 记录的范围与导出的代码逐行一致。
- [ ] 同一快照、任务和策略版本重复构建结果稳定。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R6：增量索引、快照与失效正确性

## 目的

修复当前 refresh 清空整个 FTS 快照等问题，使工作区变化后索引与缓存能够准确、原子地更新。

## 前置条件

- R4 查询服务稳定
- R5 PayloadPlan 版本字段冻结

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R6-W001 | 实现 FTS 单文件删除与 Upsert | 禁止修改一个文件时 ClearSnapshot；为 FTS 建立 rowid/path 映射，支持按 snapshot+path 删除和重建。 |
| R6-W002 | 采用不可变增量快照 | Active Snapshot 不原地修改；创建 Building Snapshot，复制未变化行、重建变化行，全部成功后原子激活。 |
| R6-W003 | 统一忽略规则 | Build、Refresh、Verify、Watcher 使用同一个 IgnoreRuleSet；实现必要的 gitignore 语义并版本化规则哈希。 |
| R6-W004 | 强化路径与符号链接边界 | 平台感知大小写；解析最终真实路径；拒绝工作区外 symlink；防止循环、路径前缀碰撞和 UNC 逃逸。 |
| R6-W005 | Watcher 转后台任务 | FileSystemWatcher 只负责入队；防抖、合并、溢出后触发全量对账；任务可取消、恢复并记录状态。 |
| R6-W006 | 分支切换与 Dirty State | 记录 commit、branch、dirty_state_hash；检测 checkout/reset/rebase 等大变化并强制构建新快照。 |
| R6-W007 | 建立精确失效图 | 文件变化只失效依赖该文件的 Parse/Search/RepoMap/Context 缓存；策略版本变化触发对应层级失效。 |
| R6-W008 | 大仓库与故障注入测试 | 覆盖 1k/10k/100k 文件、进程中断、磁盘满、数据库锁、监听丢事件和同大小内容变化。 |

## 阶段技术方向

### 推荐增量快照流程

```text
读取 Active Snapshot A
→ 扫描和对账
→ 创建 Building Snapshot B
→ INSERT SELECT 复制未变化 Files/Symbols/Relations
→ 对变化文件重新 Hash/Parse/FTS
→ 不复制已删除文件
→ 校验 B 的行数、哈希和 FTS
→ 单事务：A=Superseded，B=Active
```

不要在 Active Snapshot 上做半完成更新。FTS 必须支持 `DeleteFile(snapshotId, path)` 与 `UpsertFile(...)`，禁止 `ClearSnapshot` 处理单文件修改。

## 阶段验收 Gate

- [ ] 修改任意单文件后，未变化文件仍可被 FTS 搜索。
- [ ] Building Snapshot 失败时旧 Active Snapshot 保持完整可用。
- [ ] Verify 与 Build 使用完全一致的忽略范围。
- [ ] 工作区内容变化后，不允许复用旧 Context Package 或模型响应缓存。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R7：持久化缓存与版本安全精确缓存

## 目的

把当前进程内 Dictionary 原型升级为可持久化、可失效、可审计的多层缓存。

## 前置条件

- R6 快照和失效语义稳定

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R7-W001 | 缓存数据库与内容寻址存储 | 新增 cache_entries、cache_dependencies、cache_stats；大响应按内容哈希保存到文件仓库，SQLite 保存元数据。 |
| R7-W002 | 持久化 Context Package Cache | 缓存键包含任务、快照、Ranking、Chunking、Budget、Tokenizer、Security、RepoMap 和显式文件。 |
| R7-W003 | Parse/Search/RepoMap Cache | 解析缓存绑定 content_hash+parser；搜索缓存绑定 snapshot+query+source versions；RepoMap 绑定快照和预算。 |
| R7-W004 | Raw Exact Model Cache | 保存真实 status、必要 headers、body、usage、provider、model、TTL；只缓存完整成功响应。 |
| R7-W005 | 严格缓存安全分类 | 有 tools/functions、流式、实时信息、高随机性、无版本元数据或用户 no-cache 时默认禁用。 |
| R7-W006 | 淘汰、压缩、修复与并发 | 实现 LRU/TTL/空间上限、原子写入、校验哈希、损坏隔离和并发事务。 |
| R7-W007 | 缓存可解释统计 | 区分本地计算复用、实际未回源 Token、Provider cached input 和估算节省；不得混为一个数字。 |

## 阶段技术方向

### 模型响应精确缓存键

```text
SHA256(
  protocol_endpoint
  + provider_id
  + provider_base_url
  + model
  + exact_request_bytes
  + system_prompt_hash
  + tool_schema_hash
  + response_format_hash
  + context_package_id
  + index_snapshot_id
  + dirty_state_hash
  + cache_policy_version
)
```

对于普通聊天客户端没有工作区元数据的请求，可使用 Raw Exact Cache；对于代码任务，没有 Snapshot/Context 元数据时应采用更保守 TTL，并明确显示“未绑定代码版本”。

## 阶段验收 Gate

- [ ] 进程重启后安全缓存仍可命中。
- [ ] 文件或策略变化后依赖缓存准确失效。
- [ ] 任何失败、部分流式或含工具调用的响应不得进入可重放缓存。
- [ ] 缓存损坏可自动隔离并回源，不影响主流程。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R8：生产级 Gateway 与云端回源

## 目的

完成 OpenAI-compatible 请求转发、SSE、状态码、认证、Provider 路由、真实 Usage 和安全回源。

## 前置条件

- R7 Raw Exact Cache 可用

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R8-W001 | 拆分独立 Gateway 项目 | 从 Core 移出 Server、Provider、缓存运行实现；建立 CacheHub.Gateway 进程，Core 只保留契约。 |
| R8-W002 | 使用 ASP.NET Core 流式管线 | 采用 ResponseHeadersRead 转发，限制请求大小，支持取消和背压；避免 HttpListener 原型长期扩展。 |
| R8-W003 | 实现 SSE 透明转发 | 逐帧转发 data/event/id/retry；客户端断开后取消上游；流式默认不进入响应缓存。 |
| R8-W004 | 保持真实状态码与响应头 | SingleFlight 必须共享 status+headers+body；401/429/5xx 不得改写为 200；过滤 hop-by-hop headers。 |
| R8-W005 | 本地认证与令牌生命周期 | 随机令牌可轮换、保存在受限权限文件或系统凭据库；GUI/CLI 自动配置；日志永不输出 Provider Key。 |
| R8-W006 | Provider Adapter 与模型发现 | 实现 OpenAI-compatible 基线；/v1/models 携带认证；模型能力、上下文窗口和 Tokenizer 可发现。 |
| R8-W007 | Provider Router 与 Fallback | 支持 Explicit、Fallback、健康检查、429/5xx 重试、超时和熔断；禁止对非幂等工具调用自动跨 Provider 重放。 |
| R8-W008 | 解析真实 Usage 与费用 | 解析 chat/responses 的 usage；流式从最终事件提取；实际值与估算值分开，价格表版本化。 |
| R8-W009 | Gateway 协议与压力测试 | 覆盖非流式、SSE、取消、慢上游、429、401、5xx、超大请求、并发 SingleFlight 和缓存重启。 |

## 阶段技术方向

### Gateway 转发原则

- 使用 `HttpCompletionOption.ResponseHeadersRead`。
- 请求取消必须传播到 Provider。
- 返回真实 HTTP 状态码和 Content-Type。
- 只转发安全响应头，移除 Connection、Transfer-Encoding 等 hop-by-hop headers。
- SingleFlight 保存完整 `ProviderTransportResponse`，不能只保存 Body。
- 重试仅针对连接失败、超时、429 和部分 5xx，并遵守 Retry-After。
- 工具调用请求不得跨 Provider 自动重放，除非客户端显式允许。

## 阶段验收 Gate

- [ ] OpenAI-compatible 非流式与 SSE 客户端均可使用。
- [ ] 上游状态码、错误体和关键响应头准确传递。
- [ ] 缓存未命中能够安全回源，命中时完全不调用 Provider。
- [ ] Gateway 关闭或故障不影响 Context Engine、CLI 和索引。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R9：上下文与模型调用统一工作流

## 目的

把 Context Engine 与 Gateway 从两条独立链连接成可由 Agent 主动调用的通用工作流，同时保持模块可独立使用。

## 前置条件

- R5 Context Package 稳定
- R8 Gateway 稳定

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R9-W001 | 定义 Contextual Completion 协议 | 请求显式包含 workspace_id、task、model、budget 和安全模式；服务构建 Context Package 后再调用 Gateway。 |
| R9-W002 | 定义 Gateway 元数据契约 | 支持 context_package_id、snapshot_id、dirty_state_hash、client_id；无元数据时按普通 Gateway 请求处理。 |
| R9-W003 | 实现 Prompt Assembly Service | 只拼装明确模板和 Context Payload，不猜测 Agent 私有提示；允许客户端选择 Manifest-only、Payload 或自动请求。 |
| R9-W004 | 禁止隐式工作区猜测 | 路径无法唯一映射时返回 WORKSPACE_NOT_UNIQUE；不得使用最近工作区或任意默认项目。 |
| R9-W005 | Integration Kit V2 | 更新 Universal Skill、CLI/API 教程、Base URL 教程、自动配置、备份、验证和回滚。 |
| R9-W006 | 反馈与全生命周期统计 | 关联 Context Build、Expand、Gateway 请求、额外文件读取、补丁和测试结果；统计整个任务 Token。 |
| R9-W007 | 通用客户端示例 | 提供 Shell、C#、TypeScript、Python 和一个最小 Agent 示例；示例只依赖公共协议。 |

## 阶段技术方向

### 统一工作流不应破坏通用性

建议新增可选接口：

```text
POST /api/v1/workflows/contextual-completion
```

请求中必须显式包含 WorkspaceId、Task、Model 和 ProviderPolicy。该接口是便捷编排器，不取代独立 Context API 和 Gateway，也不允许从普通 `/v1/chat/completions` 中猜测工作区。

## 阶段验收 Gate

- [ ] 一个自研最小客户端可从任务到云端模型完成全链路。
- [ ] 仅 Gateway、仅 Context、统一工作流三种模式均可独立运行。
- [ ] 任何工作区绑定都由显式元数据或唯一映射产生。
- [ ] 通用 Skill 不包含对 Codex、Claude Code 等私有实现的强依赖。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R10：语义参考缓存与本地向量检索

## 目的

增加相似任务、错误和历史 Context Package 的辅助召回；默认不把语义相似旧答案直接重放。

## 前置条件

- R4-R6 确定性召回可作为基线
- R11 Benchmark 框架至少可执行检索评测

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R10-W001 | 冻结语义模式与安全边界 | 定义 Off、Reference、StrictExperimental；默认 Reference，只增加候选或提示，不直接返回历史模型回答。 |
| R10-W002 | 实现可插拔 Embedding Provider | 支持本地模型优先和可选远程模型；记录 provider、model、dimensions、version、normalization。 |
| R10-W003 | 持久化向量存储 | 初期可用 SQLite BLOB+线性检索；接口允许后续 HNSW；所有向量绑定 workspace、snapshot 和 content hash。 |
| R10-W004 | 建立语义对象生命周期 | 为 Task、Error、ContextPackage、Feedback 建立向量；源码变化后按依赖失效或降权。 |
| R10-W005 | 语义召回接入 Ranking | Semantic 只作为独立特征，设置上限，不能压过显式路径、精确符号和 Git Diff。 |
| R10-W006 | 可解释性与隐私 | 显示相似来源、相似度、版本和是否跨工作区；默认禁止跨工作区语义共享。 |
| R10-W007 | 语义增益实验 | 比较启用/关闭语义后的 Recall、Precision、任务成功率和 Token；无正向收益时保持默认关闭。 |

## 阶段技术方向

### Semantic Reference，而不是默认 Semantic Response Cache

向量对象保存：

```text
workspace_id
snapshot_id
object_type
object_id
content_hash
embedding_provider
embedding_model
embedding_version
dimensions
vector
created_at
```

相似历史答案只能作为“参考上下文”进入 Payload，必须标注来源和旧版本。只有未来 StrictExperimental 模式在完全相同 Snapshot、工具、安全策略和确定性参数下，才允许探索直接响应复用。

## 阶段验收 Gate

- [ ] 关闭 Semantic 时核心功能和结果不受影响。
- [ ] 语义命中不得绕过工作区版本和安全策略。
- [ ] 默认模式不直接返回旧模型回答。
- [ ] 只有真实评测证明有收益后才在 GUI 中推荐开启。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R11：安全策略与出口强制执行

## 目的

确保所有代码读取、Payload 导出、统一工作流和 Gateway 都遵守同一安全策略。

## 前置条件

- 可与 R4-R10 并行，但发布门必须在其后

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R11-W001 | 集中式 SecurityApplicationService | CLI、API、Payload、Export、Gateway 元数据统一调用；禁止各模块自行解释策略。 |
| R11-W002 | 出口强制检查 | PayloadGenerator、Markdown/File Export、Contextual Completion 在发送/导出前强制扫描；未传 Enforcer 不再等于允许。 |
| R11-W003 | Local API 与 GUI 加固 | Bearer Token、Host 校验、CSP、SameSite、禁止未转义 innerHTML、路径只接受 workspace-relative。 |
| R11-W004 | 不可信仓库内容标记 | README、AGENTS.md、注释和检索文本在 Context Payload 中标记为 untrusted data，不能覆盖系统安全规则。 |
| R11-W005 | 秘密扫描与外发审计 | 扩展规则、允许自定义正则；审计只记录元数据和范围，默认不保存完整敏感正文。 |
| R11-W006 | 审批与计划/执行分离 | 导出受限文件、访问工作区外路径、执行仓库脚本和 Git 写操作必须有 ApprovalId。 |
| R11-W007 | 攻击面测试 | 覆盖路径穿越、symlink、DNS rebinding、XSS、恶意文件名、提示注入、超大文件和缓存投毒。 |

## 阶段技术方向

### 出口统一

所有输出源码的路径必须经过：

```text
Resolve Workspace-relative Path
→ Symlink/realpath containment
→ Ignore/Sensitive rules
→ Secret scan
→ External-send policy
→ Approval decision
→ Audit metadata
→ Payload/Export/Provider
```

禁止通过 `securityEnforcer = null` 绕过出口检查。没有策略时使用默认安全策略，而不是默认允许。

## 阶段验收 Gate

- [ ] 任何外发出口都无法在没有安全决定的情况下输出源码。
- [ ] 本地恶意网页或仓库内容不能调用 API 读取任意文件。
- [ ] Offline 模式下网络层无法发送工作区内容。
- [ ] 安全测试失败时禁止发布。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R12：真实 Benchmark、阶段门与质量闭环

## 目的

用真实 Context Engine、真实 Agent/客户端、真实补丁和测试证明产品是否达到 Token 优化目标。

## 前置条件

- R4-R6 至少完成
- R9 统一工作流可选

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R12-W001 | 真实仓库夹具与重置器 | 每个任务固定 commit/dirty state；运行前清理构建产物和缓存；支持本地镜像避免网络不稳定。 |
| R12-W002 | Ground Truth 管理 | 标注 Required、Helpful、Distractor、Symbols、Tests；保存标注来源和复核人。 |
| R12-W003 | 基线 Runner | 让 Agent 使用原生搜索/读取工具，记录每次读文件、Token、轮数、补丁和测试。 |
| R12-W004 | CacheHub Runner | 强制在 Prompt 前调用 Context Build，允许 Expand，记录 Context Package 和实际额外读取。 |
| R12-W005 | 真实任务执行与测试 | 任务成功必须由补丁应用和项目测试验证，不能硬编码 success 或直接使用 Ground Truth 作为选择结果。 |
| R12-W006 | 重复运行与统计 | 每任务多次运行，报告均值、方差、成功率、最坏结果；模型、参数和权限固定。 |
| R12-W007 | 阶段门与失败归因 | 计算 Recall@K、Precision、Missing/Stale、Test Pass、全生命周期 Token；失败分为检索、压缩、Agent、模型和环境。 |
| R12-W008 | 持续回归与公开报告 | 每次 Ranking/Chunking/Tokenizer 变更运行核心集合；报告带 commit、配置和原始证据。 |

## 阶段技术方向

### 真实 Benchmark 单次运行

```text
恢复固定 commit
→ 清理未跟踪文件和构建产物
→ 清空本轮缓存
→ 启动日志采集
→ 执行基线或 CacheHub 工作流
→ 应用补丁
→ 运行项目指定测试
→ 保存工具调用、文件读取、Token、轮数
→ 归因失败
→ 恢复环境
```

不得使用开发者已知答案直接喂给 Context Engine。RequiredFiles 只能用于计算指标，不能参与选择。

## 阶段验收 Gate

- [ ] Benchmark 不再包含模拟成功、硬编码 Token 或 Ground Truth 直接选中。
- [ ] Relevant File Recall@10 ≥ 90% 或给出明确未达标报告。
- [ ] Test Pass Rate 不低于基线 95%，平均输入 Token 降低目标 ≥20%。
- [ ] 任何阶段门调整必须通过 ADR 记录，不能因结果不好静默降低标准。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R13：GUI、安装、发布与运维体验

## 目的

让普通用户无需命令行完成导入、索引、上下文构建、Gateway 配置、统计、诊断和升级。

## 前置条件

- 核心功能 API 稳定
- R11 安全门通过

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R13-W001 | GUI 核心工作流 | 导入/Clone→索引→Context Build→查看原因→配置客户端→验证；每步显示真实状态和失败恢复。 |
| R13-W002 | 后台任务中心 | 索引、Clone、Benchmark、缓存清理可取消、重试和恢复；应用重启后状态一致。 |
| R13-W003 | Gateway 与 Provider 页面 | 配置 Base URL、凭据引用、模型、Fallback、缓存策略；密钥不进入日志和数据库明文。 |
| R13-W004 | 可解释统计面板 | 分别展示 Context 压缩、Raw Exact 命中、Provider cached input、本地解析复用和估算费用。 |
| R13-W005 | 安装与自动接入 | Windows 首发安装器；检测 .NET/单文件包；生成 Agent 接入教程和验证命令，不直接破坏第三方配置。 |
| R13-W006 | 升级、迁移与回滚 | 数据库备份、Schema 迁移、旧缓存重建、版本降级说明和安装失败回滚。 |
| R13-W007 | 诊断包与隐私 | 导出脱敏日志、版本、配置摘要、任务状态；默认不包含源码、Prompt 和 Key。 |
| R13-W008 | 跨平台与发布矩阵 | Windows→macOS→Linux；验证路径大小写、权限、symlink、单文件发布和服务生命周期。 |

## 阶段技术方向

### GUI 只调用应用服务

Desktop 不能继续复制 CLI 的 SQL、索引和 Context 逻辑。建议引入本地后台 Host，GUI/CLI/API 都通过同一个应用层。长任务返回 JobId，界面轮询或订阅状态；进程重启后从 BackgroundJobs 表恢复。

## 阶段验收 Gate

- [ ] 新用户可在五分钟内完成导入、索引、构建 Context 和接入验证。
- [ ] 升级/卸载不会修改源码或 Git 历史。
- [ ] GUI 展示的能力与 Capability Discovery 一致。
- [ ] 发布包通过干净机器安装、升级、卸载和回滚测试。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R14：Tree-sitter、Roslyn、LSP 与语言智能

## 目的

在确定性基础链稳定后提高多语言语法结构和精确定义引用能力。

## 前置条件

- R12 评测框架可衡量增益

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R14-W001 | 解析器插件契约 | ICodeParser 输出符号、导入、关系、Anchor 和置信度；ParserId/Version 进入缓存键。 |
| R14-W002 | Tree-sitter 首批三语言 | C#、TypeScript、Python；语法错误可部分解析，失败回退 Regex/Text。 |
| R14-W003 | 结构 Repo Map | 按组件、文件和符号构建预算受限图；排名来自任务相关性和图中心性。 |
| R14-W004 | Roslyn C# 深度语义 | 作为可选插件提供 definition/reference/type/call hierarchy，不成为通用核心强依赖。 |
| R14-W005 | LSP 沙箱 | 每工作区授权、独立进程、资源限制、明确可能执行依赖恢复；崩溃隔离。 |
| R14-W006 | 语言增益 Benchmark | 逐语言测量 Recall、精度、时间、内存和安全风险；无收益插件不默认启用。 |

## 阶段技术方向

### 语言能力分层

- Text/Regex：始终存在的降级层。
- Tree-sitter：语法结构与启发式关系。
- Roslyn/LSP：精确定义、引用、类型和诊断。
- 每条关系保存 `relation_type`、`confidence`、`source`、`parser_version`。
- 排序不能把 Tree-sitter 的 possible_call 当成 LSP 的 definition_reference。

## 阶段验收 Gate

- [ ] Regex/Text 降级路径始终可用。
- [ ] Tree-sitter 启发式关系与 LSP 精确关系明确区分。
- [ ] 语言服务失败不得阻断索引和 Context Build。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# R15：长期生态与企业模块

## 目的

在本地单机产品稳定后扩展团队、内网、插件和策略管理，且不能削弱本地安全基线。

## 前置条件

- 1.0 核心发布质量

## 任务清单

| ID | 任务 | 实施重点 |
| --- | --- | --- |
| R15-W001 | 插件权限与签名 | 插件声明读写、网络、进程权限；默认隔离和禁用未签名插件。 |
| R15-W002 | 团队共享索引 | 共享内容寻址对象和快照元数据；工作区权限、租户隔离和版本冲突处理。 |
| R15-W003 | 企业策略与内网部署 | 中央策略只能收紧本地安全，不允许放宽；支持无公网 Provider。 |
| R15-W004 | 多设备同步与审计 | 同步 Manifest/索引元数据时默认不传源码；审计可验证、可保留期限。 |
| R15-W005 | 插件市场与评测平台 | 所有插件必须声明兼容版本、权限和 Benchmark 证据。 |

## 阶段技术方向

### 长期模块约束

企业能力必须作为独立程序集/服务启用。核心开源本地版不依赖账户、云端控制面或团队服务。中央策略只能收紧安全规则，不能让 Agent 绕过本机 Offline/Restricted 模式。

## 阶段验收 Gate

- [ ] 核心本地版在没有企业服务时仍完整可用。
- [ ] 团队功能不允许跨工作区或租户泄漏。

## 阶段停止条件

- 任一 P0 安全或数据完整性问题未解决。
- 核心测试无法稳定复现，或测试结果依赖本机残留状态。
- 公共协议变化未提供迁移与兼容说明。
- 功能只有接口/DTO/注释，没有真实入口、真实数据和验收测试。

---

# 7. 数据库后续迁移建议

不得编辑 Migration0001-Migration0008。后续按新增迁移实施：

| 迁移 | 内容 |
| --- | --- |
| Migration0009RecallEvidence | FTS/符号命中证据、Anchor、Source Version |
| Migration0010ImmutableSnapshots | 快照父子关系、构建原因、commit/dirty hash |
| Migration0011CacheEntries | 多层缓存元数据、依赖、TTL、大小、状态 |
| Migration0012ModelResponses | status、headers、body object hash、usage、provider |
| Migration0013SemanticReferences | 向量对象、模型版本、工作区和快照 |
| Migration0014BackgroundJobs | 可恢复长任务和进度 |
| Migration0015ApprovalsAudit | 审批、外发和安全审计 |

迁移要求：

- 每个迁移必须幂等。
- 迁移前备份数据库。
- 大表迁移需要分批、进度和取消策略。
- 新版本能检测旧数据库并给出明确错误。
- 索引和缓存数据允许重建，用户配置与凭据不可丢失。

# 8. 公共 API 目标

## 8.1 Context API

```text
GET  /api/v1/capabilities
GET  /api/v1/workspaces
POST /api/v1/workspaces/import
POST /api/v1/workspaces/{id}/index
POST /api/v1/workspaces/{id}/refresh
GET  /api/v1/workspaces/{id}/index/status

POST /api/v1/context/build
GET  /api/v1/context/{id}
GET  /api/v1/context/{id}/payload
POST /api/v1/context/{id}/expand
POST /api/v1/context/{id}/feedback
GET  /api/v1/context/{id}/explain
```

## 8.2 Gateway API

```text
GET  /v1/models
POST /v1/chat/completions
POST /v1/responses
GET  /gateway/v1/status
GET  /gateway/v1/stats
POST /gateway/v1/cache/purge
```

## 8.3 可选统一工作流

```text
POST /api/v1/workflows/contextual-completion
```

统一工作流必须是显式入口；普通 Gateway 请求不得自动读取项目文件。

# 9. 错误模型

所有 CLI JSON 和 HTTP API 使用统一错误：

```json
{
  "success": false,
  "errorCode": "INDEX_SNAPSHOT_STALE",
  "message": "工作区内容与上下文包快照不一致",
  "recoverable": true,
  "suggestedActions": [
    "运行 workspace refresh",
    "重新构建 Context Package"
  ],
  "correlationId": "...",
  "details": {}
}
```

关键错误码：

- WORKSPACE_NOT_FOUND
- WORKSPACE_NOT_UNIQUE
- INDEX_NOT_FOUND
- INDEX_BUILD_FAILED
- INDEX_SNAPSHOT_STALE
- CONTEXT_PACKAGE_NOT_FOUND
- CONTEXT_BUDGET_INVALID
- CONTEXT_CONTENT_CHANGED
- SECURITY_BLOCKED
- APPROVAL_REQUIRED
- PROVIDER_UNAVAILABLE
- PROVIDER_RATE_LIMITED
- CACHE_CORRUPTED
- PROTOCOL_VERSION_UNSUPPORTED

# 10. 测试矩阵

| 测试层 | 必须覆盖 |
| --- | --- |
| Unit | Query Parser、Ranking、Anchor merge、Token Budget、Cache Key、Security |
| SQLite Integration | Build/Refresh、FTS、Symbols、Relations、Migration、Cache round-trip |
| HTTP Contract | Context API、Gateway、错误、认证、SSE |
| E2E | 导入→索引→Context→Payload→Gateway→Provider Stub |
| Security | Path、Symlink、XSS、DNS rebinding、Prompt Injection、Cache Poisoning |
| Performance | 1k/10k/100k 文件、并发 Context、SSE、缓存 |
| Benchmark | 真实补丁、真实测试、全生命周期 Token |

必须建立本地 Provider Stub，支持：

- 正常 JSON。
- SSE。
- 401、429、500。
- Retry-After。
- 慢响应和中途断开。
- Usage 事件。
- Tool call 响应。

# 11. CI 与质量门

每次 PR/主分支提交至少执行：

```text
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
协议 Schema 校验
数据库迁移测试
安全测试
基础 E2E
```

每日或手动执行：

- 大仓库性能。
- Gateway 压力。
- 真实 Benchmark 子集。
- 安装/升级/卸载矩阵。
- Windows/macOS/Linux 跨平台测试。

任何任务不得只在提交说明中声称“全部测试通过”；应由 CI Check 或保存的测试证据支持。

# 12. Definition of Done

一个 Work Item 只有同时满足以下条件才算完成：

- [ ] 实现位于正确模块，没有扩大 Core 职责。
- [ ] 真实入口已接线，不是孤立接口。
- [ ] 单元测试通过。
- [ ] 至少一个相关集成测试通过。
- [ ] 错误和取消路径有测试。
- [ ] 安全影响已评估。
- [ ] 公共协议/数据库变化已记录。
- [ ] Capability 和 README 与真实状态一致。
- [ ] AI_DEV_STATE 已更新。
- [ ] 有独立 Git commit。
- [ ] 阶段 Gate 证据可复现。

# 13. 推荐版本发布

| 版本 | 完成阶段 | 可宣传能力 |
| --- | --- | --- |
| 0.3-prealpha | R4-R6 | 真实多源代码知识召回、精准上下文压缩、可靠增量索引 |
| 0.4-alpha | R7-R9 | 持久化精确缓存、生产级 Gateway、上下文压缩后云端回源 |
| 0.5-alpha | R10-R12 | 可选语义参考、真实 Benchmark 和公开效果数据 |
| 0.6-beta | R11-R13 | 安全 GUI、安装升级、技术用户 Beta |
| 0.8-beta | R14 | Tree-sitter/Roslyn/LSP 语言智能 |
| 1.0 | 全部核心 Gate | 通用、稳定、可解释、安全、跨平台产品 |

# 14. 开发 AI 最终目标判断

开发 AI 必须用实际效果判断，而不是文件数量判断。

最终必须能够证明以下链路：

```text
模糊编程任务
→ 本地知识库通过 FTS/符号/关系找到正确代码
→ 只截取相关行而不是整个文件
→ Context Package 与工作区版本严格绑定
→ 完全相同安全模型请求命中本地精确缓存
→ 未命中时通过 Gateway 调用云端 Provider
→ Provider 故障按安全规则回退
→ 相似历史只作为可解释参考
→ 全生命周期 Token 与任务成功率经真实 Benchmark 证明
```

只要其中任何一段仍靠手工拼接、模拟数据、硬编码或未接线接口，就不能宣称最终目标完成。
