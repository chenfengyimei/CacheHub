# SPEC-002: Workspace Index

- 状态：Frozen (v1)
- 关联任务：P01, P02
- 关联 ADR：ADR-0002

## 定义

Workspace Index 是 CacheHub 对本地代码仓库的版本感知索引，支持增量更新和全文检索。

## 结构

### Workspace（工作区）

- 强类型 ID: `WorkspaceId`
- 名称、根路径（标准化为正斜杠）
- 根路径哈希（SHA-256，用于去重）
- 状态机: Unregistered → Imported → Indexing → Ready → Dirty → Degraded → Blocked → Archived
- 仓库引用、组件列表
- 安全策略引用

### Index Snapshot（索引快照）

- 强类型 ID: `IndexSnapshotId`
- 状态: Building → Active → Superseded → Failed → Cancelled
- 原子激活：新快照构建完成后原子切换为 Active，旧快照标记为 Superseded

### File Entry（文件条目）

- 状态机: Discovered → Indexed / Ignored / Failed / Deleted / Stale
- 标准化路径、大小、语言、内容哈希
- 分层哈希：小文件全量 SHA-256，大文件快速指纹

### 忽略规则

四层合并优先级：
1. 系统默认规则
2. `.gitignore`
3. `.cachehubignore`
4. 用户自定义规则

记录 `ignore_rules_hash`（SHA-256），用于缓存失效。

### FTS5 全文索引

- 绑定 IndexSnapshotId
- 支持路径、内容、语言搜索
- 索引时去重（同一快照内同路径不重复索引）

## 路径安全

- `PathNormalizer`: 标准化分隔符、检测路径穿越（`..`）、验证路径在根目录内
- 符号链接默认不跟随
- UNC 路径处理

## 持久化

- SQLite + WAL 模式
- 连接池禁用（跨平台文件管理）
- 迁移系统：顺序编号、失败回滚、幂等
