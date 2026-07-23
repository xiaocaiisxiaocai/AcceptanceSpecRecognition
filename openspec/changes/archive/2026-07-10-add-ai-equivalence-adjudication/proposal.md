# Change: 智能填充当前最佳候选 AI 等价裁决门禁

## Why
客户反馈智能填充对换行、普通标点、`≈/约等于/约为` 等表达差异不够智能，且不希望继续维护规则词表。当前系统容易把格式或等价表达差异表现为中风险，影响自动填充体验。

## What Changes
- 在智能填充匹配结果中增加“当前最佳候选 AI 等价裁决门禁”能力。
- 不要求客户维护等价符号、等价表达或标点规则，由 Prompt + AI 判断文本关系。
- 对达到中置信门槛且无硬冲突的当前最佳候选固定执行 AI 等价裁决，避免继续信任旧的分数直通逻辑。
- AI 返回结构化结论：等价、不同、不确定，以及格式差异、标点差异、等价表达、语义差异等原因类型。
- 等价结论不降低置信度，但前端展示“AI 判断为等价表达/格式差异”等提示。
- AI 判断不同或不确定时，默认进入人工确认。
- 前端智能填充预览与详情同步展示 AI 裁决结论，避免只改后端。

## Impact
- Affected specs: `matching-engine`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/Models/*`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
  - `web/src/api/matching.ts`
  - `web/src/views/smart-fill/components/*`
  - `web/src/views/smart-fill/composables/useScoreDetailDiff.ts`
