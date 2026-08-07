# CacheHub Context API 协议

## 认证

所有 Local API（`/api/` 路由）需要 Bearer Token 认证：

```
Authorization: Bearer <token>
```

Token 在 Desktop 启动时自动生成并打印在终端中。可通过环境变量 `CACHEHUB_API_TOKEN` 或配置 `ApiToken` 覆盖。

## CLI

所有命令支持 `--output=json`。JSON 结果走 stdout，日志走 stderr。

```
cachehub capabilities --output=json
cachehub workspace import <path> [--name=<name>] [--output=json]
cachehub workspace list [--output=json]
cachehub workspace status --id=<id> [--output=json]
cachehub workspace remove --id=<id>
cachehub index build --id=<workspace-id>
cachehub index status --id=<workspace-id>
cachehub index verify --id=<workspace-id>
cachehub index refresh --id=<workspace-id>
cachehub context build --workspace=<id> --task=<text> [--output=json] [--model=<id>] [--git-diff]
cachehub context inspect --id=<context-id>
cachehub context list --workspace=<id>
cachehub context export --id=<context-id> --format=<markdown|json|file>
cachehub context expand --id=<context-id> [--file=<path>] [--symbol=<name>] [--reason=<text>]
cachehub context feedback --id=<context-id> --file=<path>
cachehub search --workspace=<id> --query=<text>
cachehub detect <path> [--plan] [--output=json]
cachehub repo inspect <url>
cachehub repo clone <url> <dir> [--depth=1]
cachehub repo status <path>
cachehub repo diff <path>
cachehub repo pull <path>
cachehub gateway start --provider-url=<url> --port=<port>
cachehub gateway status
cachehub gateway stop
cachehub stats [--output=json]
cachehub config show
cachehub config init
cachehub config set <key> <value>
cachehub version
cachehub integration verify
```

## Local API

Base URL: `http://localhost:5099`
认证: 所有 `/api/` 路由需要 `Authorization: Bearer <token>` 头。

| 路由 | 方法 | 功能 |
|------|------|------|
| `/api/v1/capabilities` | GET | 能力发现（版本、协议、已启用功能、限制） |
| `/api/v1/workspaces` | GET | 工作区列表 |
| `/api/v1/workspaces` | POST | 导入工作区（`{name, rootPath}`） |
| `/api/v1/workspaces/{id}` | GET | 工作区状态 |
| `/api/v1/workspaces/{id}` | DELETE | 删除工作区 |
| `/api/v1/workspaces/{id}/index` | POST | 构建索引（后台批量事务，返回 Building 状态） |
| `/api/v1/workspaces/{id}/contexts` | GET | 上下文包列表 |
| `/api/v1/workspaces/{id}/export` | POST | 文件导出 |
| `/api/v1/context/build` | POST | 构建上下文包（`{workspaceId, task}`） |
| `/api/v1/context/{id}` | GET | 检查上下文包 Manifest |
| `/api/v1/context/{id}/expand` | POST | 扩展上下文（生成子修订包，`{file?, symbol?, reason?}`） |
| `/api/v1/context/{id}/feedback` | POST | 提交反馈（`{clientId, taskCompleted, filesActuallyRead}`） |
| `/api/v1/context/{id}/explain` | GET | 解释选择/遗漏/预算 |
| `/api/v1/context/{id}/payload` | GET | 获取完整 Payload（含 blockedFiles 安全信息） |
| `/api/v1/search` | GET | FTS5 全文搜索（`?query=<text>&workspace=<id>`） |
| `/api/v1/outline` | GET | 代码大纲（`?workspace=<id>&path=<relative>`） |
| `/api/v1/stats` | GET | 使用统计 |

### 请求示例

```bash
# 构建上下文
curl -X POST http://localhost:5099/api/v1/context/build \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"workspaceId":"<id>","task":"Fix login bug in AuthService"}'

# 获取 Payload（含安全策略拦截信息）
curl -H "Authorization: Bearer <token>" \
  http://localhost:5099/api/v1/context/<ctx-id>/payload

# 扩展上下文（生成子修订包）
curl -X POST http://localhost:5099/api/v1/context/<ctx-id>/expand \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <token>" \
  -d '{"file":"src/utils.ts","reason":"Need utility functions"}'

# FTS5 搜索
curl -H "Authorization: Bearer <token>" \
  "http://localhost:5099/api/v1/search?query=authentication&workspace=<id>"
```

## Gateway API

Gateway 是可选的 OpenAI 兼容 API 网关，默认端口 5218。
启动时自动生成随机 Bearer Token（与 Local API Token 不同）。

| 路由 | 方法 | 功能 |
|------|------|------|
| `/v1/chat/completions` | POST | OpenAI 兼容 chat completions（支持 SSE 流式） |
| `/v1/responses` | POST | OpenAI 兼容 responses 端点 |
| `/v1/models` | GET | 模型列表 |

```bash
# 启动 Gateway（密钥通过环境变量传递，不进命令行历史）
export CACHEHUB_PROVIDER_KEY=sk-xxx
cachehub gateway start --provider-url=https://api.openai.com --port=5218

# 使用 Gateway
curl -X POST http://localhost:5218/v1/chat/completions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <gateway-token>" \
  -d '{"model":"gpt-4","messages":[{"role":"user","content":"hello"}]}'
```

## 文件导出

```
.cachehub/
├─ workspace.json
├─ latest-context.manifest.json
├─ latest-context.md
└─ repomap.md
```

## 错误模型

所有 CLI JSON 输出和 HTTP API 使用统一的 ErrorEnvelope：

```json
{
  "success": false,
  "errorCode": 2001,
  "message": "Workspace not found",
  "recoverable": false,
  "suggestedActions": ["Use 'cachehub workspace list' to see registered workspaces"]
}
```

错误码定义：

| 范围 | 类别 |
|------|------|
| 1001-1003 | 通用（参数错误、不支持、取消） |
| 2001-2005 | 工作区 |
| 3001-3003 | 索引 |
| 4001-4004 | 上下文 |
| 5001-5004 | 安全 |
| 6001-6003 | 仓库 |
| 7001-7004 | Gateway（含 AuthRequired、RequestTooLarge） |
