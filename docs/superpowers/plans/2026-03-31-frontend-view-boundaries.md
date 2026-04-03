# Frontend View Boundaries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变现有交互、文案、视觉和接口契约的前提下，拆分高复杂度前端视图文件，建立页面编排壳、聚焦展示组件与本地 composable 的清晰边界。

**Architecture:** `data-import/index.vue` 保留步骤导航和顶层状态装配，步骤内容与差异确认弹窗拆为受控组件；`ScoreDetailDialog.vue` 保留弹窗壳职责，最佳匹配、差异区块和候选列表拆为聚焦组件，diff/格式化逻辑迁移到本地 helper 与 composable。所有迁移分批执行，每批先写结构回归测试，再做最小实现，并以 `pnpm typecheck`、`pnpm build` 验证无回归。

**Tech Stack:** Vue 3、TypeScript、Element Plus、Vite、xUnit 源码结构回归测试、OpenSpec

---

### Task 1: 写结构回归测试，锁定重构目标边界

**Files:**
- Create: `tests/AcceptanceSpecSystem.Api.Tests/FrontendViewBoundaryRefactorTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj`（如需自动包含新测试文件则无需改动）
- Reference: `web/src/views/data-import/index.vue`
- Reference: `web/src/views/smart-fill/components/ScoreDetailDialog.vue`

- [ ] **Step 1: 写失败测试，约束新文件和页面壳边界**
- [ ] **Step 2: 运行定向测试，确认当前实现失败**
  Run: `dotnet test .\tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendViewBoundaryRefactorTests"`
  Expected: FAIL，原因是新组件 / composable 文件尚不存在，页面壳尚未引用它们
- [ ] **Step 3: 在测试中约束以下结构**
  - `web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue`
  - `web/src/views/smart-fill/components/ScoreDetailDiffSection.vue`
  - `web/src/views/smart-fill/components/ScoreDetailCandidateList.vue`
  - `web/src/views/smart-fill/composables/useScoreDetailDiff.ts`
  - `web/src/views/smart-fill/components/scoreDetail.formatters.ts`
  - `web/src/views/data-import/components/DataImportStepUpload.vue`
  - `web/src/views/data-import/components/DataImportStepTableSelect.vue`
  - `web/src/views/data-import/components/DataImportStepMapping.vue`
  - `web/src/views/data-import/components/DataImportStepTarget.vue`
  - `web/src/views/data-import/components/DataImportStepConfirm.vue`
  - `web/src/views/data-import/components/DataImportDifferenceDialog.vue`
  - `web/src/views/data-import/composables/useDataImportMapping.ts`
  - `web/src/views/data-import/composables/useDataImportPreviewSelection.ts`
  - `web/src/views/data-import/composables/useDataImportExecution.ts`
  - `web/src/views/data-import/dataImport.types.ts`
  - `web/src/views/data-import/dataImport.helpers.ts`
- [ ] **Step 4: 提交测试骨架**

### Task 2: 拆分 ScoreDetailDialog

**Files:**
- Create: `web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue`
- Create: `web/src/views/smart-fill/components/ScoreDetailDiffSection.vue`
- Create: `web/src/views/smart-fill/components/ScoreDetailCandidateList.vue`
- Create: `web/src/views/smart-fill/components/scoreDetail.formatters.ts`
- Create: `web/src/views/smart-fill/composables/useScoreDetailDiff.ts`
- Modify: `web/src/views/smart-fill/components/ScoreDetailDialog.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/FrontendViewBoundaryRefactorTests.cs`

- [ ] **Step 1: 抽格式化函数与 diff/comparison 派生逻辑**
- [ ] **Step 2: 运行定向测试，确认仍然失败在“主弹窗壳尚未接线”**
- [ ] **Step 3: 拆最佳匹配、差异区和候选列表组件，保持模板顺序和 class 名不变**
- [ ] **Step 4: 将 `ScoreDetailDialog.vue` 收敛为弹窗壳并接线**
- [ ] **Step 5: 运行定向测试，确认 Task 2 相关断言通过**
- [ ] **Step 6: 提交阶段性改动**

### Task 3: 拆分 DataImport 确认区、差异弹窗与步骤面板

**Files:**
- Create: `web/src/views/data-import/dataImport.types.ts`
- Create: `web/src/views/data-import/dataImport.helpers.ts`
- Create: `web/src/views/data-import/composables/useDataImportMapping.ts`
- Create: `web/src/views/data-import/composables/useDataImportPreviewSelection.ts`
- Create: `web/src/views/data-import/composables/useDataImportExecution.ts`
- Create: `web/src/views/data-import/components/DataImportStepUpload.vue`
- Create: `web/src/views/data-import/components/DataImportStepTableSelect.vue`
- Create: `web/src/views/data-import/components/DataImportStepMapping.vue`
- Create: `web/src/views/data-import/components/DataImportStepTarget.vue`
- Create: `web/src/views/data-import/components/DataImportStepConfirm.vue`
- Create: `web/src/views/data-import/components/DataImportDifferenceDialog.vue`
- Modify: `web/src/views/data-import/index.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/FrontendViewBoundaryRefactorTests.cs`

- [ ] **Step 1: 先抽类型与纯 helper，不改模板顺序**
- [ ] **Step 2: 运行定向测试，确认仍然失败在步骤组件 / composable 未完全接线**
- [ ] **Step 3: 拆确认区与差异弹窗，页面壳保留真实状态**
- [ ] **Step 4: 拆上传、表格选择、映射配置与目标选择步骤面板**
- [ ] **Step 5: 引入本地 composable 收敛映射、预览选择和导入执行逻辑**
- [ ] **Step 6: 运行定向测试，确认结构断言全部通过**
- [ ] **Step 7: 提交阶段性改动**

### Task 4: 全量验证与计划回写

**Files:**
- Modify: `openspec/changes/refactor-frontend-view-boundaries/tasks.md`
- Verify: `web/`
- Verify: `tests/AcceptanceSpecSystem.Api.Tests/FrontendViewBoundaryRefactorTests.cs`

- [ ] **Step 1: 运行结构回归定向测试**
  Run: `dotnet test .\tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~FrontendViewBoundaryRefactorTests"`
  Expected: PASS
- [ ] **Step 2: 运行前端类型检查**
  Run: `pnpm typecheck`
  Workdir: `web`
  Expected: PASS
- [ ] **Step 3: 运行前端生产构建**
  Run: `pnpm build`
  Workdir: `web`
  Expected: PASS
- [ ] **Step 4: 运行 OpenSpec 严格校验**
  Run: `openspec validate refactor-frontend-view-boundaries --strict`
  Expected: PASS
- [ ] **Step 5: 将 `openspec/changes/refactor-frontend-view-boundaries/tasks.md` 更新为已完成状态**
- [ ] **Step 6: 准备最终变更说明**
