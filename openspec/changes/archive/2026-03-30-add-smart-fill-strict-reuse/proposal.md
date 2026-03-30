# Change: 智能填充增加严格模式一次性复用

## Why
当前智能填充完成后，如果用户手头还有多份完全相同模板的验规文件，仍然需要逐份重新上传、重新匹配、重新确认，操作成本高且会引入不必要的匹配差异。用户明确要求在“文件格式和内容结构完全一致”的前提下，直接复用刚刚确认好的填充结果，并且该过程不再依赖 AI，也不沉淀为长期模板。

## What Changes
- 在智能填充完成后新增“应用到相同验规”入口，允许用户基于当前填充结果发起一次性严格复用。
- 增加严格模式预检与执行接口，校验目标文件是否与来源文件在类型、表格配置、数据区行数以及项目+规格顺序上完全一致。
- 复用执行时直接采用来源填充结果中已确认的验收/备注写回值，不重新匹配、不调用 AI、不重新选择规格。
- 复用方案仅作为当前填充任务的临时会话数据存在，不提供长期模板保存、模板管理或跨历史任务选择能力。
- 多目标文件复用完成后支持统一下载结果；多文件场景建议打包为单个压缩包。

## Impact
- Affected specs: `user-interface`, `api`
- Affected code:
  - `web/src/views/smart-fill/index.vue`
  - `web/src/views/smart-fill/components/*`
  - `web/src/api/matching.ts`
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/*`（如需抽出严格复用校验/打包服务）
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
