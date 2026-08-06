# SPEC-001: Context Package

- 状态：Frozen (v1)
- 关联任务：P00-W006, P04
- 关联 ADR：ADR-0003

## 定义

Context Package 是 CacheHub 的核心公共协议，将代码上下文以可解释、可复现、Token 受限的方式传递给 AI Agent。

## 结构

### Manifest（元数据）

与 Payload 分离，允许客户端先检查再决定是否拉取内容。

必填字段：
- `id`: `^ctx_[a-zA-Z0-9]+$`
- `schemaVersion`: 1
- `workspaceId`: 工作区 ID
- `indexSnapshotId`: 索引快照 ID
- `task`: 任务信息（原文、关键词、路径、符号、操作类型、查询解析器版本）
- `ranking`: 排序信息（profile ID、版本、特征权重）
- `budget`: Token 预算（模型窗口、Agent 预留、响应预留、目标、硬限制、安全边界、实际估算、Tokenizer）
- `selectedFiles`: 选中文件列表（路径、模式、分数、原因、行范围）
- `excludedCandidates`: 排除候选列表（路径、分数、原因）
- `safety`: 安全信息（cloudSendAllowed、secretsScanPassed、ignoreRulesHash、securityPolicyVersion、secretScannerVersion、approvalId、sensitiveExclusions）
- `createdAt`: 创建时间

复现字段：
- `repositoryCommit`: Git HEAD commit hash
- `dirtyStateHash`: 脏状态哈希
- `queryParserVersion`: 查询解析器版本
- `rankingProfileVersion`: 排序 profile 版本
- `chunkingStrategyVersion`: 分块策略版本
- `tokenBudgetPolicyVersion`: Token 预算策略版本
- `parserVersions`: 解析器版本映射
- `contextEngineVersion`: 引擎版本
- `repoMapVersion`: Repo Map 版本

### Payload（内容）

- `contextPackageId`: 关联的 Manifest ID
- `format`: Markdown / JSON / Plain
- `totalEstimatedTokens`: 总估算 Token
- `items`: 内容项列表（路径、模式、内容、起止行）

### Selection Modes

| 模式 | 说明 |
|------|------|
| Full | 完整文件内容 |
| Chunks | 语法块分块（带行号范围） |
| Outline | 仅文件大纲（符号列表） |
| DeterministicSummary | 确定性摘要 |
| Metadata | 仅元数据（路径、大小、语言） |

## JSON Schema

详见 `Docs/specs/context-package.manifest.v1.json`。

## 兼容性

- Schema 版本化，消费者忽略未知可选字段
- 安全字段不忽略
- 旧 minor 版本 Manifest 必须可读
