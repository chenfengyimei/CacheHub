# CacheHub 使用教程

本教程详细介绍 CacheHub 的所有功能和使用方法。

---

## 目录

1. [基本概念](#基本概念)
2. [CLI 使用](#cli-使用)
3. [Web UI 使用](#web-ui-使用)
4. [Agent 集成](#agent-集成)
5. [配置管理](#配置管理)
6. [安全策略](#安全策略)
7. [上下文包协议](#上下文包协议)
8. [高级用法](#高级用法)

---

## 基本概念

### 工作区 (Workspace)

工作区是 CacheHub 管理的最小单位。一个工作区对应一个本地代码仓库或项目目录。

```
工作区 (Workspace)
├── 索引快照 (IndexSnapshot) — 版本感知的文件索引
├── 上下文包 (ContextPackage) — 根据任务生成的最小代码上下文
└── 反馈记录 (Feedback) — Agent 使用后的反馈
```

### 上下文包 (Context Package)

上下文包是 CacheHub 的核心输出。它包含：

- **Manifest**：元数据（任务、排序、预算、安全检查、选中文件列表）
- **Payload**：实际代码内容（按模式提供：完整/分块/大纲/摘要/元数据）
- **Explanation**：选择理由、排除原因、潜在遗漏

### 工作流程

```
导入工作区 → 构建索引 → 构建上下文 → 导出/扩展 → 提交反馈
```

---

## CLI 使用

### 1. 能力发现

```bash
cachehub capabilities
cachehub capabilities --output=json
```

输出当前版本支持的功能、协议版本、Schema 版本和已知限制。

### 2. 工作区管理

```bash
# 导入项目
cachehub workspace import /path/to/project
cachehub workspace import /path/to/project --name="My App"

# 列出所有工作区
cachehub workspace list

# 查看状态
cachehub workspace status --id=abc123

# 移除工作区（仅删除 CacheHub 数据，不删源码）
cachehub workspace remove --id=abc123
```

### 3. 索引管理

```bash
# 构建索引
cachehub index build --id=<workspace-id>

# 查看索引状态
cachehub index status --id=<workspace-id>

# 一致性校验（对比磁盘和索引）
cachehub index verify --id=<workspace-id>
```

索引构建会：
- 枚举目录（遵守 `.gitignore` + `.cachehubignore` 规则）
- 检测文件类型（30+ 扩展名映射）
- 计算内容哈希（SHA-256）
- 解析代码结构（C#/TS/Python/Markdown）
- 建立 FTS5 全文索引

### 4. 上下文构建

```bash
# 基本用法
cachehub context build \
  --workspace=<id> \
  --task="Fix the token refresh logic in auth module" \
  --output=json

# 结合 Git Diff（只看变更文件）
cachehub context build \
  --workspace=<id> \
  --task="Review recent changes" \
  --git-diff \
  --output=json

# 指定模型（影响 Token 预算）
cachehub context build \
  --workspace=<id> \
  --task="Add unit tests for UserService" \
  --model=gpt-4 \
  --output=json
```

### 5. 上下文检查与导出

```bash
# 检查上下文包详情
cachehub context inspect --id=<ctx-id> --output=json

# 列出工作区的所有上下文包
cachehub context list --workspace=<id>

# 导出为 Markdown（可直接粘贴给 AI）
cachehub context export --id=<ctx-id> --format=markdown

# 导出为 JSON
cachehub context export --id=<ctx-id> --format=json

# 导出到 .cachehub/ 目录（协议文件）
cachehub context export --id=<ctx-id> --format=file
```

### 6. 上下文扩展

当 Agent 发现上下文缺少某个文件时，可以按需扩展：

```bash
# 按文件路径扩展
cachehub context expand \
  --id=<ctx-id> \
  --file=src/auth/token-refresh.ts \
  --reason="Missing token refresh implementation"

# 按符号名称扩展
cachehub context expand \
  --id=<ctx-id> \
  --symbol=TokenRefresher \
  --reason="Need to see the TokenRefresher class"
```

### 7. 提交反馈

Agent 完成任务后提交反馈，帮助 CacheHub 改进排序：

```bash
# 创建反馈文件
cat > feedback.json << 'EOF'
{
  "clientId": "claude-code",
  "taskCompleted": true,
  "missingContextReported": false,
  "filesActuallyRead": [
    "src/auth/token.ts",
    "src/auth/refresh.ts",
    "src/config.ts"
  ]
}
EOF

# 提交
cachehub context feedback --id=<ctx-id> --file=feedback.json
```

### 8. 项目检测

```bash
# 检测项目类型
cachehub detect /path/to/project

# 生成初始化计划
cachehub detect /path/to/project --plan --output=json
```

支持检测 9 种生态：Node.js / Python / .NET / Go / Rust / Java / Unity / Flutter / Docker。

### 9. Git 仓库操作

```bash
# 解析 Git URL
cachehub repo inspect https://github.com/user/repo.git

# 安全克隆（不执行 hooks、不拉取 LFS、不初始化子模块）
cachehub repo clone https://github.com/user/repo.git ./local-repo --depth 1

# 查看状态
cachehub repo status /path/to/repo

# 查看变更文件
cachehub repo diff /path/to/repo

# 安全拉取（仅 fast-forward，不自动合并）
cachehub repo pull /path/to/repo
```

### 10. 搜索

```bash
# FTS5 全文搜索
cachehub search --workspace=<id> --query="authentication"
```

### 11. 代码大纲

```bash
# 生成文件大纲
cachehub outline --file=src/auth/token.ts
```

### 12. 使用统计

```bash
cachehub stats
cachehub stats --output=json
```

---

## Web UI

启动 Web UI：

```bash
dotnet run --project src/CacheHub.Desktop
# 访问 http://localhost:5099
# API Token 打印在终端中，所有 /api/ 请求需 Authorization: Bearer <token>
```

### 页面功能

| 页面 | 功能 |
|------|------|
| Workspaces | 工作区列表、导入、状态查看 |
| Context | 上下文构建、检查、解释、扩展 |
| History | 历史上下文包浏览 |
| Search | FTS5 全文搜索 |
| Integration | Agent 集成信息 |
| Settings | 配置管理 |

### Local API

所有 Web UI 功能都通过 REST API 提供，详见 [README 中的 API 表](../README.md#local-api)。

---

## Agent 集成

### 方式一：CLI 调用

最简单的集成方式，Agent 直接调用 `cachehub` CLI：

```bash
# 1. 发现能力
cachehub capabilities --output=json

# 2. 导入工作区
cachehub workspace import /path/to/project --output=json

# 3. 构建索引
cachehub index build --id=<workspace-id>

# 4. 构建上下文
cachehub context build --workspace=<id> --task="当前任务描述" --output=json

# 5. 导出为 Markdown
cachehub context export --id=<ctx-id> --format=markdown

# 6. 如需扩展
cachehub context expand --id=<ctx-id> --file=<path> --reason="需要查看实现"
```

### 方式二：Local API

通过 HTTP API 集成，适合程序化调用：

```bash
# 构建上下文（需 Bearer Token 认证）
curl -X POST http://localhost:5099/api/v1/context/build \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"workspaceId":"<id>","task":"Fix login bug"}'

# 获取 Payload
curl -H "Authorization: Bearer <token>" \
  http://localhost:5099/api/v1/context/<ctx-id>/payload

# 扩展上下文
curl -X POST http://localhost:5099/api/v1/context/<ctx-id>/expand \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"file":"src/auth.ts","reason":"Missing auth"}'
```

### 方式三：文件导出协议

适合离线场景，Agent 读取 `.cachehub/` 目录下的文件：

```bash
cachehub context export --id=<ctx-id> --format=file
```

生成文件：
```
.cachehub/
├── workspace.json                    # 工作区信息
├── latest-context.manifest.json      # 上下文包 Manifest
├── latest-context.md                  # 上下文包 Markdown（可直接喂给 AI）
└── repomap.md                         # 代码库地图
```

### 现成集成示例

CacheHub 提供 3 个 Agent 集成示例：

| Agent | 示例路径 | 说明 |
|-------|----------|------|
| Codex | `integration/examples/codex/` | codex-config.yaml 工具配置 |
| Claude Code | `integration/examples/claude-code/` | JSON 工具配置 + AGENTS.md 片段 |
| Shell Agent | `integration/examples/generic-shell-agent.md` | Shell + jq 集成 |

### Universal Skill

CacheHub 提供通用 Agent Skill（`integration/skills/universal/SKILL.md`），可直接复制到任何支持 Skill 协议的 Agent 中。

---

## 配置管理

### 配置文件

```bash
# 初始化配置文件
cachehub config init

# 查看当前配置
cachehub config show

# 设置配置项
cachehub config set defaultModel gpt-4
cachehub config set security.mode Restricted
cachehub config set gateway.port 5218
```

配置文件位于工作区根目录的 `.cachehub-config.json`：

```json
{
  "defaultModel": "gpt-4",
  "security": {
    "mode": "Standard"
  },
  "gateway": {
    "enabled": false,
    "port": 5218,
    "providerUrl": ""
  }
}
```

### 安全模式

| 模式 | 行为 |
|------|------|
| Standard | 默认，允许本地处理，密钥扫描后允许外发 |
| Restricted | 禁止任何外发，纯本地运行 |
| PreviewRequired | 外发前需人工确认 |
| Offline | 完全离线，不触发任何网络请求 |

### 忽略规则

在项目根目录创建 `.cachehubignore` 文件（语法同 `.gitignore`）：

```gitignore
# 自定义忽略规则
docs/
*.test.ts
internal/legacy/
```

规则合并优先级：系统默认 > `.gitignore` > `.cachehubignore` > 用户规则

---

## 上下文包协议

### Context Package Manifest Schema v1

每个上下文包包含一个 Manifest（JSON），核心字段：

```json
{
  "id": "ctx_abc123",
  "schemaVersion": 1,
  "workspaceId": "ws_xyz",
  "indexSnapshotId": "snap_001",
  "task": {
    "originalText": "Fix login bug",
    "keywords": ["login", "bug", "auth"],
    "paths": ["src/auth/"],
    "symbols": ["LoginService"]
  },
  "ranking": {
    "profile": "deterministic-v1",
    "profileVersion": 3,
    "features": ["pathMatch", "symbolMatch", "keywordMatch", ...]
  },
  "budget": {
    "modelContextWindow": 128000,
    "agentReservedTokens": 4096,
    "responseReservedTokens": 4096,
    "contextTarget": 32000,
    "contextHardLimit": 64000,
    "actualEstimate": 28500
  },
  "selectedFiles": [
    {
      "path": "src/auth/login.ts",
      "mode": "full",
      "score": 0.92,
      "reasons": ["pathMatch:0.9", "symbolMatch:1.0"]
    }
  ],
  "safety": {
    "cloudSendAllowed": false,
    "secretsScanPassed": true,
    "ignoreRulesHash": "sha256:...",
    "securityPolicyVersion": "1.0"
  }
}
```

### 选择模式

| 模式 | 说明 |
|------|------|
| `full` | 完整文件内容 |
| `chunks` | 语法块分块（带行号范围） |
| `outline` | 仅文件大纲（符号列表） |
| `deterministicSummary` | 确定性摘要 |
| `metadata` | 仅元数据（路径、大小、语言） |

---

## 高级用法

### Gateway 网关

启动 OpenAI 兼容的 API 网关：

```bash
# 设置环境变量（不要通过命令行参数传递密钥）
export CACHEHUB_PROVIDER_KEY=sk-xxx

cachehub gateway start \
  --provider-url=https://api.openai.com \
  --port=5218

# 查看状态
cachehub gateway status

# 停止
cachehub gateway stop
```

Gateway 功能：
- OpenAI 兼容 API 转发
- 安全请求缓存（Exact Cache）
- 并发请求去重（SingleFlight）
- SSE 流式响应解析
- 用量统计

### Token 估算

```bash
# 估算文件的 Token 数
cachehub token --file=src/auth/login.ts

# 估算文本
cachehub token --text="some text to estimate"
```

### 文件哈希

```bash
cachehub hash --file=src/auth/login.ts
```

### 基准测试

```bash
# 运行基准测试
cachehub benchmark run --workspace=<id> --output=json
```

基准测试包含 6 个跨语言任务，每个任务有 Ground Truth（Required / Helpful / Distractor 文件），评估指标包括 Recall@K、Precision。

### 代码库地图

```bash
# 生成 Repo Map
cachehub repomap --workspace=<id> --output=json
```

### 清理

```bash
# 清理过期数据
cachehub clean --workspace=<id>
```

---

## 最佳实践

### 1. 任务描述要具体

```
❌ "fix bug"
✅ "Fix the token refresh logic in src/auth/refresh.ts that fails when the token expires"
```

### 2. 结合 Git Diff

在代码审查场景中，使用 `--git-diff` 只关注变更文件：

```bash
cachehub context build --workspace=<id> --task="Review changes" --git-diff
```

### 3. 及时提交反馈

```bash
# Agent 完成任务后，告诉 CacheHub 实际读了哪些文件
cachehub context feedback --id=<ctx-id> --file=feedback.json
```

### 4. 使用 Restricted 模式处理敏感项目

```bash
cachehub config set security.mode Restricted
```

### 5. 定期重建索引

代码变更后，重建索引以保持一致性：

```bash
cachehub index build --id=<workspace-id>
```
