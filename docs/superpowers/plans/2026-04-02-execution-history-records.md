# Execution History Records Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为智能填充与批量回复新增可查询的执行记录能力，支持任务级列表和按文件、Sheet 展示的逐行结果详情。

**Architecture:** 在数据库新增执行记录主表，保存任务级摘要字段与详情 JSON；智能填充和批量回复在执行完成后统一生成记录；API 暴露列表与详情查询；前端新增记录列表页和详情页，详情按任务 -> 文件 -> Sheet -> 行记录呈现。现有下载产物与临时快照继续保留原职责，不作为正式查询来源。

**Tech Stack:** ASP.NET Core 8、EF Core 8、MySQL、xUnit、Vue 3、TypeScript、Element Plus

---

### Task 1: 执行记录持久化模型

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Entities/ExecutionHistoryRecord.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/IUnitOfWork.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/UnitOfWork.cs`
- Create: `src/AcceptanceSpecSystem.Data/Repositories/IExecutionHistoryRecordRepository.cs`
- Create: `src/AcceptanceSpecSystem.Data/Repositories/ExecutionHistoryRecordRepository.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/<timestamp>_AddExecutionHistoryRecords.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 写失败测试，约束执行记录实体可通过 API 查询**
- [ ] **Step 2: 运行测试确认失败，失败原因应为实体/接口不存在**
- [ ] **Step 3: 新增实体、仓储、DbSet 与 Migration 的最小实现**
- [ ] **Step 4: 再跑测试，确认进入下一层失败或通过基础建表**

### Task 2: 智能填充执行后生成记录

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Create: `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryBuilder.cs`
- Create: `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryAppService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchFillTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExcelFillFlowTests.cs`

- [ ] **Step 1: 写失败测试，覆盖智能填充完成后会生成一条执行记录**
- [ ] **Step 2: 运行该测试并确认失败原因是未持久化执行记录**
- [ ] **Step 3: 实现智能填充记录构建与保存的最小代码**
- [ ] **Step 4: 运行目标测试，确认记录摘要与详情 JSON 符合契约**

### Task 3: 批量回复执行后生成记录

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/ExecutionHistoryBuilder.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 写失败测试，覆盖批量回复执行后生成一条任务级记录并带多文件详情**
- [ ] **Step 2: 运行测试确认失败**
- [ ] **Step 3: 以最小实现补齐批量回复记录保存逻辑**
- [ ] **Step 4: 运行测试确认文件级与 Sheet 级详情结构正确**

### Task 4: 执行记录查询 API

**Files:**
- Create: `src/AcceptanceSpecSystem.Api/Controllers/ExecutionHistoryController.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 写失败测试，约束列表接口和详情接口返回结构**
- [ ] **Step 2: 运行测试确认接口尚不存在**
- [ ] **Step 3: 实现最小控制器、DTO 和查询服务**
- [ ] **Step 4: 运行测试，确认权限范围和响应字段正确**

### Task 5: 前端记录页面

**Files:**
- Create: `web/src/api/execution-history.ts`
- Create: `web/src/views/execution-history/index.vue`
- Create: `web/src/views/execution-history/detail.vue`
- Modify: `shared/navigation/navigation-manifest.json`
- Modify: `web/src/router/modules/other.ts`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryFrontendRegressionTests.cs`

- [ ] **Step 1: 写失败测试，约束导航和页面文件存在且接入 API**
- [ ] **Step 2: 运行测试确认失败**
- [ ] **Step 3: 实现列表页与详情页最小界面，详情按文件 -> Sheet -> 行记录展示**
- [ ] **Step 4: 运行前端回归测试，确认页面包含状态、置信度与人工选择列**

### Task 6: 全量验证

**Files:**
- Modify: `openspec/changes/add-execution-history-records/tasks.md`

- [ ] **Step 1: 运行后端相关测试**
  - `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "ExecutionHistoryApiTests|BatchReplyApiTests|BatchFillTests|ExcelFillFlowTests"`
- [ ] **Step 2: 运行前端构建**
  - `pnpm build`
- [ ] **Step 3: 根据实际完成情况勾选 OpenSpec tasks**
