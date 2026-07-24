## ADDED Requirements

### Requirement: 验收规格分组分页使用组合排序索引
系统 SHALL 为验收规格建立覆盖客户、制程、机型、导入时间和主键的组合索引，以支持分组内稳定倒序分页。

#### Scenario: 分组内按导入时间倒序分页
- **WHEN** 系统按 `CustomerId`、`ProcessId` 与 `MachineModelId` 等值筛选并按 `ImportedAt`、`Id` 倒序查询验收规格
- **THEN** 数据库能够使用同一组合索引完成分组筛选与稳定排序
- **AND** 全局列表所需的独立导入时间索引继续保留

#### Scenario: 迁移回滚
- **WHEN** 管理员回滚本次数据库迁移
- **THEN** 系统删除五列组合排序索引并恢复原三列组合筛选索引
