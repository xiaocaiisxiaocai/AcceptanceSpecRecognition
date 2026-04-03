# 匹配知识运行时来源重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将匹配知识改为数据库唯一运行时来源，并同步调整 API、前端页面与默认种子初始化语义。

**Architecture:** 后端移除 `builtIn/custom/effective` 运行时分层语义，改为数据库当前配置单一事实源；默认知识仅用于空库初始化和显式恢复默认。前端配置页改为单一可编辑配置视图，AI 草稿基于当前数据库配置去重与导入。

**Tech Stack:** ASP.NET Core 8、EF Core 8、xUnit、Vue 3、TypeScript、OpenSpec

---

### Task 1: 先写后端失败测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ConfigurationMatchingKnowledgeProviderTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeBootstrapperTests.cs`

- [ ] **Step 1: 写出新的 API 语义失败测试**
- [ ] **Step 2: 运行目标测试并确认因旧分层语义失败**
- [ ] **Step 3: 写出 Provider 与 Bootstrapper 的新语义失败测试**
- [ ] **Step 4: 再次运行目标测试并确认失败原因正确**

### Task 2: 实现后端数据库唯一事实源

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeComposition.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/ConfigurationMatchingKnowledgeProvider.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeBootstrapper.cs`

- [ ] **Step 1: 实现 DTO 与 Controller 新契约**
- [ ] **Step 2: 实现 Composition 新职责**
- [ ] **Step 3: 实现 Provider 与 Bootstrapper 新语义**
- [ ] **Step 4: 运行后端目标测试直至转绿**

### Task 3: 调整前端配置页与接口

**Files:**
- Modify: `web/src/api/matching-knowledge.ts`
- Modify: `web/src/views/config/matching-knowledge/index.vue`
- Modify: `web/src/views/config/matching-knowledge/components/MatchingKnowledgeDraftDialog.vue`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`

- [ ] **Step 1: 先改前端回归测试为新语义**
- [ ] **Step 2: 运行回归测试并确认失败**
- [ ] **Step 3: 实现前端单层配置视图与新接口**
- [ ] **Step 4: 运行前端回归测试直至转绿**

### Task 4: 更新 OpenSpec 与验证

**Files:**
- Modify: `openspec/changes/refactor-matching-knowledge-runtime-source/tasks.md`
- Validate: `openspec/changes/refactor-matching-knowledge-runtime-source/*`

- [ ] **Step 1: 根据真实实现勾选完成项**
- [ ] **Step 2: 运行 `openspec validate refactor-matching-knowledge-runtime-source --strict`**
- [ ] **Step 3: 运行匹配知识相关后端测试**
- [ ] **Step 4: 运行前端构建或最小验证命令**
