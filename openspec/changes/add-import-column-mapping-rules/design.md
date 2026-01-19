## Context
导入数据支持“多表选择 + 每表独立列映射”，但缺少“规则复用”。用户希望只维护一套规则，在一个 Word 的多个表格上自动套用映射，减少重复配置。

## Goals / Non-Goals
- Goals:
  - 提供可持久化的“列映射规则”，可复用、可维护
  - 导入时自动预填映射，减少逐表配置
  - 保留逐表手动调整能力（不强制）
- Non-Goals:
  - 不做 AI/LLM 智能理解表格语义（先用可解释规则）
  - 不要求 100% 自动命中（命中率提升即可）

## Decisions
### Decision: 基于“表头文本”做规则匹配
- 规则以“表头文本”为主要信号：如“项目/项目名称/检验项目/Item”等。
- 支持 matchMode：
  - contains（默认）
  - equals
  - regex（高级）
- 支持 priority：多候选时优先级高者胜出；同优先级则按最短/最长文本或首次出现决策（待定）。

### Decision: 规则持久化在后端，自动匹配逻辑可先放前端
- 后端提供规则 CRUD + “当前生效规则”接口。
- 前端导入页拿到 tables.headers 后，在本地执行匹配生成每表 mapping（减少后端耦合，便于快速迭代）。

## Data Model (draft)
- ColumnMappingRuleSet:
  - Id, Name, Scope(Global/Customer/Process), CustomerId?, ProcessId?
  - Rules: List<ColumnMappingRule>
- ColumnMappingRule:
  - TargetField(Project/Specification/Acceptance/Remark)
  - MatchMode(contains/equals/regex)
  - Patterns(List<string>)
  - Priority(int)

## UX (draft)
- 配置管理新增菜单：列映射规则
- 支持编辑四个目标列的可匹配表头列表（可导入/导出 JSON 后续扩展）
- 导入页：在“配置映射”步骤为每个表格预填映射，并提示“已按规则自动匹配”

## Open Questions
- 作用域合并策略（全局 + 客户覆盖 + 制程覆盖）
- 冲突决策：同一个表头可能同时命中多个目标列时如何处理
