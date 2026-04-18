# Restore Word Column Mapping Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 恢复 Word 专用列映射规则配置和自动预填能力，同时保持 Excel 与 AI 匹配主链不变。

**Architecture:** 后端恢复 `ColumnMappingRules` 持久化、仓储与 CRUD / effective API；前端恢复配置页并抽出共享的 Word 表头匹配 helper，供数据导入和 smart-fill 复用。自动预填仅发生在 Word 场景，仍允许用户逐表手工微调。

**Tech Stack:** ASP.NET Core Web API, EF Core, MySQL migrations, Vue 3, TypeScript, Element Plus, xUnit, node:test

---

### Task 1: 规范与失败测试

**Files:**
- Create: `openspec/changes/restore-word-column-mapping-rules-prefill/*`
- Create: `docs/superpowers/plans/2026-04-17-restore-word-column-mapping-rules.md`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ColumnMappingRuleRemovalTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Data.Tests/ColumnMappingRuleDataRemovalTests.cs`
- Modify: `web/tests/smart-fill-ai-equivalence.test.ts`

- [ ] 先把移除规则的断言改成恢复规则的断言
- [ ] 运行相关测试并确认先失败

### Task 2: 后端恢复规则链路

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Entities/ColumnMappingRule.cs`
- Create: `src/AcceptanceSpecSystem.Data/Repositories/IColumnMappingRuleRepository.cs`
- Create: `src/AcceptanceSpecSystem.Data/Repositories/ColumnMappingRuleRepository.cs`
- Create: `src/AcceptanceSpecSystem.Api/DTOs/ColumnMappingRuleDtos.cs`
- Create: `src/AcceptanceSpecSystem.Api/Controllers/ColumnMappingRulesController.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/IUnitOfWork.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/UnitOfWork.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Modify: `tests/AcceptanceSpecSystem.Data.Tests/UnitOfWorkTests.cs`

- [ ] 恢复实体、DbSet、仓储与 UnitOfWork
- [ ] 恢复控制器和 DTO
- [ ] 跑后端/数据层相关测试并确认通过

### Task 3: 前端恢复配置页与共享匹配 helper

**Files:**
- Create: `web/src/api/column-mapping-rules.ts`
- Create: `web/src/views/config/column-mapping-rules/index.vue`
- Create: `web/src/views/shared/word-column-mapping-rules.ts`
- Modify: `web/src/router/modules/config.ts`
- Modify: `shared/navigation/navigation-manifest.json`

- [ ] 恢复规则 API 与配置页
- [ ] 抽出 Word 表头规则匹配 helper
- [ ] 跑前端静态测试并确认通过

### Task 4: 接回 Word 导入与 smart-fill 自动预填

**Files:**
- Modify: `web/src/views/data-import/index.vue`
- Modify: `web/src/views/data-import/components/DataImportStepMapping.vue`
- Modify: `web/src/views/smart-fill/index.vue`

- [ ] 仅在 Word 场景加载有效规则并自动预填
- [ ] Excel 保持原有手工配置行为
- [ ] 确认手工修改不会被自动预填强制覆盖

### Task 5: 迁移与全量验证

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Migrations/20260417xxxxxx_RestoreWordColumnMappingRules.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/20260417xxxxxx_RestoreWordColumnMappingRules.Designer.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Migrations/AppDbContextModelSnapshot.cs`

- [ ] 生成恢复 `ColumnMappingRules` 的新迁移
- [ ] 运行数据库迁移
- [ ] 运行构建、测试与 OpenSpec 校验
