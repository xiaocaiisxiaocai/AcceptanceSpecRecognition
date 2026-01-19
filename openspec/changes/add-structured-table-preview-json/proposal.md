# Change: 表格预览返回结构化 JSON（支持单元格内嵌套表格）

## Why
当前表格预览/导入阶段把单元格内容扁平化为纯文本，遇到“单元格里还有表格”的情况时可读性与可比对性差，且难以在前端做更准确的呈现与对比。

## What Changes
- 在表格预览接口返回中新增 `structuredRows`，以结构化 JSON 表达每个单元格内容（文本/嵌套表格/混合内容）。
- 保留现有 `rows`（纯文本二维数组）字段，确保现有导入/映射逻辑不被破坏。
- 解析层（Word）对嵌套表格进行递归提取，并设置合理的深度限制，避免结构爆炸。

## Impact
- Affected specs: `user-interface`, `word-processing`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Documents/Parsers/WordDocumentParser.cs`
  - `src/AcceptanceSpecSystem.Core/Documents/Models/*`
  - `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/DocumentDtos.cs`
  - `web/src/api/document.ts`
  - `web/src/views/data-import/components/TablePreview.vue`

