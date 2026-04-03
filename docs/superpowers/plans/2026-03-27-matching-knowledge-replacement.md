# Matching Knowledge Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用数据库持久化的匹配知识配置替换文本预处理、同义词和关键字旧体系，并提供新的后台配置页面。

**Architecture:** 后端新增 `MatchingKnowledgeConfig` 单例实体和统一 API，匹配运行时从数据库读取结构化知识；前端新增单页“匹配知识配置”界面，删除旧配置页面与接口接线。匹配主链路仅保留最小安全归一化，结构化归一化和冲突判断全部走 `MatchingKnowledge`。

**Tech Stack:** ASP.NET Core 8、EF Core 8、MySQL、Vue 3、TypeScript、Element Plus、xUnit

---

### Task 1: 数据模型与迁移

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Entities/MatchingKnowledgeConfig.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/IUnitOfWork.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/UnitOfWork.cs`
- Create: `src/AcceptanceSpecSystem.Data/Repositories/IMatchingKnowledgeConfigRepository.cs`
- Create: `src/AcceptanceSpecSystem.Data/Repositories/MatchingKnowledgeConfigRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/<timestamp>_ReplaceTextPreprocessingWithMatchingKnowledge.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/MatchingKnowledgeConfigRepositoryTests.cs`

- [ ] **Step 1: 写仓储测试，定义单例读取/保存行为**
- [ ] **Step 2: 运行测试并确认失败**
- [ ] **Step 3: 实现实体、仓储和 DbContext 映射**
- [ ] **Step 4: 生成并整理迁移，包含旧表删除**
- [ ] **Step 5: 运行数据层测试确认通过**

### Task 2: 默认值初始化与旧同义词迁移

**Files:**
- Create: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeBootstrapper.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.json`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeBootstrapperTests.cs`

- [ ] **Step 1: 写失败测试，定义首次初始化与保守迁移行为**
- [ ] **Step 2: 运行测试并确认失败**
- [ ] **Step 3: 实现默认知识写入与旧同义词保守迁移**
- [ ] **Step 4: 运行测试确认通过**

### Task 3: 运行时 provider 与 API

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/ConfigurationMatchingKnowledgeProvider.cs`
- Create: `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
- Create: `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Delete: `src/AcceptanceSpecSystem.Api/Controllers/TextProcessingController.cs`
- Delete: `src/AcceptanceSpecSystem.Api/Controllers/SynonymsController.cs`
- Delete: `src/AcceptanceSpecSystem.Api/Controllers/KeywordsController.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigurationMatchingKnowledgeProviderTests.cs`

- [ ] **Step 1: 写失败测试，定义 GET/PUT/reset API 与 provider 数据源切换**
- [ ] **Step 2: 运行测试并确认失败**
- [ ] **Step 3: 实现 DTO、校验、Controller 和 provider**
- [ ] **Step 4: 删除旧 Controller 与相关 API 接线**
- [ ] **Step 5: 运行 API 测试确认通过**

### Task 4: 匹配主链路去旧预处理依赖

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/TextProcessing/Interfaces/ITextPreprocessingPipeline.cs`
- Delete: `src/AcceptanceSpecSystem.Core/TextProcessing/Services/DefaultTextPreprocessingPipeline.cs`
- Delete: `src/AcceptanceSpecSystem.Core/TextProcessing/Services/SynonymService.cs`
- Delete: `src/AcceptanceSpecSystem.Core/TextProcessing/Services/KeywordService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/MatchingKnowledgeDrivenNormalizationTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingWorkflowServiceTests.cs`

- [ ] **Step 1: 写失败测试，定义仅保留最小安全归一化后的匹配行为**
- [ ] **Step 2: 运行测试并确认失败**
- [ ] **Step 3: 实现主链路清理和最小归一化**
- [ ] **Step 4: 删除旧文本预处理服务和注入**
- [ ] **Step 5: 运行核心匹配测试确认通过**

### Task 5: 前端配置页与路由替换

**Files:**
- Create: `web/src/api/matching-knowledge.ts`
- Create: `web/src/views/config/matching-knowledge/index.vue`
- Modify: `web/src/router/modules/config.ts`
- Delete: `web/src/views/config/text-processing/index.vue`
- Delete: `web/src/views/other/synonyms/index.vue`
- Delete: `web/src/views/other/keywords/index.vue`
- Modify: `web/src/router/modules/remaining.ts`
- Test: `web` build

- [ ] **Step 1: 写前端页面接口与状态草图**
- [ ] **Step 2: 实现单页整包编辑界面**
- [ ] **Step 3: 替换配置管理路由入口**
- [ ] **Step 4: 删除旧页面与旧入口**
- [ ] **Step 5: 运行 `pnpm build` 验证通过**

### Task 6: 权限种子与回归收口

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthPermissionsTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`
- Modify: `openspec/changes/replace-text-preprocessing-with-matching-knowledge/tasks.md`

- [ ] **Step 1: 写失败测试，定义新页面权限与旧权限移除行为**
- [ ] **Step 2: 运行测试并确认失败**
- [ ] **Step 3: 更新权限种子和回归测试**
- [ ] **Step 4: 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`**
- [ ] **Step 5: 勾选 OpenSpec tasks 完成项**
