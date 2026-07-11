# Change: remove legacy column mapping and harden smart-fill

## Why
当前分支已经转向 AI 主导的智能填充链路，但前后端与数据库中仍保留旧的列映射规则能力；同时 smart-fill 前端仍缺少 Embedding 空态/范围空态前置引导，并且执行填充与下载权限耦合，下载失败后没有恢复入口。这些残留会误导用户，也会继续拖住代码和数据库结构。

## What Changes
- **BREAKING** 删除旧 `ColumnMappingRules` API、前端配置页、数据导入自动预填规则链路及数据库表/仓储/DTO。
- 为 smart-fill 增加 Embedding 不可用、范围内无候选数据的前置引导与明确空态文案。
- 解耦 smart-fill 的执行权限与下载权限；执行成功但下载失败后保留可重试的下载入口。
- 清理 smart-fill 详情区旧“存在差异，请先确认 / 核对差异后再填充”语义和误导性旧规则文案。

## Impact
- Affected specs: `api`, `user-interface`, `data-storage`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/*`
  - `src/AcceptanceSpecSystem.Data/*`
  - `web/src/views/data-import/*`
  - `web/src/views/smart-fill/*`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
  - `tests/AcceptanceSpecSystem.Data.Tests/*`
  - `web/tests/smart-fill-ai-equivalence.test.ts`
