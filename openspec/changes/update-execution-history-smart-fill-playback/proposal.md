# Change: 执行记录支持智能填充完整回放

## Why
当前执行记录只保存任务级统计和较薄的逐行结果，无法回答“系统当时怎么匹配”“AI 是否参与改选”“用户是否人工确认或人工改写”这些复盘问题。用户需要在执行记录中尽可能还原智能填充页面，并允许同时看到匹配来源和人工干预结果，而不是只能看最终写回值。

## What Changes
- 扩展智能填充执行记录，为每个任务持久化“执行前完整预览归档 + 执行时最终选择归档”。
- 执行记录详情返回智能填充专用回放结构，包含最佳匹配、候选、决策、选定方式、AI 复核信息、人工确认和人工写入信息。
- 执行记录列表补充智能填充任务的分类汇总，支持前端以下拉框方式选择任务并展示摘要卡。
- 执行记录界面改为“任务下拉 + 任务摘要 + 详情回放”结构；智能填充详情尽量复用现有智能填充详情展示能力。
- 批量回复详情保持简化视图，不补候选与 AI 回放。
- 历史未归档旧记录允许降级展示，但不得在查看详情时重新调用 AI 或重新匹配。

## Impact
- Affected specs: `api`, `data-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Services/ExecutionHistory*`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/ExecutionHistoryDtos.cs`
  - `src/AcceptanceSpecSystem.Data/Entities/ExecutionHistoryRecord.cs`
  - `web/src/views/other/execution-history/index.vue`
  - `web/src/api/execution-history.ts`
  - `web/src/views/smart-fill/components/*`
