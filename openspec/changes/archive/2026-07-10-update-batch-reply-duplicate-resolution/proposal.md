# Change: 批量回复重复键冲突弹窗决议

## Why
当前批量回复在来源表或目标表出现重复的“项目 + 规格”组合时，只会返回一条笼统错误并阻断预览。用户已经明确要求改为弹窗确认处理，而不是只能自己回去猜测和手动排查。

## What Changes
- 为批量回复单表预览补充结构化重复键冲突响应，区分来源冲突与目标冲突，并返回冲突分组详情。
- 为批量回复单表预览请求补充重复键处理决议，支持“保留首条”“保留末条”“跳过该组”。
- 在批量回复页面的当前 Sheet/表格上下文内弹出冲突处理对话框，用户确认后重新生成预览。
- 保持现有“步骤 Tab -> 文件 Tab -> Sheet/表格 Tab”结构，不恢复独立预检查区域。

## Impact
- Affected specs: `api`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
  - `web/src/api/matching.ts`
  - `web/src/views/batch-reply/index.vue`
  - `web/src/views/smart-fill/components/BatchTableConfig.vue`
  - `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`
  - `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`
