# Change: 最终提交流程沉淀列映射

## Why
当前只有智能识别确认流程会把用户确认的表头映射学习到列映射规则。普通导入和普通智能填充也存在明确的用户最终列选择，如果不沉淀，会导致同一客户后续仍需重复配置。

## What Changes
- 普通 Word/Excel 导入最终成功后，将本次使用的表头文本与字段映射写入客户级列映射规则。
- 普通 Word/Excel 智能填充最终成功后，将本次执行使用的表头文本与字段映射写入客户级列映射规则。
- 学习仅写入 `ColumnMappingRules`，不写入表格路由规则。
- 学习失败记录告警，不影响已成功的导入或填充响应。

## Impact
- Affected specs: matching-engine
- Affected code: `DocumentImportAppService`、`MatchingWorkflowSupportService`、列映射学习服务、API 集成测试
