# CacheHub 后续完整开发执行包 V2.0

基线：`chenfengyimei/CacheHub@ca0896ec5e39acaf670af614fc9e3328d5fbe524`

## 文件说明

- `01_CacheHub_后续完整开发总纲_V2.0.docx`：适合阅读和评审的 Word 版。
- `01_CacheHub_后续完整开发总纲_V2.0.md`：适合开发 AI 直接读取。
- `execution-kit/ROADMAP.yaml`：阶段依赖、目标和 Gate。
- `execution-kit/WORK_ITEMS.yaml`：88 个任务的机器可读卡片。
- `execution-kit/ACCEPTANCE_GATES.yaml`：全局发布门和核心指标。
- `execution-kit/schemas/AI_DEV_STATE.schema.json`：状态文件 Schema。
- `execution-kit/prompts/主执行提示词.md`：交给开发 AI 的总提示词。
- `execution-kit/prompts/单阶段执行提示词.md`：执行某个阶段时使用。
- `execution-kit/templates/`：任务卡、ADR、测试证据、Bug 和发布模板。

## 推荐使用顺序

1. 把整个目录放到仓库 `Docs/development-plan/` 或提供给开发 AI。
2. 先发送 `主执行提示词.md`。
3. 要求 AI 从 R4-W001 开始，自动提交并持续开发。
4. 每个阶段结束后检查 `Docs/ai/evidence/<PHASE>-GATE.md`。
5. Gate 失败时修复，不要直接跳到下一个阶段。
