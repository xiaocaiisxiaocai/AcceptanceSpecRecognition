# Change: 新增智能结构识别极简流程

## Why
当前数据导入和智能填充依赖用户逐步选择表格、配置表头和列映射，真实文档较多时操作成本高。系统需要在上传并选择业务归属后自动识别文档结构，高置信直达，低置信只确认少量字段。

## What Changes
- 新增 `POST /api/smart-config/recognize` 和 `POST /api/smart-config/confirm`，输出全文档扁平表格结构识别结果并支持确认后学习。
- 新增客户级文档模板与列映射规则客户域学习，支持重复结构 L0 命中和表头词自增长。
- 新增三层识别流水线：客户模板、规则字典、LLM 结构裁决；AutoApply 前必须经过确定性体检。
- 数据导入和智能填充改为上传/归属后的两步式体验，并保留现有手动配置作为高级兜底。
- 数据导入直达必须满足现有 Word/Excel 导入接口必填列；智能填充仅规格模式须遵守现有请求级 `MatchingMode` 约束。

## Impact
- Affected specs: `api`, `user-interface`, `data-storage`, `matching-engine`, `architecture`
- Affected code: Application 智能结构识别用例服务、SmartConfigController、Core 文档智能识别模块、DocumentTemplate 与 ColumnMappingRule 数据模型、Prompt 模板场景、数据导入与智能填充前端流程
