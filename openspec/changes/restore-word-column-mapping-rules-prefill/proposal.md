# Change: restore word column mapping rules prefill

## Why
当前分支删除 `ColumnMappingRules` 后，Word 导入和智能填充都只能靠本地默认列位或逐表手工调整。对于一个文件含十几个甚至几十个 Word 表格的场景，这会显著降低操作效率，也不符合当前业务对“先自动预填、再人工微调”的真实需求。

## What Changes
- 恢复 `ColumnMappingRules` 后端持久化、管理 API 与前端配置页。
- 仅在 `Word` 的数据导入和智能填充中，基于列映射规则对表头做自动预填。
- `Excel` 继续保持人工列配置，不套用列映射规则。
- 列映射规则只负责列预匹配，不参与 Embedding、召回、重排、AI 复核或等价裁决。

## Impact
- Affected specs: `api`, `user-interface`, `data-storage`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/*`
  - `src/AcceptanceSpecSystem.Data/*`
  - `web/src/api/*`
  - `web/src/views/config/*`
  - `web/src/views/data-import/*`
  - `web/src/views/smart-fill/*`
  - `tests/*`
