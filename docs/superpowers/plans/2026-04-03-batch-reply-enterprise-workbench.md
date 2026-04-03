# 批量回复企业工作台视觉优化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将批量回复页面收敛为稳重企业工作台视觉，同时保持现有步骤、文件、Sheet 结构不回退。

**Architecture:** 通过小范围模板整理与样式重构完成，不改后端接口与主要交互语义。视觉重点落在页头、流程导航、文件工作区和 Sheet 工作区四层。

**Tech Stack:** Vue 3、TypeScript、Element Plus、xUnit 回归测试

---

### Task 1: 锁定新的工作台视觉骨架

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] **Step 1: 写失败测试，要求页面包含工作台页头和流程导航类名**
- [ ] **Step 2: 运行测试确认先失败**

### Task 2: 重构批量回复页面视觉层级

**Files:**
- Modify: `web/src/views/batch-reply/index.vue`
- Modify: `web/src/views/smart-fill/components/BatchTableConfig.vue`

- [ ] **Step 1: 弱化横幅，改成企业工作台页头**
- [ ] **Step 2: 强化步骤导航、文件导航和 Sheet 工作区边界**
- [ ] **Step 3: 收敛提示条、标签和结果区视觉噪音**
- [ ] **Step 4: 运行回归测试并修正样式细节**

### Task 3: 完整验证

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] **Step 1: 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug --filter "ReviewRegressionTests|BatchReply"`**
- [ ] **Step 2: 运行 `pnpm --dir web build`**
