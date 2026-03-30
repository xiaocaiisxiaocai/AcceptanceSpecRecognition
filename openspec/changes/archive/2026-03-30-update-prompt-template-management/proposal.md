# Change: 优化 Prompt 模板管理的场景化与校验机制

## Why
当前 Prompt 模板运行时按固定名称读取，但管理端仍暴露“默认模板”和任意命名的管理语义，导致页面操作与真实运行时行为不一致。与此同时，智能填充复核与导入重复识别复核共用同一 review 模板，场景边界不清晰，模板内容一旦改坏也缺少保存前校验，容易把问题拖到运行时才暴露。

## What Changes
- 将 Prompt 模板改为按系统场景管理，拆分智能填充复核、导入重复复核和智能填充建议生成三个系统模板。
- 为模板增加场景元数据、系统标记和展示名称，弱化并退出“默认模板”运行时语义。
- 新增模板占位符校验、样例渲染和结构化输出预览能力，阻止无效模板进入运行时。
- 调整 Prompt 模板配置页，仅围绕系统模板提供编辑、预览测试和恢复默认内容能力。

## Impact
- Affected specs: `api`, `matching-engine`, `user-interface`, `data-storage`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Models/LlmMatchingModels.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs`
  - `src/AcceptanceSpecSystem.Api/Services/ImportDuplicateDetectionService.cs`
  - `src/AcceptanceSpecSystem.Data/Entities/PromptTemplate.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/PromptTemplateRepository.cs`
  - `web/src/api/prompt-template.ts`
  - `web/src/views/config/prompt-templates/index.vue`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
