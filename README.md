# AI_KV

> 让任何 AI 编程工具每次只读取真正需要的代码。

AI_KV 是一个通用的本地代码上下文路由器和可选模型 API 网关：它持续维护版本感知的项目索引，根据当前任务生成可解释、受 Token 预算限制的最小 Context Package，并允许任何客户端通过稳定协议使用。

## 核心特性

- **版本感知索引**：增量文件扫描、内容哈希、FTS5 全文检索
- **Context Package**：可解释、可复现、Token 受限的最小代码上下文
- **Agent 无关协议**：CLI、Local API、文件导出、可选 Gateway
- **多语言解析**：Tree-sitter 语法结构 + 可选 LSP 精确语义
- **安全优先**：默认只读、本地运行、最少外发

## 技术栈

| 区域 | 技术 |
| --- | --- |
| 主语言 | C# / .NET 9 |
| 数据库 | SQLite + FTS5 |
| 语法解析 | Tree-sitter |
| 实时搜索 | ripgrep |
| 桌面 UI | Avalonia UI（后续阶段） |

## 快速开始

```bash
# 构建和测试
dotnet restore
dotnet build AI_KV.sln -c Release
dotnet test AI_KV.sln

# CLI 示例（开发中）
aikv capabilities --output json
aikv workspace import --path . --output json
aikv context build --workspace <id> --task "..." --output json
```

## 项目状态

当前阶段：P00 - 协议冻结、研究与仓库初始化

参见 [开发路线图](docs/ai/ROADMAP_STATUS.md) 了解详细进度。

## 文档

- [完整项目开发总策划案 V3.0](Docs/项目开发策划案/AI_KV完整项目开发总策划案_V3.0.md)
- [AI 开发执行手册 V1.0](Docs/AI开发执行手册_开发包/AI_KV_AI开发执行手册_V1.0_开发包/AI_KV_AI开发执行手册_V1.0.md)
- [开发路线图状态](docs/ai/ROADMAP_STATUS.md)

## 许可证

待定（参见 [LICENSE](LICENSE)）
