# Change: 导入前支持剔除待导入行

## Why
当前“确认导入”步骤只能整体导入，用户无法在写入数据库前剔除不需要的数据，容易把无关或脏数据一并导入数据库。

## What Changes
- 在“确认导入”步骤增加待导入数据预览，并支持单个删除与批量删除。
- 导入请求增加“本次剔除行”参数，后端按该参数跳过对应行。
- 预计导入数量按剔除后的结果实时更新。

## Impact
- Affected specs: `user-interface`
- Affected code: `web/src/views/data-import/index.vue`, `web/src/api/document.ts`, `src/AcceptanceSpecSystem.Api/DTOs/DocumentDtos.cs`, `src/AcceptanceSpecSystem.Api/DTOs/ExcelImportDtos.cs`, `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs`
