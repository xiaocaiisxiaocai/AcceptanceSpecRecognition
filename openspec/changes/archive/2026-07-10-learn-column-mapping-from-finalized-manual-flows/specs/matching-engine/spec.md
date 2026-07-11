## ADDED Requirements
### Requirement: 最终提交流程沉淀列映射学习
系统 SHALL 在普通导入和普通智能填充最终成功后，将用户最终使用的表头列映射学习为客户级列映射规则。

#### Scenario: 普通导入成功后学习列映射
- **GIVEN** 用户在普通 Word 或 Excel 导入中选择了项目、规格、验收和备注列
- **WHEN** 导入最终成功写入或覆盖验收规格数据
- **THEN** 系统将对应表头文本学习为当前客户的 `ColumnMappingRules`
- **AND** 学习规则使用 `Source = Learned`、`MatchMode = Equals`、`Priority >= 100`

#### Scenario: 普通智能填充成功后学习列映射
- **GIVEN** 用户在普通 Word 或 Excel 智能填充中选择了项目、规格、验收和备注列
- **WHEN** 填充最终成功持久化结果
- **THEN** 系统将对应表头文本学习为当前客户的 `ColumnMappingRules`
- **AND** 系统不写入表格路由规则

#### Scenario: 非最终成功流程不学习
- **GIVEN** 普通导入或普通智能填充仍处于预览、待确认或失败状态
- **WHEN** 流程尚未最终成功
- **THEN** 系统不得写入新的列映射学习规则

#### Scenario: 学习失败不阻断主流程
- **GIVEN** 普通导入或普通智能填充已经最终成功
- **AND** 列映射学习写入失败
- **WHEN** 系统返回本次业务操作结果
- **THEN** 导入或填充仍按成功返回
- **AND** 系统记录可排查的告警日志
