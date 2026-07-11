# Change: 收紧智能结构路由规则定位

## Why
当前智能结构路由规则页面和学习逻辑容易让用户误以为系统主要按 Excel Sheet 名识别表格。真实客户文件中 Sheet 名、Word 表格标题和段落说明都不稳定，按名称自动学习会污染客户级规则，并且无法覆盖 Word 多表场景。

## What Changes
- 将智能结构路由规则定位收敛为“人工兜底/排除/覆盖工具”，不作为主识别入口。
- 用户确认普通验收表后，系统继续保存结构模板和列映射学习，但不默认生成表名路由学习规则。
- Word 表格不按文件名、表格序号、附近标题或段落说明生成表名路由学习规则。
- Excel Sheet 名仅作为弱信号或人工兜底配置，不作为默认自动学习依据。
- 前端配置页调整文案、默认值和中文显示，避免把 `TableName` 暗示为推荐配置方式。

## Impact
- Affected specs: `matching-engine`, `data-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationLearningService.cs`
  - `src/AcceptanceSpecSystem.Application/Services/DocumentTemplateAppService.cs`
  - `web/src/views/config/smart-structure-routing-rules/index.vue`
  - `web/tests/smart-structure-routing-rules.test.ts`
  - 智能结构确认/学习相关 API 测试
- No database migration expected.
