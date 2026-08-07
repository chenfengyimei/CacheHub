# 贡献指南

感谢你对 CacheHub 项目的兴趣！本文档介绍如何参与开发�?
## 快速开�?
```bash
# 1. Fork 并克�?git clone https://github.com/chenfengyimei/CacheHub.git
cd CacheHub

# 2. 构建 + 测试
dotnet build CacheHub.sln -c Release
dotnet test CacheHub.sln -c Release

# 3. 格式检查（CI 会验证）
dotnet format CacheHub.sln --verify-no-changes

# 4. 创建特性分�?git checkout -b feat/your-feature
```

## 项目结构

```
src/
  CacheHub.Core/         领域模型、错误、标识符、安全、Tokenizer（无运行时依赖）
  CacheHub.Storage/      SQLite�?0 个迁移�? 个仓储、FTS5、CacheStore
  CacheHub.Indexing/     扫描、忽略规则�?2 语言正则解析器、RepoMap、Reconciler
  CacheHub.Context/      9 源召回�? 维排序、锚点分块、预算验证、引擎、缓�?  CacheHub.Gateway/      Gateway Server、多 Provider Fallback、SSE 流式、Responses API 流式、持久化缓存（独立项目）
  CacheHub.Cli/          CLI 入口�?3 个命令组，含 workflow/doctor/benchmark�?  CacheHub.Desktop/      Web UI + Local API�?8 路由 + Bearer 认证 + contextual-completion�?tests/
  CacheHub.Tests/        847 测试
```

**依赖�?*：Core �?Storage, Indexing, Gateway；Context �?Core + Storage + Indexing；CLI/Desktop �?全部。不要引入循环依赖�?
## 开发流�?
1. Fork 仓库并创建特性分支（`feat/`、`fix/`、`docs/`、`refactor/`、`test/`�?2. 编写代码，遵循下方代码规�?3. 新增功能必须附带测试
4. 确保所有检查通过�?   ```bash
   dotnet build CacheHub.sln -c Release          # 0 错误 0 警告
   dotnet test CacheHub.sln -c Release             # 全部通过
   dotnet format CacheHub.sln --verify-no-changes  # 格式检�?   ```
5. 提交 Pull Request，描述变更内容和动机

## 代码规范

- **Nullable**：启�?nullable 引用类型，不使用 `!` 压制警告
- **异步**：异步方法传�?`CancellationToken`，不使用 sync-over-async（`.Result`/`.GetAwaiter().GetResult()`�?- **异常**：不吞掉异常，catch 块必须记录或重新抛出
- **XML 文档**：公�?API 需 XML 文档注释（`GenerateDocumentationFile=true`�?- **安全**：测试不依赖真实网络或云 API，不硬编码密�?- **命名**：私有字段用 `_` 前缀（`.editorconfig` 强制�?- **行尾�?*：所有文件使�?LF（`.gitattributes` 强制�?
## 测试规范

- 使用 xUnit
- 测试类放�?`tests/CacheHub.Tests/`
- 集成测试标注 `[Collection("SQLite")]`（串行执行，避免数据库锁�?- 不依赖真�?Git 环境�? 个跳过测试已记录�?- 安全测试必须包含攻击夹具
- Benchmark 测试不允许硬编码成功指标

## 提交规范

- 提交信息使用中文
- 格式：`类型(模块): 本阶段完成内容`
- 类型：`feat` / `fix` / `chore` / `docs` / `refactor` / `test` / `ci`
- 用【】分章节大纲描述变更
- 结尾标注文件变更统计

示例�?```
feat(security): 新增符号链接逃逸检�?
【新增】SafePathResolver 检�?symlink 目标是否在工作区�?
- 使用 FileInfo.LinkTarget 跨平台检测符号链�?- 新增 2 个测试验�?symlink 逃逸被阻止

1 个文件变更（+15 / -3�?```

## Pull Request 审查标准

- [ ] 构建通过�? 错误 0 警告�?- [ ] 所有测试通过
- [ ] 格式检查通过
- [ ] 新功能有测试覆盖
- [ ] 安全相关变更有攻击夹具测�?- [ ] 提交信息符合规范
- [ ] 不引入循环依�?- [ ] 文档已更新（如涉�?API 变更�?
## CI

GitHub Actions �?**Ubuntu + Windows + macOS** 三平台上运行 `build + test + format`，每�?push �?PR 自动触发。PR 必须在三平台全部通过才能合并�?
## 许可�?
贡献的代码遵�?[MIT License](LICENSE)�?