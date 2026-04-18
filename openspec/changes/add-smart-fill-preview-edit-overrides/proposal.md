# Change: add smart-fill preview edit overrides

## Why
当前智能填充预览页只能确认“采用哪条匹配结果”，不能直接修订本次导出的 `验收标准` 与 `备注`。客户因此需要先导出文件，再在导出文件中二次修改，影响操作闭环和交付效率。

## What Changes
- 在智能填充预览表的操作列增加单行编辑入口。
- 允许用户在弹窗中只读查看 `项目/规格`，并编辑 `验收标准/备注`。
- 点击 `保存并采用` 后，当前行自动加入本次填充选择。
- 执行填充请求允许携带本次导出的覆盖值，并在写回结果文件时优先使用。
- 覆盖值仅作用于本次导出，不回写验收规格主数据。

## Impact
- Affected specs: `api`, `user-interface`
- Affected code:
  - `web/src/views/smart-fill/*`
  - `web/src/api/matching.ts`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`
  - `tests/AcceptanceSpecSystem.Api.Tests/LlmMatchingAssistFillTests.cs`
  - `web/tests/smart-fill-ai-equivalence.test.ts`
