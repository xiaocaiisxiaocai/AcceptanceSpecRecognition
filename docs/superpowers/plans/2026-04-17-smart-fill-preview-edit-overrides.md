# 智能填充预览页内编辑导出覆盖值 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让用户在智能填充预览页直接编辑单行验收标准和备注，并将修改结果仅用于本次导出写回。

**Architecture:** 保持现有“预览选择 -> 执行填充 -> 导出文件”链路不变，只在单行选择模型中增加一次性覆盖值，并让前端表格展示与后端写回都优先消费该覆盖值。规格主数据、匹配详情链路和数据库结构不做扩展。

**Tech Stack:** Vue 3 + TypeScript + Element Plus、ASP.NET Core 8、xUnit、Node test

---

### Task 1: 前端契约与交互测试先行

**Files:**
- Modify: `web/tests/smart-fill-ai-equivalence.test.ts`
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/components/BatchPreviewTabs.vue`
- Modify: `web/src/views/smart-fill/components/MatchPreviewTable.vue`
- Modify: `web/src/views/smart-fill/index.vue`

- [ ] **Step 1: 先补前端测试**
- [ ] **Step 2: 运行前端定向测试，确认先红**
- [ ] **Step 3: 扩展前端 `FillMapping` 与选择缓存模型，支持覆盖值透传**
- [ ] **Step 4: 在预览表增加编辑弹窗、`保存并采用` 行为与 `已编辑` 展示**
- [ ] **Step 5: 回归前端定向测试**

### Task 2: 后端执行契约与写回测试先行

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/LlmMatchingAssistFillTests.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`

- [ ] **Step 1: 先补后端集成测试，覆盖“执行请求携带覆盖值后写回导出文件”**
- [ ] **Step 2: 运行对应 API 测试，确认先红**
- [ ] **Step 3: 扩展执行 DTO，支持 `overrideAcceptance`、`overrideRemark`**
- [ ] **Step 4: 在匹配工作流中优先使用覆盖值构造写回结果与执行历史**
- [ ] **Step 5: 回归后端定向测试**

### Task 3: 验证与收口

**Files:**
- Verify: `web/src/views/smart-fill/*`
- Verify: `src/AcceptanceSpecSystem.Api/*`

- [ ] **Step 1: 运行 `node --test --experimental-strip-types web/tests/smart-fill-ai-equivalence.test.ts`**
- [ ] **Step 2: 运行 `pnpm --dir web typecheck`**
- [ ] **Step 3: 运行 `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter LlmMatchingAssistFillTests`**
- [ ] **Step 4: 运行 `dotnet build AcceptanceSpecSystem.sln`**
