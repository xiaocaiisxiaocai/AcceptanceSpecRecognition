# Change: 智能结构识别接入历史模板 Few-shot

## Why

当前智能结构识别已经具备“客户模板命中 -> 规则识别 -> 健康检查 -> LLM 结构裁决”的链路，但 LLM 裁决只看到当前表格摘要和规则候选，无法参考同一客户已经确认过的相似结构。

在规则低置信或字段缺失时，给 LLM 注入少量同客户历史模板案例，可以提高灰区识别稳定性，同时复用现有 `DocumentTemplate`，避免新增复杂学习系统。

## What Changes

- 在 LLM 结构裁决请求中增加“相似历史结构案例”输入。
- 从现有客户级 `DocumentTemplate` 中选择少量高频/相似模板作为 Few-shot 案例。
- 扩展智能结构识别 Prompt，占位符中包含历史案例 JSON。
- 保持现有高置信模板命中和规则识别路径不变。
- 保持 LLM 调用预算、超时和失败降级策略不变。

## Non-Goals

- 不新增独立 `successful_structure_cases` 表。
- 不引入 Embedding 列映射。
- 不实现 OCR、多模态识别或流式识别。
- 不新增前端交互。
- 不改变 `/api/smart-config/recognize` 响应契约。

## Impact

- Affected specs: `matching-engine`
- Affected code:
  - `src/AcceptanceSpecSystem.Application/Services/DocumentTemplateAppService.cs`
  - `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
  - `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Structure/DocumentStructureFusion.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.Prompts.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.RerankStructurePrompts.cs`
  - related Core/API tests
