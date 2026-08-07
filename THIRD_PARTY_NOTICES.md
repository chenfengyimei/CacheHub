# Third Party Notices

CacheHub 使用的第三方软件和库。

## 当前依赖

| 库/工具 | 版本 | 许可证 | 用途 | 复制代码 |
|---------|------|--------|------|----------|
| Microsoft.Data.Sqlite | 9.0.x | MIT | SQLite ADO.NET 提供程序 | 否 |
| SQLite | 3.x | Public Domain | 嵌入式数据库（含 FTS5 扩展） | 否 |
| ASP.NET Core | 9.0 | MIT | Web UI 和 Minimal API | 否 |
| xUnit | 2.9.x | MIT | 单元测试框架 | 否 |
| coverlet.collector | 6.0.x | MIT | 代码覆盖率收集 | 否 |
| Microsoft.NET.Test.Sdk | 17.12.x | MIT | 测试 SDK | 否 |
| Microsoft.AspNetCore.Mvc.Testing | 9.0.x | MIT | Web 应用集成测试 | 否 |

## 计划依赖（尚未引入）

| 库/工具 | 许可证 | 用途 | 状态 |
|---------|--------|------|------|
| Tree-sitter | MIT | 多语言语法解析（替代 regex fallback） | 📦 Scaffold — regex 始终可用 |
| ripgrep | MIT/Unlicense | 实时磁盘搜索加速 | 📦 可选优化 |
| Avalonia UI | MIT | 跨平台桌面 UI（替代 Web UI） | 📦 未来考虑 |

## .NET 运行时

CacheHub 基于 .NET 9 (LTS) 构建，使用以下 .NET 组件：
- .NET SDK 9.0.313
- .NET Runtime 9.0
- ASP.NET Core Runtime 9.0

.NET 基于 [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) 发布。

## 许可证

CacheHub 本身基于 [MIT License](LICENSE) 发布。

详细许可证研究记录见 [Docs/research/license-register.md](Docs/research/license-register.md)。
