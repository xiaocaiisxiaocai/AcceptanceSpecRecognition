# Change: 导入阶段增加 AI 疑似重复识别与人工确认

## Why
当前数据导入只支持“完全相同直接跳过”和“项目+规格完全相同进入差异确认”两种规则命中，无法识别语义相近但文本不完全一致的重复规格，也无法让用户在完全重复场景下手动确认是否覆盖已有数据。

## What Changes
- 在 Word / Excel 导入流程中新增可选的 AI 疑似重复识别配置。
- 保留规则命中优先：完全重复、项目+规格相同但内容不同仍优先命中。
- 对规则未命中的导入行，使用 Embedding 召回候选，再可选使用 LLM 做语义裁决。
- 将导入确认语义统一为“覆盖已有 / 跳过”，不再把确认导入实现为新增一条重复数据。
- 在导入确认弹窗中展示命中类型、相似度、LLM 结论与左右对照数据。

## Impact
- Affected specs: `user-interface`, `matching-engine`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/DocumentDtos.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/ExcelImportDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/ImportDuplicateDetectionService.cs`
  - `web/src/api/document.ts`
  - `web/src/views/data-import/index.vue`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
