## Context
列映射规则已迁入数据库，并通过智能识别确认流程学习客户级表头别名。普通导入和普通智能填充中的手动列配置同样代表用户确认过的“表头 -> 字段”关系。

## Goals / Non-Goals
- Goals: 在普通导入和普通智能填充最终成功后学习客户级列映射。
- Goals: 复用现有 `SmartConfigurationLearningService` 的 upsert 与全局晋升逻辑。
- Non-Goals: 不学习整表路由，不改表格路由规则，不新增数据库字段。
- Non-Goals: 不在预览、临时切换列、待确认或失败流程中学习。

## Decisions
- Decision: 新增 API 层 `ColumnMappingLearningService`，统一把表头列表和最终列索引转换为学习项。
- Decision: 复用 `SmartConfigurationLearningService.ApplyLearningAsync`，避免重复实现规则 upsert 与晋升逻辑。
- Decision: 导入路径使用已解析的 `TableData.Headers`；填充路径在执行成功后按最终表格配置重新提取表头。
- Decision: 只学习非空、长度合理的表头文本，并按 `Header + TargetField` 去重。

## Risks / Trade-offs
- 多行表头可能学习到组合后的表头文本。这符合当前解析器输出，也和智能识别使用的表头口径一致。
- 学习失败不阻断主流程，可能导致本次成功操作没有沉淀规则；通过 warning 日志保留排查线索。

## Migration Plan
无需数据库迁移。上线后新成功的普通导入和普通智能填充会开始沉淀客户级列映射规则。
