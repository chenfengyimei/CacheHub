# CacheHub Context API 协议

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
cachehub context build --workspace=<id> --task=<text> [--output=json]
cachehub context inspect --id=<context-id>
cachehub context export --id=<context-id> --format=<markdown|json>
cachehub context expand --id=<context-id> --symbol=<name>
cachehub context feedback --id=<context-id> --file=<path>
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
.cachehub/
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
  "suggestedActions": ["使用 cachehub workspace list 查看已注册工作区"]
}
```
