# CacheHub

> ⚠️ **Pre-Alpha** — 本项目处于早期开发阶段，核心链路已通过测试验证，但不建议用于生产环境或处理敏感代码。

> 让任何 AI 编程工具每次只读取真正需要的代码。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Status: Pre-Alpha](https://img.shields.io/badge/Status-Pre--Alpha-orange.svg)](#功能成熟度矩阵)

---

## CacheHub 是什么？

CacheHub 是一个**本地代码上下文路由器**和**可选模型 API 网关**。它维护版本感知的项目索引，根据当前任务生成**可解释、受 Token 预算限制**的最小 Context Package（上下文包），并允许任何 AI Agent 通过稳定协议使用。

**一句话**：给 AI 编程助手装上"眼睛"——让它看到整个代码库的结构，但只把真正需要的那部分代码喂给模型。

### 功能成熟度矩阵

| 功能 | 状态 | 说明 |
|------|------|------|
| 工作区导入与管理 | ✅ Implemented | 注册本地目录为工作区，路径校验+重复检测 |
| 全量索引构建（FTS5 + files + symbols） | ✅ Implemented | 批量事务写入 FTS5、元数据、符号/导入/关系表 |
| 增量索引刷新 | ✅ Implemented | FTS 单文件 Delete/Upsert，增量快照切换 |
| FTS5 全文搜索 | ✅ Implemented | 绑定快照的全文搜索，QueryCompiler 转义 |
| FTS5 接入 Context Recall | ✅ Implemented | FTS5 正文召回参与 Context Build |
| 任务解析（英文） | ✅ Implemented | 确定性规则解析英文任务 |
| 任务解析（中文/Unicode） | ✅ Implemented | Unicode 分词 + 中文 n-gram + 代码标识符独立通道 |
| 多源召回（路径/符号/FTS/Import/Relation） | ✅ Implemented | 9 个 IRecallSource 可组合，含 SourceEvidence |
| 排序引擎 | ✅ Implemented | 7 维特征，只启用已实现维度，归一化+效率信号 |
| 智能分块 | ✅ Implemented | LineAnchor 贯穿召回→排序→选择→分块，语法范围合并 |
| Token 预算管理 | ✅ Implemented | ValidateOrThrow 构建前验证，reserves 参与公式 |
| 上下文扩展（按文件） | ✅ Implemented | 支持按文件扩展，生成子修订包 |
| 上下文扩展（按符号） | ✅ Implemented | ExpandBySymbol 查询 file_symbols 表 |
| 可解释性 | ✅ Implemented | FeatureBreakdown 加权贡献，排除代码和遗漏 |
| 安全策略（Allow/Deny/ApprovalRequired） | ✅ Implemented | PayloadGenerator 出口强制执行，blockedFiles 返回 |
| 密钥扫描 | ✅ Implemented | 5 种密钥模式扫描，Payload 层强制阻止 |
| Local API 安全 | ✅ Implemented | Bearer Token 认证 + loopback + Host 校验 + CSP |
| CLI 接入 | ✅ Implemented | 21 个命令组，统一 ErrorEnvelope |
| 文件导出 | ✅ Implemented | Markdown/JSON/File 导出，Plan/Apply 分离 |
| Desktop Web UI | ✅ Implemented | 导入→索引→Context→预览→导出闭环 |
| 代码解析（C#/TS/Python/Markdown） | ✅ Implemented | Regex 增强解析器 v2.0，结果持久化到主索引 |
| Repo Map | ✅ Implemented | 真实目录树 + 重要性评分 + 预算裁剪 |
| 可选模型 API 网关 | ✅ Implemented | SSE 流式 + 状态码透传 + loopback + 并发限制 + Byte LRU |
| 持久化缓存 | ✅ Implemented | SqliteCacheStore 依赖哈希失效 + TTL + 命中率统计 |
| 上下文与模型统一工作流 | ✅ Implemented | ContextualCompletion 协议 + PromptAssemblyService |
| 语义参考缓存 | 🧪 Experimental | SemanticMode Off/Reference/StrictExperimental，默认安全边界 |
| 安全策略集中服务 | ✅ Implemented | SecurityApplicationService 统一调用，EvaluateFile 出口强制 |
| Benchmark | 🧪 Experimental | 20 任务/5 类仓库真实结构，Gate 测试验证无硬编码 |
| Git 仓库操作 | ✅ Implemented | clone/pull/status/diff，LFS 修复+凭据脱敏+输出限制 |
| Tree-sitter | 📦 Scaffold | regex fallback 始终可用，Tree-sitter 需原生库 |
| LSP | 📦 Scaffold | 帧读取器+请求关联已实现，独立进程审批模型就绪 |
| Provider/路由/预算 | 📦 Scaffold | OpenAI-compatible 基线，路由/健康检查待完善 |
| 插件/团队/企业 | 📦 Planned | 签名验证+权限隔离+企业策略模型已预留 |

**图例**：✅ Implemented = 已实现且有测试 ｜ 🧪 Experimental = 有原型但需更多验证 ｜ 📦 Scaffold = 有代码骨架 ｜ 📦 Planned = 仅设计

### 解决了什么问题？

| 痛点 | CacheHub 方案 |
|------|---------------|
| AI Agent 每次都扫全仓库，Token 浪费严重 | 版本感知索引 + 排序引擎，选相关代码 |
| 上下文太大导致模型"失忆" | Token 预算管理器，硬上限 + 安全边界 + 构建前验证 |
| 选了什么、为什么选，黑盒不可知 | ContextExplainer，选择理由和排除原因可查 |
| 每个工具的上下文方案各不相同 | Agent 无关协议（CLI / Local API / 文件导出），任何工具可接入 |
| 密钥泄漏风险 | 安全策略 + 5 种密钥扫描，Payload 出口强制执行 |
| 索引版本不可信 | 真实 Active Snapshot 绑定 + 文件哈希从磁盘补算 |

---

## 核心能力

### 1. 版本感知索引引擎

- **全量文件扫描**：异步流式枚举，深度/数量/大小限制 + 符号链接保护
- **批量事务索引**：单连接单事务写入 files + symbols + imports + relations，FTS 分离事务
- **内容哈希**：分层哈希策略（小文件全量 SHA-256，大文件快速指纹），Context Package 补算强哈希
- **FTS5 全文检索**：版本感知的全文搜索，绑定 IndexSnapshotId，QueryCompiler 转义
- **增量索引**：FTS 单文件 Delete/Upsert，修改单文件不再 ClearSnapshot
- **文件监视器**：防抖事件队列 + 溢出检测 + 一致性校验器
- **忽略规则引擎**：系统默认 > `.gitignore` > `.cachehubignore` > 用户规则，四层合并
- **解析器持久化**：C#/TS/Python/Markdown 解析结果写入 file_symbols/file_imports/file_relations

### 2. 上下文引擎 (Context Engine)

- **任务解析器**：确定性规则解析（不依赖 LLM），支持中文/Unicode + 代码标识符 + 错误栈 + 路径独立通道
- **多源召回**：9 个 IRecallSource（路径/FTS5/符号/Import/Relation/Test/Config/RepoMap/GitDiff），含 SourceEvidence
- **排序引擎**：7 维特征（路径/FTS/符号/Import/Relation/Test/Config），只启用已实现维度，归一化+效率信号
- **智能分块**：LineAnchor 贯穿召回→排序→选择→分块，语法范围合并裁剪，不再全文件切块
- **Token 预算管理**：模型窗口 / Agent 预留 / 响应预留 / 目标 / 硬上限 / 安全边界，ValidateOrThrow 构建前验证
- **5 种选择模式**：Full / Chunks / Outline / DeterministicSummary / Metadata
- **上下文扩展**：按文件/符号扩展，生成子修订包（ParentPackageId + 累计预算 + 增量哈希）
- **可解释性**：FeatureBreakdown 加权贡献 / 排除代码 / 潜在遗漏
- **PayloadPlan 不可变**：Manifest 和 Payload 共享同一计划，防止二次分块差异

### 3. 多语言代码解析

> ✅ Regex 增强解析器 v2.0，结果持久化到主索引。Tree-sitter 作为后续可选增强。

| 语言 | 解析器 | 提取内容 |
|------|--------|----------|
| C# | CSharpRegexParser v2.0 | namespace/class/method/property/record/构造器/字段/箭头方法/继承关系 |
| TypeScript/JavaScript | TypeScriptRegexParser | export class/interface/function/import + 调用 |
| Python | PythonRegexParser | class/def/import/decorator + 调用 |
| Markdown | MarkdownParser | 标题、代码块、配置键 |
| 纯文本 | TextParser | 通用结构提取 |

- **代码关系**：语法 / 启发式 / 语义三级置信度 (0..1)
- **确定性大纲**：按行号 + 名称稳定排序
- **Repo Map**：真实目录树 + 重要性评分 + 预算裁剪 + 公共符号优先
- **解析器缓存**：哈希 + ParserId + 版本键控

### 4. 安全策略

- **3 级外发决策**：Allow / Deny / ApprovalRequired
- **密钥扫描**：API Key / 密码 / 私钥 / 连接字符串 / Bearer Token
- **敏感文件检测**：`.env`、`.pem`、`.key`、`id_rsa`、`credentials.json` 等
- **出口强制执行**：PayloadGenerator 在生成前评估每个文件，Deny/ApprovalRequired 文件不进入 Payload，blockedFiles 返回给调用方
- **路径安全**：SafePathResolver 拒绝绝对路径/`..`/URL 编码/symlink 逃逸
- **Local API 安全**：Bearer Token 认证 + loopback 强制 + Host 校验 + CSP

### 5. 可选模型 API 网关

> ✅ Gateway 支持 SSE 流式透传、状态码透传、loopback 强制、并发限制、Byte LRU 缓存。

- **OpenAI 兼容**：chat/completions、/models 端点
- **SSE 流式**：逐帧转发，客户端断连取消上游
- **状态码透传**：401/429/500 正确传播，不缓存非 2xx
- **安全缓存**：仅缓存安全请求（无工具、无流式、合法 2xx）
- **SingleFlight**：ConcurrentDictionary + Lazy<Task> 原子去重
- **并发限制**：SemaphoreSlim + 请求大小限制 + Byte LRU 淘汰
- **用量统计**：请求数 / 缓存命中率 / Token 节省 / 平均延迟

### 6. 持久化缓存

> ✅ SqliteCacheStore 实现跨会话持久化缓存。

- **依赖哈希失效**：文件变化自动失效关联缓存
- **TTL 过期**：可配置的生存时间
- **损坏隔离**：缓存损坏自动隔离并回源，不影响主流程
- **命中率统计**：区分本地计算复用和实际未回源 Token

### 7. 上下文与模型统一工作流

> ✅ ContextualCompletion 协议统一 Context Build 和 Gateway 调用。

- **ContextualCompletionRequest/Response**：显式 workspace_id + task + model + budget + 安全模式
- **PromptAssemblyService**：只拼装明确模板和 Context Payload，不猜测 Agent 私有提示
- **WorkspaceResolution**：禁止隐式猜测，NotUnique/NotFound 明确返回
- **GatewayMetadata**：context_package_id + snapshot_id + dirty_state_hash + client_id

### 8. Agent 无关协议

CacheHub 提供 4 种接入方式，任何 AI 编程工具均可使用：

```
┌─────────────────────────────────────────────────┐
│              AI Agent (任何工具)                  │
│  Codex / Claude Code / Cursor / 自定义 Agent     │
└──────────┬──────────┬──────────┬────────────────┘
           │          │          │
      CLI 命令    Local API   文件导出
      cachehub    :5099/api   .cachehub/
           │          │          │
           └──────────┴──────────┘
                      │
              ┌───────┴───────┐
              │   CacheHub    │
              │  上下文引擎    │
              └───────────────┘
```

---

## 技术栈

| 区域 | 技术选型 |
|------|----------|
| 主语言 | C# / .NET 9 (LTS) |
| SDK | 9.0.313 (global.json 锁定) |
| 数据库 | SQLite + FTS5 (Microsoft.Data.Sqlite) |
| 语法解析 | Regex + [GeneratedRegex] 源生成器 (v2.0) |
| Web UI | ASP.NET Core 最小 API + 静态文件 |
| Gateway | HttpListener + OpenAI 兼容协议 + SSE |
| 发布 | 单文件自包含 (PublishSingleFile + SelfContained) |
| 测试 | xUnit + coverlet 覆盖率 |
| 代码质量 | TreatWarningsAsErrors + Nullable + AnalysisLevel latest-recommended |
| CI/CD | GitHub Actions (push + PR 自动触发) |
| 许可证 | MIT |

---

## 项目结构

```
CacheHub/
├── CacheHub.sln                    # 解决方案文件
├── Directory.Build.props           # 全局构建属性
├── global.json                     # .NET SDK 版本锁定
├── install.ps1 / install.sh        # 安装脚本
│
├── src/                            # 源代码
│   ├── CacheHub.Core/              # 领域核心：模型、错误、标识符、上下文、安全、Tokenizer、Gateway、Provider、Semantic、LSP、Ecosystem
│   ├── CacheHub.Storage/           # 存储层：SQLite、9 个迁移、Workspace/ContextPackage/Feedback 仓储、FTS5 搜索、CacheStore
│   ├── CacheHub.Indexing/          # 索引层：目录扫描、忽略规则、文件检测、4 语言解析器 v2.0、RepoMap、缓存
│   ├── CacheHub.Context/           # 上下文层：任务解析、9 源召回、7 维排序、锚点分块、预算验证、PayloadPlan、扩展修订
│   ├── CacheHub.Cli/               # CLI 入口：21 个命令组、55 个子命令、单文件发布
│   └── CacheHub.Desktop/           # Web UI：ASP.NET Core 最小 API + 17 个 Local API 路由 + 6 个页面
│
├── tests/
│   └── CacheHub.Tests/             # 749 单元/集成/Gate 测试
│
├── integration/                    # Agent 集成套件
│   ├── skills/universal/           # Universal Skill（通用技能）
│   ├── protocol/                   # 协议文档
│   ├── tutorials/                  # 教程
│   ├── examples/                   # 3 个 Agent 示例（Codex / Claude Code / Shell）
│   └── templates/                  # 系统提示词模板
│
├── Docs/                           # 项目文档
│   ├── INSTALL.md                  # 安装手册（AI Agent 可读）
│   ├── USAGE.md                    # 使用教程
│   ├── ARCHITECTURE.md             # 架构设计
│   ├── adr/                        # 架构决策记录 (ADR-0001~0004)
│   ├── specs/                      # Context Package Manifest Schema v1
│   ├── ai/                         # AI 开发状态、路线图、风险登记
│   ├── architecture/               # 架构文档
│   ├── benchmarks/                  # 基准测试
│   ├── security/                   # 安全文档
│   └── research/                   # 研究台账
│
├── AGENTS.md                       # Agent 接入指南
├── CHANGELOG.md                    # 变更日志
├── CONTRIBUTING.md                 # 贡献指南
├── .cachehubignore.example          # 忽略规则示例
└── LICENSE                         # MIT 许可证
```

---

## 快速开始

### 安装

详细安装步骤请参阅 **[安装手册](Docs/INSTALL.md)**，AI Agent 可直接阅读该文件完成全自动安装。

最简流程：

```bash
# 1. 克隆仓库
git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub

# 2. 一键安装（会自动构建 + 测试 + 发布单文件可执行文件）
# Windows:
./install.ps1
# Linux/macOS:
./install.sh

# 3. 验证
cachehub version
cachehub capabilities
cachehub integration verify
```

### 基本使用

```bash
# 1. 导入项目作为工作区
cachehub workspace import /path/to/your/project

# 2. 构建索引
cachehub index build --id=<workspace-id>

# 3. 根据任务构建上下文
cachehub context build --workspace=<id> --task="Fix login bug" --output=json

# 4. 检查上下文包
cachehub context inspect --id=<context-id>

# 5. 导出为 Markdown（直接喂给 AI）
cachehub context export --id=<context-id> --format=markdown

# 6. 如需补充文件
cachehub context expand --id=<context-id> --file=src/auth.ts --reason="Missing auth implementation"
```

### Web UI

```bash
dotnet run --project src/CacheHub.Desktop
# 浏览器访问 http://localhost:5099
# API Token 打印在终端中，所有请求需 Authorization: Bearer <token>
```

---

## CLI 命令参考

| 命令 | 子命令 | 功能 |
|------|--------|------|
| `capabilities` | — | 能力发现（版本、协议、已启用功能） |
| `workspace` | `import` / `list` / `status` / `remove` | 工作区管理 |
| `index` | `build` / `status` / `verify` / `refresh` | 索引构建/状态/一致性校验/增量刷新 |
| `context` | `build` / `inspect` / `list` / `export` / `expand` / `feedback` | 上下文包全生命周期 |
| `detect` | — | 项目类型检测 + 初始化计划 |
| `gateway` | `start` / `status` / `stop` | 可选模型 API 网关 |
| `config` | `show` / `init` / `set` | 配置管理 |
| `stats` | — | 使用统计 |
| `repo` | `inspect` / `clone` / `status` / `diff` / `pull` | Git 仓库操作（安全模式） |
| `integration` | `verify` | 安装验证（5 步检查） |
| `version` | — | 版本信息 |
| `help` | `[command]` | 帮助 |

**全局选项**：`--output=json` / `--json`（大多数命令支持 JSON 输出）

---

## Local API

Web UI 启动后提供 17 个 REST API 路由（所有 `/api/` 路由需 Bearer Token 认证）：

| 路由 | 方法 | 功能 |
|------|------|------|
| `/api/v1/capabilities` | GET | 能力发现 |
| `/api/v1/workspaces` | GET / POST | 工作区列表 / 导入 |
| `/api/v1/workspaces/{id}` | GET / DELETE | 状态 / 删除 |
| `/api/v1/workspaces/{id}/index` | POST | 索引构建（后台批量事务） |
| `/api/v1/workspaces/{id}/contexts` | GET | 上下文包列表 |
| `/api/v1/workspaces/{id}/export` | POST | 文件导出 |
| `/api/v1/context/build` | POST | 构建上下文包 |
| `/api/v1/context/{id}` | GET | 检查上下文包 |
| `/api/v1/context/{id}/expand` | POST | 扩展上下文（生成子修订包） |
| `/api/v1/context/{id}/feedback` | POST | 提交反馈 |
| `/api/v1/context/{id}/explain` | GET | 解释选择 / 遗漏 / 预算 |
| `/api/v1/context/{id}/payload` | GET | 获取完整 Payload（含 blockedFiles） |
| `/api/v1/search` | GET | FTS5 全文搜索 |
| `/api/v1/outline` | GET | 代码大纲 |
| `/api/v1/stats` | GET | 使用统计 |

---

## Agent 集成

CacheHub 支持 3 种 AI Agent 集成方式，详见 [AGENTS.md](AGENTS.md) 和 [集成教程](Docs/USAGE.md#agent-集成)：

### 方式一：CLI 调用（最简单）

```bash
# Agent 直接调用 CLI
cachehub context build --workspace=<id> --task="当前任务" --output=json | jq '.id'
cachehub context export --id=<ctx-id> --format=markdown
```

### 方式二：Local API（程序化集成）

```bash
# 通过 HTTP API 集成（需 Bearer Token）
curl -X POST http://localhost:5099/api/v1/context/build \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"workspaceId":"<id>","task":"Fix login bug"}'
```

### 方式三：文件导出（离线场景）

```bash
# 导出到 .cachehub/ 目录
cachehub context export --id=<ctx-id> --format=file
# 生成: .cachehub/workspace.json, latest-context.manifest.json, latest-context.md, repomap.md
```

---

## 测试覆盖率

| 类别 | 数量 | 说明 |
|------|------|------|
| 单元测试 | 749 通过 | 覆盖模型、工具函数、存储、索引、上下文引擎 |
| 跳过（需真实 Git 环境） | 2 | |
| Gate 回归测试 | 60+ | R4-R15 阶段门验证（FTS/符号/锚点/预算/缓存/Gateway/安全/Benchmark） |
| 真实 SQLite 集成测试 | 10+ | index→context→payload 完整闭环 |
| **总计** | **751** | |

---

## 文档导航

| 文档 | 说明 |
|------|------|
| [安装手册](Docs/INSTALL.md) | 完整安装步骤，AI Agent 可读 |
| [使用教程](Docs/USAGE.md) | CLI / Web UI / Agent 集成详细教程 |
| [架构设计](Docs/ARCHITECTURE.md) | 系统架构、模块设计、数据流 |
| [Agent 接入指南](AGENTS.md) | AI Agent 快速接入 |
| [变更日志](CHANGELOG.md) | 版本历史 |
| [贡献指南](CONTRIBUTING.md) | 如何参与开发 |
| [Context Package Schema](Docs/specs/context-package.manifest.v1.json) | 上下文包 JSON Schema v1 |
| [ADR 记录](Docs/adr/) | 架构决策记录 |

---

## 许可证

[MIT License](LICENSE) — Copyright (c) 2026 CacheHub Contributors
