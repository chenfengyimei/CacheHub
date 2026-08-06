# SPEC-004: Integration Protocol

- 状态：Frozen (v1)
- 关联任务：P05
- 关联 ADR：ADR-0004

## 定义

Integration Protocol 定义了 AI Agent 接入 CacheHub 的标准方式，确保任何工具无需专属插件即可使用。

## 接入等级

| 等级 | 方式 | 说明 |
|------|------|------|
| Level 0 | Gateway Base URL | Agent 配置 CacheHub Gateway 为 API Base URL |
| Level 1 | 文件导出 | 导出 `.cachehub/` 目录，Agent 读取文件 |
| Level 2 | CLI 调用 | Agent 直接调用 `cachehub` CLI 命令 |
| Level 3 | Local API | Agent 通过 HTTP API 调用 |
| Level 4 | 原生工作流 | Agent 内置 CacheHub 集成 |

## CLI JSON 契约

- 所有命令支持 `--output=json` / `--json`
- stdout 只输出协议结果（JSON）
- 日志和进度信息走 stderr
- 退出码：0=成功，1=失败

## Local API

- 仅监听 loopback (127.0.0.1)
- REST API，JSON 请求/响应
- 路由前缀 `/api/v1/`

## 文件导出协议

`.cachehub/` 目录结构：
- `workspace.json`: 工作区信息
- `latest-context.manifest.json`: 上下文包 Manifest
- `latest-context.md`: 上下文包 Markdown（可直接喂给 AI）
- `repomap.md`: 代码库地图

## Capability Discovery

`cachehub capabilities --output=json` 返回：
- 版本、协议版本
- 已启用的能力列表
- Schema 版本
- 已知限制

## Universal Skill

Agent 无关的通用技能文件，包含：
- 工作流程指引
- 安全规则
- 命令调用顺序

## integration verify

5 步安装验证：
1. 数据目录可访问
2. 数据库和迁移已应用
3. 工作区仓储可用
4. CLI 功能正常
5. 回滚能力（安全移除）
