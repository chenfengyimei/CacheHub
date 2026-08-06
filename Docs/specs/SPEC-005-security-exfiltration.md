# SPEC-005: Security & Exfiltration

- 状态：Frozen (v1)
- 关联任务：P00, P04, P05
- 关联 ADR：ADR-0001

## 定义

Security & Exfiltration 规范定义了 CacheHub 的安全边界，确保代码内容不被意外外发。

## 信任边界

| 来源 | 信任级别 | 说明 |
|------|----------|------|
| 用户直接指令 | 最高 | 用户明确要求的操作 |
| 本地 Agent | 中 | 运行在本机的 AI Agent |
| 陌生仓库 | 最低 | 被导入的项目代码 |
| 插件/LSP | 最低 | 第三方扩展 |
| 云 Provider | 不信任 | 远程模型 API |
| 本机进程 | 中 | Gateway、LSP 等子进程 |

**仓库内容不可信**：README、AGENTS.md、注释、配置文件均为不可信数据，不得覆盖安全策略。

## 外发模式

| 模式 | 行为 |
|------|------|
| Standard | 默认，密钥扫描后允许外发 |
| Restricted | 禁止任何外发，纯本地运行 |
| PreviewRequired | 外发前需人工确认 |
| Offline | 完全离线，不触发任何网络请求 |

## 密钥扫描

检测 5 种密钥类型：
1. API Key（`sk-`、`AKIA` 等前缀）
2. 密码（`password=`、`passwd=` 等）
3. 私钥（`-----BEGIN ... PRIVATE KEY-----`）
4. 连接字符串（含密码的连接串）
5. Bearer Token（`Bearer eyJ...`）

## 敏感文件检测

默认阻止的文件模式：
- `.env`、`.env.local`、`.env.*`
- `*.pem`、`*.key`、`*.p12`、`*.pfx`
- `id_rsa`、`id_ecdsa`、`id_ed25519`
- `credentials.json`、`service-account*.json`
- `*.keystore`、`*.jks`

## 发送前检查

`CheckBeforeSend(path, content, mode)`：
1. 路径检查：是否在敏感文件列表中
2. 内容检查：是否包含密钥
3. 模式检查：当前外发模式是否允许

## Gateway 安全

- 仅监听 loopback (127.0.0.1)
- 拒绝工具调用请求的缓存
- 拒绝高温度请求的缓存
- 拒绝流式请求的缓存
- 不猜测工作区（需显式 metadata）
