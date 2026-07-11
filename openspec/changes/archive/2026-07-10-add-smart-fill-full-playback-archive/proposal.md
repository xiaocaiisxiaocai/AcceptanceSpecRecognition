# Change: 智能填充完整回放外置归档

## Why
当前智能填充执行记录把完整回放明细写入单条 `ExecutionHistoryRecord.DetailJson`，持久化阈值为 512KB。大批量任务会触发精简，导致每次匹配的候选、证据、AI 裁决与原文等详细信息被剥离。

用户要求每一次匹配都能看到所有详细信息，因此不能继续依赖单条执行记录 JSON 承载完整明细。

## What Changes
- 智能填充执行记录保留轻量摘要与逐行索引在 `DetailJson` 中。
- 完整 `SmartFillPlayback` 明细保存为文件系统归档，不再受 512KB 执行记录详情阈值限制。
- 新增按执行记录读取完整回放或按行读取完整匹配详情的接口。
- 前端执行记录详情默认加载轻量回放，用户展开行详情时按需加载完整匹配明细。
- 保留现有精简逻辑作为归档异常或旧记录的降级兜底。

## Impact
- Affected specs: `api`, `file-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryAppService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/ExecutionHistorySmartFillSlimmer.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/ExecutionHistoryDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/ExecutionHistoryController.cs`
  - `src/AcceptanceSpecSystem.Api/Services/FileStorageService.cs`
  - `web/src/api/execution-history.ts`
  - `web/src/views/other/execution-history/components/ExecutionHistorySmartFillPlayback.vue`
  - `tests/AcceptanceSpecSystem.Api.Tests/ExcelFillPacketLimitRegressionTests.cs`
