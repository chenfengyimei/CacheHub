# CacheHub 架构设计

---

## 系统概览

CacheHub 采用分层架构，从底到上分为 6 层：

```
┌─────────────────────────────────────────────────┐
│                  接入层 (Access)                  │
│   CLI (cachehub)  ·  Web UI (:5099)  ·  Gateway  │
├─────────────────────────────────────────────────┤
│                  上下文层 (Context)               │
│  TaskParser · Recall · Ranking · Chunking        │
│  Budget · Selection · Engine · Expand · Explain  │
├─────────────────────────────────────────────────┤
│                  索引层 (Indexing)                │
│  Enumerator · IgnoreRules · FileDetector         │
│  Hasher · Parser(4语言) · RepoMap · FTS5 · Cache │
├─────────────────────────────────────────────────┤
│                  存储层 (Storage)                 │
│  SQLite · 9 Migrations · 3 Repositories · FTS5   │
├─────────────────────────────────────────────────┤
│                  核心层 (Core)                    │
│  Domain Models · Errors · IDs · Context Schema   │
│  Security · Tokenizer · Semantic · LSP Contracts │
├─────────────────────────────────────────────────┤
│                  基础设施 (Infra)                 │
│  .NET 10 · ConfigManager · LruCache · GitWrapper  │
└─────────────────────────────────────────────────┘
```

---

## 模块详解

### 1. Core 层 (`CacheHub.Core`)

项目的基础契约和领域模型，无外部依赖。

| 模块 | 职责 |
|------|------|
| Identifiers | 强类型 ID（WorkspaceId, FileId, IndexSnapshotId, ContextPackageId 等） |
| Errors | 错误模型（CacheHubException + ErrorCode + 可恢复性 + 建议操作） |
| Context | Context Package Manifest/Schema v1、选择模式、预算模型 |
| Capabilities | Capability Discovery v1（能力发现协议） |
| Workspaces | Workspace 聚合根（状态机：Created → Indexed → Active） |
| Security | SecurityPolicyEnforcer（4 级模式）、SecretScanner（5 种密钥） |
| Tokenizer | TokenizerRegistry（3 种分词器 + 模型注册表） |
| Gateway | GatewayServer、CacheSafetyChecker、SingleFlight、SSE 解析 |
| Providers | IProvider、OpenAiCompatibleProvider、CostCalculator、RoutingConfig |
| Semantic | IEmbeddingProvider、InMemoryVectorStore、余弦相似度 |
| LSP | ILanguageServer、LspLifecycle、JSON-RPC 2.0 序列化器 |
| Ecosystem | PluginManifest、EnterprisePolicy、TeamConfig、UpdateConfig |
| Benchmarks | BenchmarkTaskSet（6 任务）、MetricsCalculator、ReportGenerator |

### 2. Storage 层 (`CacheHub.Storage`)

基于 SQLite + FTS5 的持久化层。

| 组件 | 说明 |
|------|------|
| AppDataDirectory | 跨平台数据目录（Windows: `%LOCALAPPDATA%`, Linux: `~/.local/share`, macOS: `~/Library/Application Support`） |
| SqliteConnectionFactory | 连接池禁用（跨平台文件管理），单连接 |
| MigrationRunner | 顺序迁移执行器 |
| Migration 0001 | Initial：workspaces、index_snapshots、files 表 |
| Migration 0002 | FTS5：全文搜索虚拟表 |
| Migration 0003 | ContextPackages：上下文包持久化 |
| Migration 0004 | Feedback：反馈 + 反馈文件表 |
| SqliteWorkspaceRepository | 工作区 CRUD |
| SqliteContextPackageRepository | 上下文包持久化 + 查询 |
| SqliteFeedbackRepository | 反馈持久化 |
| Fts5Index | FTS5 全文搜索（绑定 IndexSnapshotId） |

### 3. Indexing 层 (`CacheHub.Indexing`)

文件索引和代码解析引擎。

| 组件 | 说明 |
|------|------|
| DirectoryEnumerator | 异步流式枚举，深度/数量/大小限制 + 符号链接保护 |
| IgnoreRuleEngine | 四层合并：系统默认 > .gitignore > .cachehubignore > 用户规则 |
| FileTypeDetector | 30+ 扩展名映射 + 内容采样 + 证书/归档检测 |
| FileHasher | 分层哈希：小文件全量 SHA-256，大文件指纹 |
| FileEntry | 状态机：Discovered → Indexed / Ignored / Failed / Deleted / Stale |
| RipgrepSearcher | 进程包装 + 回退搜索 |
| FileWatcher | 防抖事件队列 + 溢出检测 |
| ConsistencyReconciler | 磁盘 vs 索引对比，Git HEAD 变更检测 |
| CSharpRegexParser | namespace/class/method/property/import + 启发式调用 |
| TypeScriptRegexParser | export class/interface/function/import + 调用 |
| PythonRegexParser | class/def/import/decorator + 调用 |
| MarkdownParser | 标题、代码块、配置键 |
| TextParser | 通用文本结构提取 |
| DeterministicOutlineGenerator | 稳定排序大纲（按行号 + 名称） |
| RepoMapGenerator | 预算受限代码树 + 关键符号 |
| ParserCache | 哈希 + ParserId + 版本键控缓存 |

### 4. Context 层 (`CacheHub.Context`)

上下文引擎——CacheHub 的核心智能。

| 组件 | 说明 |
|------|------|
| TaskParser | 确定性规则解析（不依赖 LLM），提取关键词/路径/符号 |
| RecallPipeline | 五路召回：路径匹配 / 符号匹配 / 关键词搜索 / Git Diff / 当前文件 |
| RankingEngine | 9 维特征 minmax 归一化 + 加权评分 |
| RankingProfile | 版本化权重（deterministic-v1 v3），权重和 = 1.0 |
| ChunkingStrategy | 语法块优先 + 行窗口回退 + 重叠控制 |
| TokenBudget | 模型窗口 / Agent 预留 / 响应预留 / 目标 / 硬上限 / 安全边界 |
| SelectionEngine | 5 种模式：Full / Chunks / Outline / DeterministicSummary / Metadata |
| ContextEngine | 核心引擎，编排 TaskParser → Recall → Ranking → Budget → Selection |
| ContextExpander | 按文件/符号扩展，追踪父上下文 + 增量 Token |
| ContextExplainer | 选择理由 / 预算驱逐原因 / 潜在遗漏检测 |
| ContextPackageCache | 严格绑定（task + snapshot + profile + budget + security） |
| PayloadGenerator | 从 Manifest + 文件内容生成 Payload |
| FileExporter | `.cachehub/` 目录导出协议 |

### 5. 排序引擎 9 维特征

| 特征 | 说明 |
|------|------|
| PathMatch | 文件路径与任务关键词的匹配度 |
| SymbolMatch | 文件中的符号与任务提到的符号匹配度 |
| KeywordMatch | 文件内容与任务关键词的 FTS5 匹配度 |
| ImportDistance | 通过 import 关系的距离 |
| CallDistance | 通过调用关系的距离 |
| FileSize | 文件大小评分（过大降权） |
| Recency | 最近修改时间评分 |
| DirectoryProximity | 与已选文件的目录邻近度 |
| TypeRelevance | 文件类型与任务的相关性 |

### 6. CLI 层 (`CacheHub.Cli`)

命令行入口，单文件自包含发布。

- 21 个命令组、55 个子命令
- 全局 `--output=json` 支持
- 单文件发布（`PublishSingleFile` + `SelfContained` + 压缩）

### 7. Desktop 层 (`CacheHub.Desktop`)

ASP.NET Core 最小 API Web 服务。

- 17 个 REST API 路由
- 6 个静态 HTML 页面（`wwwroot/`）
- 静态文件服务 + 默认文件
- 依赖注入：AppDataDirectory、SqliteConnectionFactory、3 个 Repository、ContextEngine

---

## 数据流

### 上下文构建流程

```
用户输入任务描述
       │
       ▼
  TaskParser ──→ 提取关键词/路径/符号
       │
       ▼
  RecallPipeline ──→ 五路召回候选文件
       │            ├── 路径匹配
       │            ├── 符号匹配
       │            ├── FTS5 关键词搜索
       │            ├── Git Diff（可选）
       │            └── 当前文件
       │
       ▼
  RankingEngine ──→ 9 维特征评分 + minmax 归一化
       │
       ▼
  TokenBudget ──→ 计算可用 Token 预算
       │
       ▼
  SelectionEngine ──→ 按预算选择文件 + 决定模式
       │                 ├── Full（完整内容）
       │                 ├── Chunks（分块）
       │                 ├── Outline（大纲）
       │                 ├── DeterministicSummary（摘要）
       │                 └── Metadata（元数据）
       │
       ▼
  SecurityPolicyEnforcer ──→ 密钥扫描 + 外发检查
       │
       ▼
  ContextPackage Manifest ──→ 持久化到 SQLite
       │
       ▼
  PayloadGenerator ──→ 生成实际代码内容
       │
       ▼
  导出（Markdown / JSON / .cachehub/ 文件）
```

---

## 架构决策记录 (ADR)

| ADR | 标题 | 决策 |
|-----|------|------|
| ADR-0001 | 主语言选择 | C# / .NET 10（性能、类型安全、跨平台、生态） |
| ADR-0002 | 存储方案 | SQLite + FTS5（零配置、嵌入式、跨平台、全文搜索） |
| ADR-0003 | 解析策略 | Regex + [GeneratedRegex]（确定性、无原生依赖，后续替换 Tree-sitter） |
| ADR-0004 | UI 方案 | ASP.NET Core 最小 Web（0.1-beta 阶段，后续迁移 Avalonia） |

---

## 安全架构

### 分层防御

```
┌─────────────────────────────────────┐
│  1. 默认本地运行（不外发）            │
├─────────────────────────────────────┤
│  2. 4 级外发模式                     │
│     Standard / Restricted /          │
│     PreviewRequired / Offline        │
├─────────────────────────────────────┤
│  3. 密钥扫描（5 种类型）              │
│     API Key / Password / Private Key │
│     Connection String / Bearer Token │
├─────────────────────────────────────┤
│  4. 敏感文件检测                      │
│     .env / .pem / .key / id_rsa /   │
│     credentials.json 等              │
├─────────────────────────────────────┤
│  5. 发送前检查                        │
│     路径 + 内容 + 模式三重检查        │
└─────────────────────────────────────┘
```

### Gateway 安全

- 仅回环监听（127.0.0.1）
- 拒绝工具调用请求（防注入）
- 拒绝高温度请求（防泄漏）
- 仅缓存安全请求

---

## 扩展性设计

### 插件系统（Ecosystem 模块）

- **PluginManifest**：插件元数据（名称、版本、入口点、权限声明）
- **EnterprisePolicy**：企业策略（允许/禁止的插件、安全策略覆盖）
- **TeamConfig**：团队配置（共享配置、权限继承）
- **UpdateConfig**：更新策略（自动/手动、频道选择）

### Provider 扩展

- `IProvider` 接口可接入任何模型提供商
- `RoutingConfig` 支持：Explicit / RoundRobin / LeastLatency / Fallback
- `CostCalculator` 支持版本化定价
- `CredentialRef` 仅存储凭据 ID，不存储密钥本身

### 解析器扩展

- `ICodeParser` 接口可接入任何语言的解析器
- 当前使用 Regex 解析器（确定性、无原生依赖）
- 未来可替换为 Tree-sitter（精确语法树）
- LSP 接口已定义（`ILanguageServer`），可接入语言服务器获取精确语义

---

## 性能设计

| 设计点 | 策略 |
|--------|------|
| 文件枚举 | 异步流式（IAsyncEnumerable），不一次性加载内存 |
| 哈希计算 | 分层策略（小文件全量，大文件指纹） |
| 解析缓存 | 哈希 + ParserId + 版本键控，避免重复解析 |
| 上下文缓存 | 严格绑定（task + snapshot + profile + budget + security） |
| LRU 缓存 | 线程安全 + TTL + 大小限制 |
| 单文件发布 | PublishSingleFile + SelfContained + 压缩 |
| SQLite 连接 | 池化禁用（跨平台文件管理），单连接复用 |

---

## 测试策略

| 层级 | 数量 | 说明 |
|------|------|------|
| 单元测试 | 995 | 覆盖所有模块的核心逻辑 + 安全 + Gateway + Gate 回归 + V3 闭环 + 12 语言解析器 |
| 跳过 | 2 | 需要真实 Git 环境 |
| E2E 集成测试 | 8 | 端到端工作流验证 |
| 真实场景测试 | 5 | 模拟真实使用场景 |
| 基准测试 | 6 | 跨语言任务 + Ground Truth |

测试规范：
- `TreatWarningsAsErrors` — 警告视为错误
- `Nullable enable` — 空引用安全
- `AnalysisLevel latest-recommended` — 最新推荐分析级别
- 无网络/云依赖（测试完全本地化）
