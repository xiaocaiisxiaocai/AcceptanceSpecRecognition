## ADDED Requirements
### Requirement: 历史旧表删除迁移
系统 SHALL 通过新增迁移删除已废弃的历史数据表，而不是篡改已有历史迁移文件。

#### Scenario: 删除旧列映射规则表
- **WHEN** 系统执行最新数据库迁移
- **THEN** 旧 `ColumnMappingRules` 表及其索引被删除
- **AND** 现行模型快照不再包含该表

## REMOVED Requirements
### Requirement: 列映射规则持久化
**Reason**: 旧列映射规则能力已删除，不再需要对应持久化结构。
**Migration**: 通过新增迁移删除旧表；历史创建迁移文件保留，仅用于历史追溯。

#### Scenario: 新模型不再包含列映射规则表
- **WHEN** 开发者查看当前 `AppDbContext` 与模型快照
- **THEN** 当前模型中不再出现 `ColumnMappingRules`
