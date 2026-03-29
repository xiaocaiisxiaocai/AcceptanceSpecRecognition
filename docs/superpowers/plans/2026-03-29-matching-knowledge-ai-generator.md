# Matching Knowledge AI Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在匹配知识配置页为单个分类增加 AI 草稿生成功能，并支持审核后导入自定义扩展。

**Architecture:** 后端在 `MatchingKnowledgeController` 下增加草稿生成接口，复用现有文档读取、AI 服务和 Prompt 模板体系，返回结构化候选草稿。前端在匹配知识页新增“AI 生成候选”入口和审核弹窗，用户审核后才把数据并入自定义扩展。

**Tech Stack:** ASP.NET Core 8、EF Core 8、Vue 3、TypeScript、Element Plus、Semantic Kernel、xUnit

---

### Task 1: 定义草稿请求/响应模型

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`

- [ ] **Step 1: 写失败测试，定义单分类草稿生成接口的请求与响应结构**
- [ ] **Step 2: 运行目标测试并确认失败**
- [ ] **Step 3: 实现 DTO，覆盖分类、来源、候选项、状态和导入模型**
- [ ] **Step 4: 运行目标测试确认通过**
- [ ] **Step 5: 提交一次小步提交**

### Task 2: 实现后端草稿生成服务

**Files:**
- Create: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeDraftGenerationService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeDraftGenerationTests.cs`

- [ ] **Step 1: 写失败测试，定义文本输入、已上传文档输入和临时上传输入行为**
- [ ] **Step 2: 运行目标测试并确认失败**
- [ ] **Step 3: 实现最小后端服务与接口，仅支持单分类返回结构化草稿**
- [ ] **Step 4: 加入重复/冲突标记逻辑**
- [ ] **Step 5: 运行目标测试确认通过**
- [ ] **Step 6: 提交一次小步提交**

### Task 3: 前端弹窗与导入交互

**Files:**
- Modify: `web/src/views/config/matching-knowledge/index.vue`
- Modify: `web/src/api/matching-knowledge.ts`
- Create: `web/src/views/config/matching-knowledge/components/MatchingKnowledgeDraftDialog.vue`
- Reuse: `web/src/views/data-import/components/FileUpload.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`

- [ ] **Step 1: 写失败测试，定义各分类 `AI 生成候选` 入口与弹窗关键文案**
- [ ] **Step 2: 运行目标测试并确认失败**
- [ ] **Step 3: 实现弹窗组件和来源切换**
- [ ] **Step 4: 实现候选编辑、删除、勾选与导入回填**
- [ ] **Step 5: 运行目标测试确认通过**
- [ ] **Step 6: 提交一次小步提交**

### Task 4: 回归验证

**Files:**
- Modify: `openspec/changes/add-ai-matching-knowledge-draft-generation/tasks.md`

- [ ] **Step 1: 运行 `dotnet test .\\AcceptanceSpecSystem.sln -c Debug`**
- [ ] **Step 2: 运行 `pnpm build`**
- [ ] **Step 3: 根据实际完成情况勾选 OpenSpec tasks**
- [ ] **Step 4: 提交最终收口提交**
