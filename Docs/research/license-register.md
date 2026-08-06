# CacheHub 开源项目许可证研究台账

> 最后更新：2026-08-06

## 研究原则

- 只学习和借鉴设计思路，不直接复制代码
- 如需引用代码片段，必须记录来源和许可证
- 所有第三方依赖必须在此台账登记

## 研究项目清单

| 项目 | 许可证 | 研究内容 | 复制代码 | 状态 |
| --- | --- | --- | --- | --- |
| Aider | Apache-2.0 | Repo Map、Token Budget、Git 集成、Prompt Cache | 否 | 计划研究 |
| GPTCache | MIT | 缓存抽象、相似度计算、淘汰策略 | 否 | 计划研究 |
| LiteLLM | MIT | Gateway、Provider 适配、模型映射 | 否 | 计划研究 |
| Portkey | MIT | Gateway 观测、Fallback、预算 | 否 | 计划研究 |
| TensorZero | Apache-2.0 | 请求统计、实验框架 | 否 | 计划研究 |
| Continue | Apache-2.0 | 代码分块、索引、IDE 集成 | 否 | 计划研究 |
| Tree-sitter | MIT | 语法解析、语法树查询 | 否 | 计划使用（NuGet 包） |
| ripgrep | MIT/Unlicense | 实时磁盘正则搜索 | 否 | 计划使用（二进制） |
| Roslyn | MIT | C# 深度语义分析（可选插件） | 否 | 计划研究 |
| SQLite | Public Domain | 嵌入式数据库 | 否 | 计划使用 |
| FTS5 | Public Domain | 全文检索（SQLite 扩展） | 否 | 计划使用 |
| Avalonia UI | MIT | 桌面 UI 框架 | 否 | 计划使用（P07+） |

## 计划使用的 NuGet 包

| 包 | 许可证 | 用途 | 引入阶段 |
| --- | --- | --- | --- |
| Microsoft.Data.Sqlite | MIT | SQLite ADO.NET | P01 |
| Microsoft.Data.Sqlite.Core | MIT | SQLite 不含原生库 | P01 |
| SQLitePCLRaw.bundle_e_sqlite3 | MIT | SQLite 原生库 | P01 |

## 注意事项

- Tree-sitter 的 .NET 绑定需在 P03 阶段评估具体包和许可证
- ripgrep 可作为独立二进制分发或进程调用，不嵌入源码
- 所有研究记录应包含：源文件路径、学到的设计、是否复制代码
