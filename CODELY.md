

## Codely Structured Memories

### User

### Feedback
- [2026-08-06 21:59:32] CacheHub 项目（原名 AI_KV，2026-08-06 重命名）开发规范：每完成一个可独立验证的模块、功能节点或阶段性任务后，自动执行 git commit + push（双远程 origin/gitee + github），无需等待用户确认。提交格式：`类型(模块): 本阶段完成内容`。频繁多提交。禁止 force push / reset --hard / 改写历史。推送失败保留本地提交并记录原因。未完成/临时代码不提交。
- [2026-08-06 22:15:36] [feedback] 用户要求系统性排查时：必须逐模块验证编译、测试、格式检查、命名空间/类名/方法名/路径一致性，不能只做全局文本替换就提交。**Why:** 大规模重命名后容易遗漏方法名（如 GetDefaultAikvignore→GetDefaultCachehubignore）、版本号不一致（0.1.0-alpha vs 0.2.0）、Capabilities 功能声明遗漏等问题。**How to apply:** 重命名后用 explore subagent 逐项验证 10+ 检查点，发现版本号 drift 等非重命名问题也一并修复。

### Project
- [2026-08-06 23:28:02] [project] CacheHub 2026-08-06 系统性排查发现的关键 bug 模式：①CLI 命令分发层容易出 args 偏移错误（Program.cs 剥离命令名后传 args[1..]，子命令处理器不应再匹配命令名）；②GetOpt 解析 `--key=value` 必须用 `StartsWith(prefix+"=")` 匹配（避免误匹配 --id--foo），且切片必须 `[(prefix.Length + 1)..]` 跳过 `=`——只加 prefix.Length 会留下前导 `=` 导致 "Workspace not found: =id"。曾波及 6 个文件（Index/Context/Explain/Gateway/Search/TokenCommands）。**How to apply:** 新增 CLI 命令参数解析时，匹配 `prefix+"="`、切片 `[(prefix.Length+1)..]`；实测 `cachehub index build --id=<ws>` 验证。

- [2026-08-06 22:38:04] [project] CacheHub CI 配置必须保持 workflow_dispatch 仅手动触发。**Why:** 2026-08-06 曾改为 push/PR 自动触发，但 CI 在 GitHub Actions ubuntu-latest 上报部署测试错误。**How to apply:** 不要在 ci.yml 中添加 push/pull_request 触发器，保持 workflow_dispatch only。

### Reference

