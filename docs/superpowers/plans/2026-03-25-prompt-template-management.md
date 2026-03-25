# Prompt 模板管理 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Prompt 模板改为按系统场景管理，并增加模板校验、预览与恢复默认能力。

**Architecture:** 后端新增模板场景注册与校验服务，运行时从场景而非默认模板读取内容；前端将 Prompt 模板页改为系统模板管理界面，只暴露与运行时一致的操作。数据库通过迁移补齐模板场景元数据并兼容旧数据。

**Tech Stack:** ASP.NET Core 8、EF Core 8、Vue 3、TypeScript、xUnit

---

### Task 1: 补规范与测试骨架

**Files:**
- Modify: `openspec/changes/update-prompt-template-management/*`
- Create: `tests/AcceptanceSpecSystem.Api.Tests/PromptTemplateControllerTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj`

- [ ] **Step 1: 写失败测试，覆盖场景模板查询和非法占位符保存**
- [ ] **Step 2: 运行对应测试并确认按预期失败**
- [ ] **Step 3: 记录需要新增的 DTO、实体字段和接口**

### Task 2: 实现后端场景化与校验

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Entities/PromptTemplate.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/IPromptTemplateRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/PromptTemplateRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/LlmMatchingModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/ImportDuplicateDetectionService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs`
- Create: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateValidationService.cs`

- [ ] **Step 1: 先让失败测试覆盖到新的场景字段和接口返回**
- [ ] **Step 2: 实现最小代码使测试转绿**
- [ ] **Step 3: 补迁移和旧数据映射逻辑**
- [ ] **Step 4: 重新运行后端测试**

### Task 3: 实现前端页面与交互

**Files:**
- Modify: `web/src/api/prompt-template.ts`
- Modify: `web/src/views/config/prompt-templates/index.vue`

- [ ] **Step 1: 先调整类型定义与接口调用**
- [ ] **Step 2: 再改页面展示为系统模板管理**
- [ ] **Step 3: 接入预览测试与恢复默认交互**
- [ ] **Step 4: 运行前端类型检查**

### Task 4: 完整验证与收尾

**Files:**
- Modify: `openspec/changes/update-prompt-template-management/tasks.md`

- [ ] **Step 1: 运行 OpenSpec 严格校验**
- [ ] **Step 2: 运行后端测试与前端类型检查**
- [ ] **Step 3: 更新任务状态并整理变更说明**
