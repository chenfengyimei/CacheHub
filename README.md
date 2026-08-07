<div align="center">

# CacheHub

**�?AI 编程助手装上"眼睛"——让它看到整个代码库的结构，但只把真正需要的那部分代码喂给模型�?*

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Tests](https://img.shields.io/badge/Tests-847%20passed-brightgreen.svg)](#测试覆盖�?
[![CI](https://img.shields.io/badge/CI-3%20platforms%20%E2%9C%93-success.svg)](.github/workflows/ci.yml)
[![Status: Pre-Alpha](https://img.shields.io/badge/Status-Pre--Alpha-orange.svg)](#功能成熟度矩�?

</div>

---

## 这是什么？

CacheHub 是一�?*本地代码上下文路由器**�?*可选模�?API 网关**�?
�?AI 编程助手（Codex、Claude Code、Cursor 等）需要理解你的代码库时，通常会把整个项目塞给模型——一�?50 万行的仓库可能消�?10 �? Token，既昂贵又容易让模型"失忆"�?
CacheHub 解决这个问题：它在本地维护版本感知的代码索引，根据当前任务智能选出**最相关的代码片�?*，生成受 Token 预算严格限制�?Context Package，然后通过 CLI、HTTP API 或文件协议交给任�?AI Agent�?
**目标**：在不降低任务成功率的前提下，显著减少无关上下文 Token，让模型注意力集中在真正需要改的代码上�?
---

## 为什么它不一样？

市面上不�?�?AI 喂代�?的工具，�?CacheHub 有几个独特的设计决策�?
### 🧠 12 源可组合召回引擎

不是一个简单的全文搜索。CacheHub 同时�?12 个独立召回源获取候选文件：

| 召回�?| 作用 |
|--------|------|
| **FTS5 正文搜索** | BM25 排序，命中行号定位，Snippet 提取 |
| **符号精确匹配** | 查询 file_symbols 表，匹配类名/方法�?属性名 |
| **Import 关系扩展** | 从命中文件向导入者扩展一�?|
| **Relation 调用关系** | file_relations 表查询，调用�?被调用链扩展 |
| **测试文件关联** | 源文�?�?测试文件确定性映�?|
| **配置文件关联** | 组件 �?配置文件映射 |
| **RepoMap 结构** | 目录树重要性评�?+ 预算裁剪 |
| **Git Diff** | 当前变更文件优先召回 |
| **当前文件** | 编辑器上下文 |
| **路径匹配** | 任务文本中提取的文件路径直接匹配 |
| **语义参�?* | 历史任务/错误/反馈的词汇相似度参�?|
| **目录降级** | 无直接候选时的入口文件兜�?|

每个候选文件携�?`SourceEvidence`，可追溯到数据库中的实际证据——不是黑盒�?
### 🎯 锚点驱动的精准分�?
不是把整个文件丢给模型。CacheHub �?`LineAnchor` 贯穿召回→排序→选择→分块全链路�?
- 符号定义行范�?�?锚点
- FTS 命中�?�?锚点
- Git Diff 变更�?�?锚点
- 锚点周围 15 行上下文 �?精确分块
- 相邻/重叠范围合并 �?不重�?
**结果**：一�?2000 行的文件，如果只�?`refreshToken()` 方法相关，只发送那 50 行——而不是全文件�?
### 🔒 安全策略从出口强�?
不只�?扫描密钥然后警告"。CacheHub �?`SecurityPolicyEnforcer` �?Payload 出口强制执行�?
- `Allow` �?正常输出
- `Deny` �?文件不进�?Payload，调用方收到 `blockedFiles` 列表
- `ApprovalRequired` �?需要审�?ID 才能输出内容
- **Offline 模式** �?网络层完全阻止发送工作区内容

5 种密钥模式扫描（API Key / 密码 / 私钥 / 连接字符�?/ Bearer Token�? 敏感文件检测（`.env`、`.pem`、`id_rsa` 等）�?
### 📊 可解释�?
选了什么文件、每个文件的得分、为什么被排除、预算消耗明细——全部记录在 Manifest 中。`context explain` 命令输出完整�?FeatureBreakdown�?
```
src/auth/refresh.ts  Score: 0.92  Mode: chunks  Tokens: 450
  ├─ pathMatch:      0.90  (路径包含 "auth")
  ├─ symbolMatch:    1.00  (精确匹配 "refreshToken")
  ├─ ftsMatch:       0.75  (FTS5 BM25 命中)
  ├─ importRelation: 0.60  (�?auth.service.ts 导入)
  └─ testRelation:   0.40  (有对应测试文�?
```

### 🔄 版本感知索引 + 增量更新

索引绑定 `IndexSnapshotId`，Context Manifest 记录使用了哪个快照。文件修改后�?
- FTS 单文�?Delete/Upsert（不清空整个快照�?- 内容哈希从磁盘补算（不再�?`sha256:pending` 占位�?- 同大小同时间戳的变异通过 SHA-256 内容哈希检�?- 工作区内容变化后�?Context Package 自动失效

### 🌐 Agent 无关 �?任何工具都能�?
不绑定特�?AI Agent 的私有协议。三种接入方式覆盖所有场景：

| 方式 | 适用场景 | 示例 |
|------|----------|------|
| **CLI** | 最简单，直接调命�?| `cachehub context build --task="Fix bug" --output=json` |
| **Local API** | 程序化集成，HTTP + Bearer Token | `POST http://localhost:5099/api/v1/context/build` |
| **文件导出** | 离线场景，生�?`.cachehub/` 目录 | `cachehub context export --format=markdown` |

---

## 功能成熟度矩�?
| 功能 | 状�?| 说明 |
|------|------|------|
| 工作区导入与管理 | �?Implemented | 路径校验 + 重复检�?+ 状态机 |
| 全量索引构建 | �?Implemented | 批量事务写入 files + symbols + imports + relations + FTS5 |
| 增量索引刷新 | �?Implemented | FTS 单文�?Delete/Upsert，内容哈希检测变�?|
| FTS5 全文搜索 + Context Recall | �?Implemented | QueryCompiler 转义 + BM25 + 命中行锚�?|
| 任务解析（中英文�?| �?Implemented | Unicode 分词 + 中文 n-gram + 代码标识符独立通道 |
| 12 源可组合召回 | �?Implemented | IRecallSource 接口 + SourceEvidence 追溯 + ScoreHint 排序 |
| 7 维排序引�?| �?Implemented | 只启用已实现维度 + 归一�?+ 效率信号 |
| 锚点驱动的智能分�?| �?Implemented | LineAnchor 贯穿召回→排序→选择→分�?|
| Token 预算管理 | �?Implemented | ValidateOrThrow 构建前验�?+ reserves 参与公式 |
| 上下文扩展（文件 + 符号�?| �?Implemented | 生成子修订包 + ParentPackageId + 累计预算 |
| 可解释�?| �?Implemented | FeatureBreakdown + 排除原因 + 潜在遗漏 |
| 安全策略出口强制 | �?Implemented | Allow/Deny/ApprovalRequired + blockedFiles 返回 |
| 密钥扫描 | �?Implemented | 5 种模�?+ Payload 层强制阻�?|
| Local API 安全 | �?Implemented | Bearer Token + loopback + Host 校验 + CSP |
| 路径安全 | �?Implemented | SafePathResolver 跨平�?symlink 逃逸检�?|
| CLI 接入 | �?Implemented | 21 个命令组 + 统一 ErrorEnvelope |
| Desktop Web UI | �?Implemented | 导入→索引→Context→预览→导出闭环 |
| 代码解析�?2 种语言�?| �?Implemented | C#/TS/JS/Python/Go/Rust/Java/C/C++/PHP/Ruby/Kotlin/Swift + Markdown，Regex + 结果持久�?|
| Repo Map | �?Implemented | 真实目录�?+ 重要性评�?+ 预算裁剪 |
| 可选模�?API 网关 | �?Implemented | SSE 流式 + 状态码透传 + SingleFlight + Byte LRU + 持久化缓�?|
| 持久化缓�?| �?Implemented | SqliteCacheStore 接入 Context + Gateway，依赖哈希失�?+ TTL |
| ContextPackageCache | �?Implemented | 接入 ContextEngine.Build，CacheKey 包含所有因�?|
| 上下文与模型统一工作�?| �?Implemented | CLI `workflow completion` + API 端点 + PromptAssemblyService |
| 真实 ITokenizer 接入 | �?Implemented | CodeTokenizer 默认 + 全链路传�?Selection→Chunking) |
| �?Provider Fallback | �?Implemented | 4 端点(Chat/Models/Responses/Streaming)均支�?429/5xx 自动切换 |
| Responses API 流式 | �?Implemented | stream:true SSE passthrough + Chat SSE Usage 解析 |
| 安全出口统一 | �?Implemented | CLI/Desktop/FileExport 3 条路径全部强�?SecurityPolicyEnforcer |
| Git 仓库操作 | �?Implemented | clone/pull/status/diff + LFS 修复 + 凭据脱敏 |
| 文件导出 | �?Implemented | Markdown/JSON/File + Plan/Apply 分离 + 真实 RepoMap |
| 语义参考缓�?| 🧪 Experimental | FNV-1a 稳定哈希 + 接入 RecallPipeline + Snapshot/ContentHash 绑定 |
| Benchmark | 🧪 Experimental | CLI 使用真实 ContextEngine 度量 Recall@10/TokenReduction |
| Tree-sitter | 📦 Scaffold | regex fallback 始终可用 |
| LSP | 📦 Scaffold | 帧读取器 + 请求关联 + 审批模型就绪 |
| 插件/团队/企业 | 📦 Planned | 签名验证 + 权限隔离 + 企业策略模型已预�?|

**图例**：✅ Implemented = 已实现且有测�?�?🧪 Experimental = 有原型需更多验证 �?📦 Scaffold = 有代码骨�?�?📦 Planned = 仅设�?
---

## 技术栈

| 区域 | 技术选型 |
|------|----------|
| 主语言 | C# / .NET 9 (LTS) |
| SDK | 9.0.313 (global.json 锁定) |
| 数据�?| SQLite + FTS5 (Microsoft.Data.Sqlite) |
| 语法解析 | Regex + [GeneratedRegex] 源生成器 v2.0 |
| Web UI | ASP.NET Core 最�?API + 静态文�?|
| Gateway | HttpListener + OpenAI 兼容协议 + SSE |
| 发布 | 单文件自包含 (PublishSingleFile + SelfContained) |
| 测试 | xUnit �?847 测试通过 |
| 代码质量 | TreatWarningsAsErrors + Nullable + AnalysisLevel latest-recommended |
| CI/CD | GitHub Actions �?Ubuntu + Windows + macOS 三平�?|
| 行尾�?| .gitattributes 强制 LF（全平台一致） |
| 许可�?| MIT |

---

## 项目结构

```
CacheHub/
├── CacheHub.sln                    # 解决方案文件
├── Directory.Build.props           # 全局构建属�?├── global.json                     # .NET SDK 版本锁定
├── .gitattributes                  # 强制 LF 行尾�?├── install.ps1 / install.sh        # 安装脚本（均支持 --skip-tests/-SkipTests�?�?├── src/                            # 源代码（8 个项目）
�?  ├── CacheHub.Core/              # 领域核心：模型、错误、标识符、安全、Tokenizer
�?  ├── CacheHub.Storage/           # 存储层：SQLite�? 个迁移�? 个仓储、FTS5、CacheStore
�?  ├── CacheHub.Indexing/          # 索引层：扫描、忽略规则�? 语言解析�?v2.0、RepoMap、Reconciler
�?  ├── CacheHub.Context/           # 上下文层�?2 源召回、排序、锚点分块、预算验证、引擎、缓�?�?  ├── CacheHub.Gateway/           # 可选网关：Server、Provider 路由、SSE 流式（独立项目）
�?  ├── CacheHub.Cli/               # CLI 入口�?1 个命令组�?5 个子命令、单文件发布
�?  └── CacheHub.Desktop/           # Web UI：最�?API + 17 个路�?+ Bearer 认证 + 6 个页�?�?├── tests/
�?  └── CacheHub.Tests/             # 847 测试（单�?+ 集成 + 安全 + Gate 回归 + Benchmark�?�?├── integration/                    # Agent 集成套件
�?  ├── skills/universal/           # Universal Skill（通用技能）
�?  ├── protocol/                   # API 协议文档
�?  ├── tutorials/                  # 教程
�?  ├── examples/                   # 3 �?Agent 示例（Codex / Claude Code / Shell�?�?  └── templates/                  # 系统提示词模�?�?├── Docs/                           # 项目文档
�?  ├── INSTALL.md / USAGE.md / ARCHITECTURE.md
�?  ├── adr/                        # 5 个架构决策记�?(ADR-0001~0005)
�?  ├── specs/                      # Context Package Manifest Schema v1
�?  └── ai/                         # AI 开发状�?�?├── AGENTS.md                       # Agent 接入指南
├── CHANGELOG.md                    # 变更日志
├── CONTRIBUTING.md                 # 贡献指南
├── SECURITY.md                     # 安全策略
├── .cachehubignore.example          # 忽略规则示例
└── LICENSE                         # MIT 许可�?```

**依赖链清�?*：Core �?Storage, Indexing, Gateway；Context �?Core + Storage + Indexing；CLI/Desktop �?全部。无循环依赖�?
---

## 快速开�?
### 安装

```bash
# 1. 克隆仓库
git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub

# 2. 一键安装（自动构建 + 测试 + 发布�?# Windows:
./install.ps1
# Linux/macOS:
./install.sh

# 3. 验证
cachehub version
cachehub capabilities
cachehub integration verify
```

> 📖 详细安装步骤�?[安装手册](Docs/INSTALL.md)，AI Agent 可直接阅读该文件完成全自动安装�?
### 30 秒上�?
```bash
# 导入项目
cachehub workspace import /path/to/your/project

# 构建索引（解析代码结�?+ FTS5 + 符号表）
cachehub index build --id=<workspace-id>

# 根据任务构建上下文（AI 会自动选出相关代码�?cachehub context build --workspace=<id> --task="Fix the token refresh logic in AuthService" --output=json

# 导出�?Markdown，直接粘贴给 AI
cachehub context export --id=<context-id> --format=markdown

# 查看选了什么、为什么�?cachehub context explain --id=<context-id>

# AI �?还缺 utils.ts"？按需扩展
cachehub context expand --id=<context-id> --file=src/utils.ts --reason="Need utility functions"
```

### Web UI

```bash
dotnet run --project src/CacheHub.Desktop
# 浏览器访�?http://localhost:5099
# API Token 打印在终端中，所有请求需 Authorization: Bearer <token>
```

---

## CLI 命令参�?
| 命令 | 子命�?| 功能 |
|------|--------|------|
| `capabilities` | �?| 能力发现（版本、协议、已启用功能�?|
| `workspace` | `import` / `list` / `status` / `remove` | 工作区管�?|
| `index` | `build` / `status` / `verify` / `refresh` | 索引构建/状�?校验/增量刷新 |
| `context` | `build` / `inspect` / `list` / `export` / `expand` / `feedback` / `explain` | 上下文包全生命周�?|
| `detect` | �?| 项目类型检�?|
| `gateway` | `start` / `status` / `stop` | 可选模�?API 网关 |
| `config` | `show` / `init` / `set` | 配置管理 |
| `stats` | �?| 使用统计 |
| `repo` | `inspect` / `clone` / `status` / `diff` / `pull` | Git 仓库操作（安全模式） |
| `integration` | `verify` | 安装验证 |
| `version` | �?| 版本信息 |

**全局选项**：`--output=json` / `--json`

---

## Local API

17 �?REST API 路由（所�?`/api/` 路由需 Bearer Token 认证）：

| 路由 | 方法 | 功能 |
|------|------|------|
| `/api/v1/capabilities` | GET | 能力发现 |
| `/api/v1/workspaces` | GET / POST | 列表 / 导入 |
| `/api/v1/workspaces/{id}` | GET / DELETE | 状�?/ 删除 |
| `/api/v1/workspaces/{id}/index` | POST | 索引构建（后台批量事务） |
| `/api/v1/workspaces/{id}/contexts` | GET | 上下文包列表 |
| `/api/v1/workspaces/{id}/export` | POST | 文件导出（含真实 RepoMap�?|
| `/api/v1/context/build` | POST | 构建上下文包 |
| `/api/v1/context/{id}` | GET | 检查上下文�?|
| `/api/v1/context/{id}/expand` | POST | 扩展上下文（生成子修订包�?|
| `/api/v1/context/{id}/feedback` | POST | 提交反馈 |
| `/api/v1/context/{id}/explain` | GET | 解释选择/遗漏/预算 |
| `/api/v1/context/{id}/payload` | GET | 获取 Payload（含 blockedFiles�?|
| `/api/v1/search` | GET | FTS5 全文搜索 |
| `/api/v1/outline` | GET | 代码大纲 |
| `/api/v1/stats` | GET | 使用统计 |

> 📖 完整 API 协议文档�?[integration/protocol/context-api.md](integration/protocol/context-api.md)

---

## Agent 集成

三种方式，任�?AI 编程工具均可接入�?
### CLI 调用（最简单）

```bash
cachehub context build --workspace=<id> --task="当前任务" --output=json | jq '.id'
cachehub context export --id=<ctx-id> --format=markdown
```

### Local API（程序化集成�?
```bash
curl -X POST http://localhost:5099/api/v1/context/build \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"workspaceId":"<id>","task":"Fix login bug"}'
```

### 文件导出（离线场景）

```bash
cachehub context export --id=<ctx-id> --format=file
# 生成: .cachehub/workspace.json, latest-context.manifest.json, latest-context.md, repomap.md
```

> 📖 详见 [AGENTS.md](AGENTS.md) �?[集成教程](Docs/USAGE.md#agent-集成)

---

## 测试覆盖�?
| 类别 | 数量 | 说明 |
|------|------|------|
| 单元 + 集成测试 | 847 通过 | 覆盖全部模块 |
| 跳过 | 2 | 需真实 Git 环境 |
| Gate 回归测试 | 60+ | R4-R15 阶段门验�?|
| 真实 SQLite 集成测试 | 10+ | index→context→payload 完整闭环 |
| 安全测试 | 40+ | 路径遍历、symlink 逃逸、XSS、密钥扫描、Offline 模式 |
| Benchmark 度量 | 3 | 真实 Context Engine 指标（非 mock�?|
| **总计** | **774** | |

CI �?**Ubuntu + Windows + macOS** 三平台上运行 `build + test + format`，每�?push �?PR 自动触发�?
---

## 文档导航

| 文档 | 说明 |
|------|------|
| [安装手册](Docs/INSTALL.md) | 完整安装步骤，AI Agent 可读 |
| [使用教程](Docs/USAGE.md) | CLI / Web UI / Agent 集成详细教程 |
| [架构设计](Docs/ARCHITECTURE.md) | 系统架构、模块设计、数据流 |
| [Agent 接入指南](AGENTS.md) | AI Agent 快速接�?|
| [API 协议](integration/protocol/context-api.md) | 17 路由 + Bearer 认证 + Gateway API |
| [变更日志](CHANGELOG.md) | 版本历史 |
| [贡献指南](CONTRIBUTING.md) | 如何参与开�?|
| [安全策略](SECURITY.md) | 安全模型和漏洞报�?|
| [Context Package Schema](Docs/specs/context-package.manifest.v1.json) | JSON Schema v1 |
| [ADR 记录](Docs/adr/) | 5 个架构决策记�?|

---

## 项目状�?
CacheHub 当前处于 **Pre-Alpha** 阶段。核心链路已通过 847 个测试验证，但不建议用于生产环境或处理敏感代码�?
**已完�?*�?- V1.0 路线�?R0-R9（安全止血 �?索引可信 �?上下文正�?�?通用协议 �?真实 Benchmark �?Gateway �?缓存 �?Semantic �?LSP �?生态）
- V2.0 路线�?R4-R15�?8 个任务：知识召回主链 �?精准压缩 �?增量索引 �?持久缓存 �?生产 Gateway �?统一工作�?�?语义参�?�?安全强制 �?真实 Benchmark �?GUI/发布 �?Tree-sitter/LSP �?生�?企业�?- 全面审计修复�?2 个问题全部解决（11 P0 + 31 P1 + 20 P2�?- 架构重构：Gateway �?Core 拆分为独立项目，依赖链清晰化

**后续方向**�?- Tree-sitter 原生库集成（替代 regex fallback�?- LSP 独立进程（精确定�?引用�?- Provider 路由/预算执行器完�?- Semantic 本地 Embedding 模型
- 跨平台安装包和签�?- 插件系统、团队共享索引、企业策�?
---

## 许可�?
[MIT License](LICENSE) �?Copyright (c) 2026 CacheHub Contributors

---

<div align="center">

**如果这个项目对你有帮助，请给一�?�?Star�?*

[报告问题](https://github.com/chenfengyimei/CacheHub/issues) · [发起讨论](https://github.com/chenfengyimei/CacheHub/discussions) · [查看文档](Docs/)

</div>
