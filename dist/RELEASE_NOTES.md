# CacheHub v0.2.0-prealpha 发行说明

**发布日期**：2026-08-11
**版本**：0.2.0-prealpha
**平台**：Windows x64（自包含，无需安装 .NET 运行时）

---

## 这是什么？

CacheHub 是一个**本地代码上下文路由器**和**可选模型 API 网关**。

它为 AI 编程助手智能索引代码库，根据当前任务精准挑选最相关的代码片段，生成受 Token 预算严格约束的 Context Package，通过 CLI / HTTP API / 文件协议交给任何 AI Agent。

**核心价值**：不降低任务成功率的前提下，显著减少无关上下文 Token。

---

## 包内容

```
CacheHub-0.2.0-prealpha-win-x64/
├── cli/
│   └── cachehub.exe          # CLI 单文件可执行程序（自包含 .NET 10 运行时，~39MB）
├── desktop/                  # Web UI 服务（ASP.NET Core 最小 API）
├── README.md                 # 项目说明
├── LICENSE                   # MIT 许可证
├── CHANGELOG.md              # 完整变更日志
└── RELEASE_NOTES.md          # 本文件
```

---

## 快速开始

### 1. CLI

```bash
# 将 cli/ 目录加入 PATH，或直接使用完整路径
cli\cachehub.exe version
cli\cachehub.exe capabilities
cli\cachehub.exe workspace import C:\path\to\your\project
cli\cachehub.exe index build --id=<workspace-id>
cli\cachehub.exe context build --workspace=<id> --task="Fix the token refresh logic" --output=json
cli\cachehub.exe context export --id=<context-id> --format=markdown
```

### 2. Web UI

```bash
# 启动 Desktop 服务
desktop\CacheHub.Desktop.exe
# 浏览器访问 http://localhost:5099
# API Token 会打印在终端中
```

---

## 功能矩阵

| 功能 | 状态 |
|------|------|
| 工作区导入与管理 | ✅ 已实现 |
| 全量索引构建（12 语言解析器） | ✅ 已实现 |
| 增量索引刷新 | ✅ 已实现 |
| FTS5 全文搜索 + BM25 | ✅ 已实现 |
| 12 源可组合召回引擎 | ✅ 已实现 |
| 7 维排序 + 锚点精准分块 | ✅ 已实现 |
| Token 预算管理 | ✅ 已实现 |
| 安全策略出口强制 | ✅ 已实现 |
| 密钥扫描（5 种模式） | ✅ 已实现 |
| CLI（23 命令组） | ✅ 已实现 |
| Desktop Web UI（17 路由） | ✅ 已实现 |
| 可选模型 API 网关 | ✅ 已实现 |
| 持久化缓存 | ✅ 已实现 |
| 语义参考缓存 | 🧪 实验性 |
| Tree-sitter | 📦 脚手架 |
| LSP | 📦 脚手架 |
| 插件 / 团队 / 企业 | 📦 规划中 |

---

## 测试

- 997 测试通过，0 失败，2 跳过（需真实 Git 环境）
- 三平台 CI 全绿（Ubuntu + Windows + macOS）
- 60+ Gate 回归测试
- 40+ 安全测试

---

## 已知限制

- **Pre-Alpha 阶段**：核心链路已通过测试验证，但不建议用于生产环境或处理敏感代码
- **Desktop 无法单文件发布**：.NET 10 单文件压缩 bug，Desktop 以目录形式发布
- **Tree-sitter / LSP**：仅有脚手架，当前使用 Regex 解析器
- **Semantic**：使用 FNV-1a 哈希相似度，非真实 Embedding 模型
- **Benchmark Matrix Agent 层**：检索层就绪，Agent 闭环需配置真实 Provider API Key

---

## 许可证

MIT License — Copyright (c) 2026 CacheHub Contributors
