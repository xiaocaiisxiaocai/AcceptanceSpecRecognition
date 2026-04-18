# TopK AI 重排 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Embedding 已召回的 TopK 候选中增加一次 AI 改选能力，再对 AI 选中的当前最佳执行现有等价裁决门禁。

**Architecture:** 保持“精确一致直达”和“Embedding 召回”不变，在 `SemanticKernelMatchingService` 的召回后、本地 Top1 与最终等价裁决之间插入一次独立的 TopK AI 重排。结果通过 DTO 透出 `selectionMode` 与 `selectionSummary`，前端只做轻量展示。

**Tech Stack:** .NET 8、Semantic Kernel、xUnit、Vue 3、TypeScript

---

### Task 1: 补核心红灯测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs`

- [ ] **Step 1: 写失败测试**
- [ ] **Step 2: 运行单测确认因“尚未实现 TopK AI 重排”而失败**
- [ ] **Step 3: 再补“精确直达不触发重排”和“重排失败回退”测试**
- [ ] **Step 4: 再次运行单测，确认仍为预期失败**

### Task 2: 实现 Core TopK AI 重排

**Files:**
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Interfaces/ILlmAssistService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/LlmMatchingModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/PromptTemplateModel.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateValidationService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Entities/PromptTemplate.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs`

- [ ] **Step 1: 增加 TopK AI 重排请求/结果模型与接口**
- [ ] **Step 2: 增加 Prompt 场景、模板与校验**
- [ ] **Step 3: 在匹配主链路中接入 AI 重排，并保留现有等价裁决门禁**
- [ ] **Step 4: 让结果模型产出 `selectionMode` / `selectionSummary`**
- [ ] **Step 5: 运行 Core 单测，确认转绿**

### Task 3: 打通 API 返回与测试替身

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/TestLlmServices.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/MatchingPreviewLlmAssistTests.cs`

- [ ] **Step 1: DTO 增加选中方式字段**
- [ ] **Step 2: 应用服务映射新增字段**
- [ ] **Step 3: 测试替身支持 TopK AI 改选**
- [ ] **Step 4: 写 API 红灯测试并跑失败**
- [ ] **Step 5: 代码补齐后跑 API 测试转绿**

### Task 4: 前端展示 AI 改选与精确直达

**Files:**
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue`
- Modify: `web/src/views/smart-fill/components/ScoreDetailCandidateList.vue`
- Modify: `web/src/views/smart-fill/components/scoreDetail.formatters.ts`

- [ ] **Step 1: 前端类型补充 `selectionMode` / `selectionSummary`**
- [ ] **Step 2: 最佳匹配区展示“精确直达 / AI 改选 / 本地 Top1”**
- [ ] **Step 3: 候选列表展示 AI 改选摘要**
- [ ] **Step 4: 跑相关前端测试或构建验证**

### Task 5: 回归验证

**Files:**
- Modify: `openspec/changes/update-topk-ai-rerank/proposal.md`
- Modify: `openspec/changes/update-topk-ai-rerank/tasks.md`

- [ ] **Step 1: 更新 openspec 任务状态**
- [ ] **Step 2: 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`**
- [ ] **Step 3: 运行 `pnpm build`（目录 `web/`）**
- [ ] **Step 4: 若失败则修复并重跑**
