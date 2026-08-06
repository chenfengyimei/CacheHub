# AI_KV

> 让任何 AI 编程工具每次只读取真正需要的代码。

AI_KV 是一个通用的本地代码上下文路由器和可选模型 API 网关：它持续维护版本感知的项目索引，根据当前任务生成可解释、受 Token 预算限制的最小 Context Package，并允许任何客户端通过稳定协议使用。

## 核心特性

- **版本感知索引**：增量文件扫描、内容哈希、FTS5 全文检索
- **Context Package**：可解释、可复现、Token 受限的最小代码上下文，支持持久化
- **Agent 无关协议**：CLI、Local API、文件导出、可选 Gateway
- **多语言解析**：C#/TypeScript/Python Regex 解析器 + 可选 LSP 精确语义
- **安全优先**：默认只读、本地运行、4 级外发模式 + 5 种密钥扫描
- **精确 Tokenizer**：3 种分词器（char/word/code）+ 模型注册表
- **Benchmark 框架**：6 个真实任务 + Ground Truth + 阶段门 + JSON 报告

## 技术栈

| 区域 | 技术 |
| --- | --- |
| 主语言 | C# / .NET 9 |
| 数据库 | SQLite + FTS5 |
| 语法解析 | Regex + [GeneratedRegex]（后续替换为 Tree-sitter） |
| 桌面 UI | ASP.NET Core 最小 Web（后续迁移 Avalonia） |
| Gateway | HttpListener + OpenAI-compatible |
| 测试 | xUnit，355+ 个单元测试 |

## 快速开始

```bash
# 构建
dotnet build AI_KV.sln -c Release
dotnet test AI_KV.sln

# CLI 使用
aikv capabilities --output=json
aikv workspace import /path/to/project
aikv index build --id=<workspace-id>
aikv context build --workspace=<id> --task="Fix login bug" --output=json
aikv context inspect --id=<context-id>
aikv context export --id=<context-id> --format=markdown
aikv context expand --id=<context-id> --file=src/auth.ts
aikv integration verify

# 项目检测
aikv detect /path/to/project --plan

# Gateway（可选）
aikv gateway start --provider-url=https://api.openai.com --provider-key=sk-xxx

# Web UI
dotnet run --project src/AiKv.Desktop
# 浏览器访问 http://localhost:5000
```

## CLI 命令集

| 命令 | 功能 |
|---|---|
| `capabilities` | 能力发现 |
| `workspace import/list/status/remove` | 工作区管理 |
| `index build/status/verify` | 索引构建/状态/一致性校验 |
| `context build/inspect/export/expand/feedback` | 上下文管理（持久化） |
| `detect <path> --plan` | 项目检测 + 初始化计划 |
| `gateway start/status/stop` | Gateway 服务器 |
| `integration verify` | 安装验证（5 步检查） |

## Local API

| 路由 | 方法 | 功能 |
|---|---|---|
| `/api/v1/capabilities` | GET | 能力发现 |
| `/api/v1/workspaces` | GET/POST | 工作区列表/导入 |
| `/api/v1/workspaces/{id}` | GET/DELETE | 状态/删除 |
| `/api/v1/workspaces/{id}/export` | POST | 文件导出 |
| `/api/v1/context/build` | POST | 构建上下文（持久化） |
| `/api/v1/context/{id}` | GET | 检查上下文包 |
| `/api/v1/context/{id}/expand` | POST | 扩展上下文（实际文件） |
| `/api/v1/context/{id}/feedback` | POST | 提交反馈 |
| `/api/v1/context/{id}/explain` | GET | 解释选择/潜在遗漏/预算 |

## 项目结构

```
src/
  AiKv.Core/         — 领域模型、错误、标识符、上下文、安全、Tokenizer、Gateway、Provider、Semantic、LSP、Ecosystem
  AiKv.Storage/      — SQLite、4 个迁移、Workspace/ContextPackage 仓储、FTS5 搜索
  AiKv.Indexing/     — 目录扫描、忽略规则、文件检测、3 语言解析器、Repo Map、缓存
  AiKv.Context/      — 任务解析器、召回、排序、分块、预算、选择、引擎、扩展、解释、缓存
  AiKv.Cli/          — CLI 命令（8 个命令组）
  AiKv.Desktop/      — ASP.NET Core Web UI + Local API（11 个路由）
tests/
  AiKv.Tests/        — 355+ 单元测试 + 8 个 E2E 集成测试
integration/         — Universal Skill、3 个 Agent 示例、系统提示词片段、协议文档
docs/                — Specs、4 个 ADR、AI 状态、路线图、研究台账
```

## 文档

- [完整项目开发总策划案 V3.0](Docs/项目开发策划案/AI_KV完整项目开发总策划案_V3.0.md)
- [AI 开发执行手册 V1.0](Docs/AI开发执行手册_开发包/AI_KV_AI开发执行手册_V1.0_开发包/AI_KV_AI开发执行手册_V1.0.md)
- [开发路线图状态](docs/ai/ROADMAP_STATUS.md)
- [AGENTS.md — Agent 接入指南](AGENTS.md)

## 许可证

MIT License — 见 [LICENSE](LICENSE)
