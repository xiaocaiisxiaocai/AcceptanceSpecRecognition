# Matching Knowledge AI Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在匹配知识配置页为单个分类增加基于历史验规筛选的 AI 草稿生成功能，并支持审核后导入自定义扩展。

**Architecture:** 后端保留现有草稿生成入口，但把输入来源从文本/文档改为历史验规筛选条件，直接查询 `AcceptanceSpec` 并拼接结构化文本给 AI。前端保留“AI 生成候选”入口，但弹窗只提供历史验规筛选、分页预览和“全选/取消全选”开关；生成时始终按当前筛选命中的全部历史验规处理。

**Tech Stack:** ASP.NET Core 8、EF Core 8、Vue 3、TypeScript、Element Plus、Semantic Kernel、xUnit

---

### Task 1: 重写后端筛选与草稿请求模型

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/AcceptanceSpecQueryOptions.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecQueryOptionsTests.cs`

- [ ] **Step 1: 写失败测试，定义草稿生成接口只接受历史验规筛选条件，并支持导入时间范围**
- [ ] **Step 2: 运行 `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~ConfigApisTests" -c Debug` 确认失败**
- [ ] **Step 3: 实现 `GenerateMatchingKnowledgeDraftRequest` 的 `specFilter` 模型，删除 `sourceType`、`inputText`、`fileIds`**
- [ ] **Step 4: 为 `AcceptanceSpecQueryOptions` 增加 `ImportedFrom` / `ImportedTo` 并补齐约束测试**
- [ ] **Step 5: 运行目标测试确认通过**

### Task 2: 扩展历史验规筛选查询

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/AcceptanceSpecRepository.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecRepositoryQueryTests.cs`

- [ ] **Step 1: 写失败测试，覆盖导入时间范围筛选与已有客户/制程/机型/关键词筛选组合**
- [ ] **Step 2: 运行 `dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj --filter "FullyQualifiedName~AcceptanceSpecRepositoryQueryTests" -c Debug` 确认失败**
- [ ] **Step 3: 在 `SpecsController` 的列表参数中加入 `importedFrom` / `importedTo`**
- [ ] **Step 4: 在 `AcceptanceSpecRepository` 中实现 `ImportedAt` 范围过滤，并保持现有排序与分页行为**
- [ ] **Step 5: 运行目标测试确认通过**

### Task 3: 重构匹配知识草稿生成服务

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeDraftGenerationService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeDraftsController.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`

- [ ] **Step 1: 写失败测试，覆盖基于历史验规生成、空结果报错、超上限报错和不落库行为**
- [ ] **Step 2: 运行 `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~ConfigApisTests" -c Debug` 确认失败**
- [ ] **Step 3: 删除文档读取与解析分支，改为查询符合筛选条件的 `AcceptanceSpec`**
- [ ] **Step 4: 用历史验规字段拼接源文本，保留单分类生成与重复/冲突标记逻辑**
- [ ] **Step 5: 移除不再使用的文档解析依赖注册和构造参数**
- [ ] **Step 6: 运行目标测试确认通过**

### Task 4: 更新前端弹窗与筛选生成交互

**Files:**
- Modify: `web/src/views/config/matching-knowledge/index.vue`
- Modify: `web/src/api/matching-knowledge.ts`
- Modify: `web/src/api/spec.ts`
- Modify: `web/src/views/config/matching-knowledge/components/MatchingKnowledgeDraftDialog.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`

- [ ] **Step 1: 写失败测试，确认旧来源文案被移除，并新增历史验规筛选、导入时间范围与全选语义文案**
- [ ] **Step 2: 运行 `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~MatchingKnowledgeFrontendRegressionTests" -c Debug` 确认失败**
- [ ] **Step 3: 重写前端请求模型，只传 `category`、`specFilter`、`llmServiceId`**
- [ ] **Step 4: 在弹窗中实现客户、制程、机型、关键词、导入时间范围筛选与预览分页**
- [ ] **Step 5: 实现“当前筛选结果默认全选，只支持全选/取消全选”的生成交互**
- [ ] **Step 6: 保持现有草稿编辑、删除、导入到自定义扩展流程**
- [ ] **Step 7: 运行目标测试确认通过**

### Task 5: 整体验证与规范收口

**Files:**
- Modify: `openspec/changes/add-ai-matching-knowledge-draft-generation/tasks.md`

- [ ] **Step 1: 运行 `dotnet test .\\AcceptanceSpecSystem.sln -c Debug`**
- [ ] **Step 2: 运行 `pnpm build`**
- [ ] **Step 3: 根据实际完成情况勾选 OpenSpec tasks**
- [ ] **Step 4: 记录无法完成的验证或残留风险**
