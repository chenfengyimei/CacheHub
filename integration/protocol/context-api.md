# AI_KV Context API 协议

## CLI

所有命令支持 `--output=json`。JSON 结果走 stdout，日志走 stderr。

```
aikv capabilities --output=json
aikv workspace import <path> [--name=<name>] [--output=json]
aikv workspace list [--output=json]
aikv workspace status --id=<id> [--output=json]
aikv workspace remove --id=<id>
aikv index build --id=<workspace-id>
aikv index status --id=<workspace-id>
aikv index verify --id=<workspace-id>
aikv context build --workspace=<id> --task=<text> [--output=json]
aikv context inspect --id=<context-id>
aikv context export --id=<context-id> --format=<markdown|json>
aikv context expand --id=<context-id> --symbol=<name>
aikv context feedback --id=<context-id> --file=<path>
```

## Local API (规划中)

```
GET  /api/v1/capabilities
GET  /api/v1/workspaces
POST /api/v1/workspaces/import
GET  /api/v1/workspaces/{id}/status
POST /api/v1/context/build
GET  /api/v1/context/{id}
GET  /api/v1/context/{id}/payload
POST /api/v1/context/{id}/expand
POST /api/v1/context/{id}/feedback
```

## 文件导出

```
.aikv/
├─ workspace.json
├─ latest-context.manifest.json
├─ latest-context.md
└─ repomap.md
```

## 错误模型

```json
{
  "success": false,
  "errorCode": "WORKSPACE_NOT_FOUND",
  "message": "工作区未找到",
  "recoverable": true,
  "suggestedActions": ["使用 aikv workspace list 查看已注册工作区"]
}
```
