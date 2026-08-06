# SPEC-003: Context Ranking

- 状态：Frozen (v1)
- 关联任务：P04
- 关联 ADR：ADR-0003

## 定义

Context Ranking 是 CacheHub 的核心智能模块，根据当前任务对候选文件进行评分和排序。

## 排序 Profile

### deterministic-v1 (v3)

版本化权重，权重和 = 1.0：

| 特征 | 权重 | 说明 |
|------|------|------|
| SymbolMatch | 0.22 | 文件中的符号与任务提到的符号匹配 |
| TextMatch | 0.18 | 文件内容与任务关键词的 FTS5 匹配 |
| PathMatch | 0.12 | 文件路径与任务关键词的匹配 |
| GitDiff | 0.12 | 文件在 Git Diff 中 |
| DependencyRelation | 0.10 | 通过 import/依赖关系的距离 |
| CurrentFileRelation | 0.08 | 与当前编辑文件的关系 |
| RecentChange | 0.07 | 最近修改时间 |
| TestRelation | 0.06 | 与测试文件的关系 |
| ConfigRelation | 0.05 | 与配置文件的关系 |

## 归一化

- MinMax 归一化（每查询内）
- 缺失特征填 0
- 防大文件偏置

## 召回管线

五路召回：
1. 路径匹配
2. 符号匹配
3. FTS5 关键词搜索
4. Git Diff（可选）
5. 当前文件

候选去重和聚合：文件级 + 区块级分数合并，记录来源。

## 确定性保证

- 稳定快照 + 版本化 Profile + 确定性 Tokenizer → 相同输入产出相同结果
- 不依赖随机数、不依赖网络、不依赖 LLM
