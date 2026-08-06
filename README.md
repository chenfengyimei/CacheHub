# CacheHub

> ⚠️ **Pre-Alpha** — 本项目处于早期开发阶段，核心链路尚未完成验证，不建议用于生产环境或处理敏感代码。

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
| 工作区导入与管理 | ✅ Implemented | 注册本地目录为工作区 |
| 全量索引构建（FTS5 + files 表） | ✅ Implemented | 全量扫描写入 FTS5 和元数据表 |
| 增量索引刷新 | 🧪 Experimental | 框架已搭建，增量逻辑尚在开发 |
| FTS5 全文搜索 | ✅ Implemented | 绑定快照的全文搜索 |
| FTS5 接入 Context Recall | 🧪 Experimental | Recall 尚未正式使用 FTS |
| 任务解析（英文） | ✅ Implemented | 确定性规则解析英文任务 |
| 任务解析（中文/Unicode） | 🧪 Experimental | 中文分词尚不完整 |
| 多源召回（路径/符号/FTS/Git Diff） | 🧪 Experimental | 符号召回和 FTS 召回尚未接入 |
| 排序引擎 | 🧪 Experimental | 多数特征维度尚未实现 |
| 智能分块 | 🧪 Experimental | 当前按固定行窗口切块 |
| Token 预算管理 | 🧪 Experimental | 预算公式不完整，Tokenizer 为估算 |
| 上下文扩展（按文件） | ✅ Implemented | 支持按文件扩展上下文 |
| 上下文扩展（按符号） | 🧪 Experimental | symbol 参数被当作文件路径 |
| 可解释性 | 🧪 Experimental | 展示选择理由，缺 feature breakdown |
| 安全策略（4 级模式） | 🧪 Experimental | 策略定义存在但未在出口强制执行 |
| 密钥扫描 | ✅ Implemented | 5 种密钥模式扫描 |
| CLI 接入 | ✅ Implemented | 21 个命令组 |
| Local API | 🧪 Experimental | 存在 P0 安全问题，修复中 |
| 文件导出 | ✅ Implemented | Markdown/JSON/File 导出 |
| Desktop Web UI | 🧪 Experimental | 基础页面可用，缺索引闭环 |
| 可选模型 API 网关 | 🧪 Experimental | 非流式原型，存在安全问题 |
| 代码解析（C#/TS/Python/Markdown） | 🧪 Experimental | Regex 解析器，未接入主索引 |
| Repo Map | 🧪 Experimental | 生成器存在但预算参数未生效 |
| Git 仓库操作 | 🧪 Experimental | clone/pull/status/diff 基础原型 |
| Benchmark | 🧪 Experimental | 当前为模拟数据，非真实实验 |
| Semantic Search | 📦 Scaffold | 仅接口和内存向量表 |
| LSP | 📦 Scaffold | 生命周期伪实现 |
| Provider/路由/预算 | 📦 Scaffold | 数据模型占位 |
| 插件/团队/企业 | 📦 Planned | 仅 DTO 预留 |

**图例**：✅ Implemented = 已实现且有测试 ｜ 🧪 Experimental = 有原型但未完成 ｜ 📦 Scaffold = 有代码骨架 ｜ 📦 Planned = 仅设计

### 解决了什么问题？

| 痛点 | CacheHub 方案 |
|------|---------------|
| AI Agent 每次都扫全仓库，Token 浪费严重 | 版本感知索引 + 实验性排序引擎，选相关代码 |
| 上下文太大导致模型"失忆" | Token 预算管理器，硬上限 + 安全边界（公式尚在完善） |
| 选了什么、为什么选，黑盒不可知 | ContextExplainer，选择理由和排除原因可查 |
| 每个工具的上下文方案各不相同 | Agent 无关协议（CLI / Local API / 文件导出），任何工具可接入 |
| 密钥泄漏风险 | 安全策略 + 5 种密钥扫描（出口强制执行尚在开发中） |

---

## 核心能力

### 1. 版本感知索引引擎

- **全量文件扫描**：异步流式枚举，深度/数量/大小限制 + 符号链接保护
- **内容哈希**：分层哈希策略（小文件全量 SHA-256，大文件快速指纹）
- **FTS5 全文检索**：版本感知的全文搜索，绑定 IndexSnapshotId
- **文件监视器**：防抖事件队列 + 溢出检测 + 一致性校验器（增量刷新尚未实现）
- **忽略规则引擎**：系统默认 > `.gitignore` > `.cachehubignore` > 用户规则，四层合并（.gitignore 语义不完整）
- **增量索引**：🧪 尚未实现，refresh 命令当前返回 Not yet implemented

### 2. 上下文引擎 (Context Engine)

- **任务解析器**：确定性规则解析（不依赖 LLM），从自然语言任务描述中提取关键词、路径、符号（当前仅支持英文）
- **多源召回**：路径匹配 / 关键词搜索 / Git Diff / 当前文件，四路召回（符号召回和 FTS 召回尚未接入）
- **排序引擎**：加权评分，权重可版本化（多数特征维度尚未实现）
- **智能分块**：行窗口分块 + 重叠控制（语法块感知分块尚在开发中）
- **Token 预算管理**：模型窗口 / Agent 预留 / 响应预留 / 目标 / 硬上限 / 安全边界（预留字段尚未参与计算）
- **5 种选择模式**：Full / Chunks / Outline / DeterministicSummary / Metadata
- **上下文扩展**：按文件扩展，追踪父上下文 + 增量 Token（按符号扩展尚未实现）
- **可解释性**：选择理由 / 预算驱逐原因 / 潜在遗漏检测（feature breakdown 尚不完整）

### 3. 多语言代码解析

> 🧪 当前使用 Regex 解析器，尚未接入 Tree-sitter。解析结果尚未持久化到主索引。

| 语言 | 解析器 | 提取内容 |
|------|--------|----------|
| C# | CSharpRegexParser | namespace/class/method/property/import + 启发式调用 |
| TypeScript/JavaScript | TypeScriptRegexParser | export class/interface/function/import + 调用 |
| Python | PythonRegexParser | class/def/import/decorator + 调用 |
| Markdown | MarkdownParser | 标题、代码块、配置键 |
| 纯文本 | TextParser | 通用结构提取 |

- **代码关系**：语法 / 启发式 / 语义三级置信度 (0..1)
- **确定性大纲**：按行号 + 名称稳定排序
- **Repo Map**：生成器存在但预算参数未生效，目录树和优先级尚未实现
- **解析器缓存**：哈希 + ParserId + 版本键控

### 4. 安全策略

- **4 级外发模式**：Standard / Restricted / PreviewRequired / Offline（策略执行尚在开发中）
- **密钥扫描**：API Key / 密码 / 私钥 / 连接字符串 / Bearer Token
- **敏感文件检测**：`.env`、`.pem`、`.key`、`id_rsa`、`credentials.json` 等
- **发送前检查**：路径 + 内容 + 模式三重检查（Payload 出口强制执行尚在开发中）

### 5. 可选模型 API 网关

> 🧪 Gateway 当前为非流式转发原型，存在安全问题（无认证、状态码固定 200、缓存安全边界不足），修复中。

- **OpenAI 兼容**：HttpListener 回环转发
- **安全缓存**：仅缓存安全请求（低温度、无工具、无流式）——缓存安全边界待修复
- **SingleFlight**：并发请求去重（存在竞态条件）
- **SSE 流式**：尚未实现
- **用量统计**：请求数 / 缓存命中率 / Token 节省 / 平均延迟

### 6. Agent 无关协议

CacheHub 提供 4 种接入方式，任何 AI 编程工具均可使用：

```
┌─────────────────────────────────────────────────┐
│              AI Agent (任何工具)                  │
│  Codex / Claude Code / Cursor / 自定义 Agent     │
└──────────┬──────────┬──────────┬────────────────┘
           │          │          │
      CLI 命令    Local API   文件导出
      cachehub    :5000/api   .cachehub/
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
| 语法解析 | Regex + [GeneratedRegex] 源生成器 |
| Web UI | ASP.NET Core 最小 API + 静态文件 |
| Gateway | HttpListener + OpenAI 兼容协议 |
| 发布 | 单文件自包含 (PublishSingleFile + SelfContained) |
| 测试 | xUnit + coverlet 覆盖率 |
| 代码质量 | TreatWarningsAsErrors + Nullable + AnalysisLevel latest-recommended |
| CI/CD | GitHub Actions |
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
│   ├── CacheHub.Storage/           # 存储层：SQLite、5 个迁移、Workspace/ContextPackage 仓储、FTS5 搜索
│   ├── CacheHub.Indexing/          # 索引层：目录扫描、忽略规则、文件检测、4 语言解析器、RepoMap、缓存
│   ├── CacheHub.Context/           # 上下文层：任务解析、召回、排序、分块、预算、选择、引擎、扩展、解释
│   ├── CacheHub.Cli/               # CLI 入口：21 个命令组、55 个子命令、单文件发布
│   └── CacheHub.Desktop/           # Web UI：ASP.NET Core 最小 API + 17 个 Local API 路由 + 6 个页面
│
├── tests/
│   └── CacheHub.Tests/             # 379+ 单元测试 + 8 个 E2E 集成测试 + 5 个真实场景测试
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
# 浏览器访问 http://localhost:5000
```

---

## CLI 命令参考

| 命令 | 子命令 | 功能 |
|------|--------|------|
| `capabilities` | — | 能力发现（版本、协议、已启用功能） |
| `workspace` | `import` / `list` / `status` / `remove` | 工作区管理 |
| `index` | `build` / `status` / `verify` | 索引构建/状态/一致性校验 |
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

Web UI 启动后提供 17 个 REST API 路由：

| 路由 | 方法 | 功能 |
|------|------|------|
| `/api/v1/capabilities` | GET | 能力发现 |
| `/api/v1/workspaces` | GET / POST | 工作区列表 / 导入 |
| `/api/v1/workspaces/{id}` | GET / DELETE | 状态 / 删除 |
| `/api/v1/workspaces/{id}/contexts` | GET | 上下文包列表 |
| `/api/v1/workspaces/{id}/export` | POST | 文件导出 |
| `/api/v1/context/build` | POST | 构建上下文包 |
| `/api/v1/context/{id}` | GET | 检查上下文包 |
| `/api/v1/context/{id}/expand` | POST | 扩展上下文 |
| `/api/v1/context/{id}/feedback` | POST | 提交反馈 |
| `/api/v1/context/{id}/explain` | GET | 解释选择 / 遗漏 / 预算 |
| `/api/v1/context/{id}/payload` | GET | 获取完整 Payload |
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
# 通过 HTTP API 集成
curl -X POST http://localhost:5000/api/v1/context/build \
  -H "Content-Type: application/json" \
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
| 单元测试 | 379 通过 | 覆盖模型和工具函数 |
| 跳过（需真实 Git 环境） | 2 | |
| 端到端集成测试 | 8 | 多为内存模拟，真实闭环测试待补充 |
| 真实场景测试 | 5 | 当前为模拟数据 |
| **总计** | **381** | |

> ⚠️ 当前 E2E 和 Benchmark 测试大量使用内存模拟数据，不能完全验证真实 CLI/API/SQLite/Git 闭环。改进测试可信度是 R0-R4 阶段的目标之一。

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
