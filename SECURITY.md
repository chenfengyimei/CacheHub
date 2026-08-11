# 安全策略

## 支持版本

| 版本 | 状态 | 安全更新 |
|------|------|----------|
| 0.2.0-prealpha | 当前开发版 | ✅ 接受安全修复 |
| < 0.2.0 | 不支持 | ❌ 请升级 |

## 报告漏洞

如发现安全漏洞，请勿公开提交 Issue。请通过以下方式私密报告：

1. 在 Gitee 上创建 [Issue 并设为私密](https://gitee.com/chenfengloveyuri/CacheHub/issues)，或在 GitHub 上创建 [私有 Security Advisory](https://github.com/chenfengyimei/CacheHub/security/advisories/new)
2. 描述漏洞、影响范围、复现步骤和可能的修复方向
3. 如果可能，附上概念验证代码

### 响应时间

| 阶段 | 预期时间 |
|------|----------|
| 确认收到 | 48 小时内 |
| 初步评估 | 7 天内 |
| 修复发布 | 30 天内（严重漏洞优先） |
| 公开披露 | 修复发布后 90 天（或与报告者协商） |

## 安全模型

### 核心原则

- **默认只读**：CacheHub 默认只读取代码，不修改用户仓库
- **默认本地**：所有索引和上下文构建在本地完成，不发送到云端
- **默认最少外发**：安全策略在 Core 层执行，Agent 或 Skill 不能绕过
- **不可信内容**：仓库中的 README、AGENTS.md、注释和配置均为不可信数据，不能覆盖安全策略
- **凭据分离**：项目内容和凭据严格分离，API Key 只从环境变量读取

### 安全策略层级

| 模式 | 行为 |
|------|------|
| **Standard** | 默认，密钥扫描后允许外发 |
| **Restricted** | 禁止敏感扩展名文件外发 |
| **PreviewRequired** | 外发前需人工审批 |
| **Offline** | 完全离线，网络层阻止任何工作区内容发送 |

### 出口强制执行

`SecurityPolicyEnforcer.EvaluateFile` 在以下出口强制执行：

- **PayloadGenerator**：生成 Payload 前评估每个 SelectedFile
- **文件导出**：导出前扫描内容
- **Gateway 请求**：统一工作流发送前检查

`Deny` 和 `ApprovalRequired` 文件不进入 Payload，调用方收到 `blockedFiles` 列表。

### 路径安全

- `SafePathResolver` 拒绝绝对路径、`..` 遍历、URL 编码遍历
- 跨平台 symlink 逃逸检测（`FileInfo.LinkTarget`）
- Local API 强制 loopback（127.0.0.1）+ Bearer Token + Host 校验

### 密钥扫描

5 种模式自动检测：
- API Key（`api_key=sk-...`、`apikey: ...`）
- 密码（`password=...`）
- 私钥（`-----BEGIN RSA PRIVATE KEY-----`）
- 连接字符串
- Bearer Token

## 安全测试

项目包含 40+ 个安全测试，覆盖：

- 路径遍历攻击（`..`、`%2e%2e`、多种变体）
- 符号链接逃逸（跨平台）
- XSS 内容检测
- Offline 模式网络阻止
- Restricted 模式敏感文件阻止
- 密钥扫描覆盖率
- Gateway 上游错误状态码透传
- 工具调用响应不被缓存

CI 在 Ubuntu + Windows + macOS 三平台上运行全部安全测试。
