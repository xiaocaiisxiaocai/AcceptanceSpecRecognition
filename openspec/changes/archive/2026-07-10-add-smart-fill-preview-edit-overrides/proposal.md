# Change: add smart-fill preview edit overrides

## Why
当前智能填充预览页只能确认“采用哪条匹配结果”，不能直接修订本次导出的 `验收标准` 与 `备注`。客户因此需要先导出文件，再在导出文件中二次修改，影响操作闭环和交付效率。

## What Changes
- 在智能填充预览表的操作列增加单行编辑入口。
- 允许用户在弹窗中只读查看 `项目/规格`，并编辑 `验收标准/备注`。
- 在匹配配置中增加 `仅精确匹配` 开关；开启后仍要求 Embedding 服务可用，但只采用 `项目+规格` 完全一致的结果，不进入语义 TopK 与 AI 裁决。
- 点击 `保存并采用` 后，当前行自动加入本次填充选择。
- 未精确命中的行仍进入预览，并允许用户手工填写 `验收标准/备注` 后用于本次导出。
- 执行填充请求允许携带本次导出的覆盖值，并在写回结果文件时优先使用。
- 覆盖值与未命中行的手工填写值仅作用于本次导出，不回写验收规格主数据。

## Impact
- Affected specs: `api`, `user-interface`
- Affected code:
  - `web/src/views/smart-fill/*`
  - `web/src/api/matching.ts`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`
  - `tests/AcceptanceSpecSystem.Api.Tests/LlmMatchingAssistFillTests.cs`
  - `web/tests/smart-fill-ai-equivalence.test.ts`
