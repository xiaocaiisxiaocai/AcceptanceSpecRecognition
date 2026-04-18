# Change: TopK 候选增加 AI 改选

## Why

在移除本地品牌、单位、反义词、数值硬编码规则后，当前链路更依赖 `Embedding + AI`。但现在 AI 只能对本地 Top1 做等价裁决，无法在已召回的 TopK 候选里重新选择更合适的候选，导致部分语义边界样本会被较高 Embedding 的错误候选抢占。

## What Changes

- 在智能填充匹配链路中新增 `TopK AI 重排`
- 让 AI 在已召回候选中选择当前最佳 `SpecId`
- 保留现有“当前最佳候选 AI 等价裁决门禁”
- 结果中新增“选中方式”元数据，区分精确直达、本地 Top1、AI 改选

## Impact

- Affected specs: `matching-engine`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs`
  - `web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue`
  - `web/src/views/smart-fill/components/ScoreDetailCandidateList.vue`
