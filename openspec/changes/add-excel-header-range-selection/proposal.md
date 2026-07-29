# Change: Excel 结构确认同时支持表头与坐标选择

## Why

仅显示和编辑 A1 坐标不利于业务用户理解字段含义。确认卡需要在保留精确坐标调整能力的同时，允许用户按实际表头直接选择项目、规格、验收和备注列。

## What Changes

- 在 Excel 结构确认卡中，将字段角色、表头下拉和现有纵向坐标范围组合展示。
- 表头选择与坐标范围映射双向同步；坐标仍通过现有“调整范围”入口编辑。
- 多行表头使用最下面一行，合并表头只显示一个可选标题。
- 数据导入和智能填充继续复用同一套确认卡。

## Impact

- Affected specs: `user-interface`
- Affected code: `web/src/views/shared/SmartStructureConfirmCard.vue`、共享结构识别辅助函数及定向测试
