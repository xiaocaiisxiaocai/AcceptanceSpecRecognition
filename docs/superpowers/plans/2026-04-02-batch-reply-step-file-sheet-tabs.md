# 批量回复步骤化文件工作区 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将批量回复页面重构为“步骤 Tab -> 文件 Tab -> Sheet/表格 Tab”结构，并移除独立预检查区。

**Architecture:** 复用现有逐表预览与执行 API，优先重构前端页面状态与展示层级。`BatchTableConfig.vue` 负责承载 Sheet/表格级工作区，`web/src/views/batch-reply/index.vue` 负责步骤与文件层级编排。

**Tech Stack:** Vue 3、TypeScript、Element Plus、Node test、xUnit 回归测试、OpenSpec

---

### Task 1: 先锁定新结构的失败测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] **Step 1: 写失败测试，断言批量回复页面使用“来源文件 / 目标文件 / 执行结果”步骤 Tab**

```csharp
content.Should().Contain("来源文件");
content.Should().Contain("目标文件");
content.Should().Contain("执行结果");
content.Should().NotContain("来源配置");
content.Should().NotContain("目标配置");
```

- [ ] **Step 2: 写失败测试，断言目标文件步骤使用文件 Tab + Sheet/表格 Tab**

```csharp
content.Should().Contain("target-file-tabs");
content.Should().Contain("sheet-tabs");
content.Should().Contain("Sheet/表格");
```

- [ ] **Step 3: 写失败测试，断言页面移除独立预检查区**

```csharp
content.Should().NotContain("当前表回写预览");
content.Should().NotContain("请在当前目标文件的表格卡片上点击“预览回写”");
```

- [ ] **Step 4: 运行测试并确认先失败**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug --filter ReviewRegressionTests`
Expected: FAIL，提示新文案或旧区域移除断言不满足

### Task 2: 重构 Sheet/表格级配置工作区

**Files:**
- Modify: `web/src/views/smart-fill/components/BatchTableConfig.vue`
- Modify: `web/src/views/data-import/components/TablePreview.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] **Step 1: 将 `BatchTableConfig.vue` 从卡片列表重构为 Sheet/表格 Tab 工作区**

- [ ] **Step 2: 在当前 Sheet/表格上下文内展示行设置、项目列、规格列、验收列、备注列**

- [ ] **Step 3: 在目标表场景下保留来源表选择和预览入口，但不再依赖外部独立预览区**

- [ ] **Step 4: 运行回归测试，确认结构断言开始通过**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug --filter ReviewRegressionTests`
Expected: PASS

### Task 3: 重构批量回复页面步骤与文件层级

**Files:**
- Modify: `web/src/views/batch-reply/index.vue`
- Modify: `web/src/api/matching.ts`
- Test: `web/tests/batch-reply-target-upload.test.ts`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] **Step 1: 将顶层步骤文案改为“来源文件 / 目标文件 / 执行结果”**

- [ ] **Step 2: 让来源文件步骤显式承载文件 Tab，再在内部渲染 Sheet/表格 Tab 工作区**

- [ ] **Step 3: 让目标文件步骤保留文件 Tab，并把每个文件内部的配置改为 Sheet/表格 Tab 工作区**

- [ ] **Step 4: 删除独立“当前表回写预览”卡片与相关空状态文案**

- [ ] **Step 5: 保持执行逻辑仍以已完成配置的目标文件为单位，不新增独立预检查步骤**

- [ ] **Step 6: 运行前端相关测试与回归测试**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug --filter "BatchReply|ReviewRegressionTests"`
Expected: PASS

### Task 4: 全量验证

**Files:**
- Modify: `openspec/changes/refactor-batch-reply-independent-table-config/proposal.md`
- Modify: `openspec/changes/refactor-batch-reply-independent-table-config/tasks.md`
- Modify: `openspec/changes/refactor-batch-reply-independent-table-config/specs/user-interface/spec.md`

- [ ] **Step 1: 运行 OpenSpec 校验**

Run: `openspec validate refactor-batch-reply-independent-table-config --strict`
Expected: PASS

- [ ] **Step 2: 运行后端测试**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug`
Expected: PASS

- [ ] **Step 3: 运行前端构建**

Run: `pnpm --dir web build`
Expected: PASS

- [ ] **Step 4: 记录验证结果并整理风险说明**

需要明确说明：
- 是否完全移除了独立预检查区
- 智能填充是否未受 `BatchTableConfig.vue` 重构影响
- 是否仍存在仅样式层面的后续优化项
