# 贡献指南

感谢你对 AI_KV 项目的兴趣！

## 开发流程

1. Fork 仓库并创建特性分支
2. 遵循 [AI 开发执行手册](Docs/AI开发执行手册_开发包/AI_KV_AI开发执行手册_V1.0_开发包/AI_KV_AI开发执行手册_V1.0.md) 的工程规范
3. 确保所有测试通过：`dotnet test AI_KV.sln`
4. 确保格式检查通过：`dotnet format AI_KV.sln --verify-no-changes`
5. 提交 Pull Request

## 代码规范

- 启用 nullable 引用类型
- 公共 API 需 XML 文档注释
- 异步方法传递 CancellationToken
- 不使用 sync-over-async
- 不吞掉异常
- 测试不依赖真实网络或云 API

## 提交规范

- 提交信息使用中文
- 用【】分章节大纲
- 结尾标注文件变更统计
