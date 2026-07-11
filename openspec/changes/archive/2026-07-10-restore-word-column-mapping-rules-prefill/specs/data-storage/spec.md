## MODIFIED Requirements
### Requirement: 列映射规则持久化
系统 SHALL 在数据库中持久化全局列映射规则，用于 Word 导入和智能填充的列自动预填。

#### Scenario: 数据模型包含列映射规则表
- **WHEN** 系统加载当前数据库模型
- **THEN** 模型包含 `ColumnMappingRules` 表
- **AND** 每条规则包含目标字段、匹配模式、匹配词、优先级与启用状态

#### Scenario: 迁移后数据库存在列映射规则表
- **WHEN** 系统应用最新迁移
- **THEN** 数据库中存在 `ColumnMappingRules` 表及相关索引
