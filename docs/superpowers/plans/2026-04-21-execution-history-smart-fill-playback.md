# Execution History Smart Fill Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把执行记录改造成可回放智能填充过程的页面，展示“完全匹配 / AI匹配 / 人工确认 / 人工写入 / 未采用或未匹配”标签，并保持批量回复详情简化。

**Architecture:** 继续使用 `ExecutionHistoryRecord.DetailJson` 承载详情，但把智能填充详情升级为带版本号的完整回放结构，拆成 `previewSnapshot` 与 `executionSnapshot` 两段；执行时直接复用前端已有预览数据归档，不为查看执行记录新增 AI 或匹配调用；执行记录列表额外返回任务摘要与任务下拉所需统计字段；前端执行记录页改成“任务下拉 + 摘要卡 + 回放详情”，智能填充尽量复用现有展示组件和格式化函数，批量回复走简化分支，历史旧记录走降级提示。

**Tech Stack:** ASP.NET Core 8、EF Core 8、xUnit、Vue 3、TypeScript、Element Plus

---

### Task 1: 定义执行记录回放契约

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/ExecutionHistoryDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryModels.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryAppService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 写失败测试，约束智能填充详情返回版本化回放结构**
- [ ] **Step 2: 运行测试确认失败，失败点应是详情字段缺失**
- [ ] **Step 3: 定义智能填充详情、批量回复简化详情、历史降级标记和任务摘要 DTO 的最小模型**
- [ ] **Step 4: 跑目标测试，确认新契约已生效且旧记录仍可反序列化**

### Task 2: 归档智能填充完整回放

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/index.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 写失败测试，约束执行记录详情能返回预览快照、执行快照和按固定顺序输出的标签**
- [ ] **Step 2: 运行测试确认失败，失败点应是执行记录未归档这些字段**
- [ ] **Step 3: 给执行请求补充最小预览归档字段，并在执行保存时直接落到执行记录**
- [ ] **Step 4: 跑目标测试，确认查看详情不依赖事后重算**

### Task 3: 兼容批量回复与历史旧记录

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryAppService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 写失败测试，约束批量回复详情保持简化结构，旧智能填充记录返回降级提示**
- [ ] **Step 2: 运行测试确认失败**
- [ ] **Step 3: 实现批量回复简化详情分支和旧记录降级读取逻辑**
- [ ] **Step 4: 跑目标测试，确认两条业务线互不污染**

### Task 4: 改造执行记录页面

**Files:**
- Modify: `web/src/api/execution-history.ts`
- Modify: `web/src/views/other/execution-history/index.vue`
- Create: `web/src/views/other/execution-history/components/ExecutionHistorySmartFillPlayback.vue`
- Create: `web/src/views/other/execution-history/components/ExecutionHistoryBatchReplyDetail.vue`
- Modify: `web/src/views/smart-fill/components/scoreDetail.formatters.ts`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryFrontendRegressionTests.cs`

- [ ] **Step 1: 写失败测试，约束页面切到任务下拉、摘要卡和智能填充回放组件**
- [ ] **Step 2: 运行测试确认失败**
- [ ] **Step 3: 实现前端最小可用页面，智能填充回放尽量复用现有展示样式和格式化函数**
- [ ] **Step 4: 跑前端回归测试，确认批量回复简化详情和旧记录降级提示都已接入**

### Task 5: 验证与收口

**Files:**
- Modify: `openspec/changes/update-execution-history-smart-fill-playback/tasks.md`

- [ ] **Step 1: 运行后端相关测试**
  `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "ExecutionHistoryApiTests"`
- [ ] **Step 2: 运行前端类型检查或构建**
  `pnpm typecheck`
- [ ] **Step 3: 按实际完成情况勾选 OpenSpec tasks**
