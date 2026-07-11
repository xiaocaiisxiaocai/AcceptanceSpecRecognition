# Change: 新增智能填充与批量回复执行记录

## Why
当前智能填充与批量回复在执行后只保留下载产物或临时快照，缺少可查询的任务历史。用户无法按任务回看整份文件的匹配结果，也无法直接查看每个文件、每个 Sheet 下逐行的未匹配、跳过、未采用、已采用记录，导致复盘和追溯成本高。

## What Changes
- 为智能填充与批量回复新增统一的执行记录持久化能力。
- 在数据库中为每次执行保存结构化摘要字段，并以数据库字段保存 `文件 -> Sheet -> 行记录` 的详情 JSON。
- 新增执行记录列表与详情 API，支持按任务读取汇总和完整明细。
- 新增执行记录界面，按“任务 -> 文件 -> Sheet -> 行记录”展示结果。
- 行记录直接展示状态、置信度百分比与人工选择标记，不要求用户再打开二级详情。

## Impact
- Affected specs: `api`, `data-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Services/` 智能填充与批量回复执行链路
  - `src/AcceptanceSpecSystem.Data/Entities/` 与 EF Core Migration
  - `src/AcceptanceSpecSystem.Api/Controllers/` 执行记录查询接口
  - `web/src/views/` 记录列表与详情页面
