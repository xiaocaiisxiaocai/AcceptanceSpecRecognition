# Change: 新增基于已回复文档的批量回复能力

## Why
现有“严格复用”只能从刚完成的智能填充任务发起，无法满足用户直接上传一份人工已经回复好的同模板文档，再将其验收与备注批量应用到其他同模板文件的业务场景。

## What Changes
- 新增独立菜单“批量回复”，支持上传人工已回复的 `docx/xlsx` 作为来源文件。
- 新增独立批量回复 API，支持本地上传目标文件、预检、执行和结果下载。
- 新增独立 RBAC 菜单/页面/按钮权限，不与“智能填充”权限混用。
- 新增独立批量回复会话模型，复用现有严格复用的判定规则与写回基础设施。
- 新增临时上传文件存储与清理约束，用于来源文件、目标文件和执行产物。

## Impact
- Affected specs: `api`, `user-interface`, `architecture`, `file-storage`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Controllers`
  - `src/AcceptanceSpecSystem.Api/Services`
  - `web/src/router`
  - `web/src/views`
  - `shared/navigation/navigation-manifest.json`
  - `tests/AcceptanceSpecSystem.Api.Tests`
