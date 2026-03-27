# Round4 Remaining Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 Round4 中剩余且本轮可以安全落地的代码审查问题，并同步回归测试与文档结论。

**Architecture:** 本轮优先处理无 schema 破坏或可局部收敛的问题：守卫校验、缓存、仓储边界、日志告警、开发期提示与注释补强。涉及数据库结构重排、跨层接口语义重构、迁移脚本联动的大项继续延后，避免在脏工作区中引入高风险回归。

**Tech Stack:** ASP.NET Core 8、EF Core 8、xUnit、FluentAssertions、Vue 3、Pinia、TypeScript

---

### Task 1: 为剩余 Round4 问题补失败测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`
- Create: `tests/AcceptanceSpecSystem.Api.Tests/AuthSeedOptionsValidationTests.cs`
- Create: `tests/AcceptanceSpecSystem.Core.Tests/CoreProviderBoundaryTests.cs`

- [x] 为 `PromptTemplateProvider`、`AuthAccessService`、`AuthDataScopeService`、`StrictReuseDialog`、`user.logOut`、`MatchingApiControllerBase` 增加失败断言
- [x] 为 `AuthSeedOptions` 启动期校验增加单元测试
- [x] 为 `SemanticKernelServiceFactory` 必填模型守卫增加单元测试
- [x] 跑定向测试并确认先红（已由后续修复转绿）

### Task 2: 修复后端守卫、缓存与仓储边界

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Options/AuthSeedOptions.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthDataScopeService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/PromptTemplateRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/KeywordRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs`

- [x] `PromptTemplateProvider` 改走 `IPromptTemplateRepository + IUnitOfWork`，移除直接 `AppDbContext` 依赖
- [x] `AuthSeedOptions` 新增 `AuthSeedOptionsValidator`，`Program.cs` 中 `ValidateOnStart()` 启动期校验
- [x] `AuthDataScopeService` 引入 `IMemoryCache`，组织树快照缓存，避免每请求全量查询
- [x] `SemanticKernelServiceFactory` 新增空模型 guard（`InvalidOperationException`），移除 `!` 强制解引用
- [x] `PromptTemplateRepository.SetDefaultAsync` 新增 `BeginTransactionAsync` + `ExecuteUpdateAsync`
- [x] `KeywordRepository.AddRangeUniqueAsync` 改为数据库侧去重，不再全表拉词到内存
- [x] `AppDbContext.DecryptApiKey` 解密失败追加 `Trace.TraceWarning`，不再静默
- [x] `AuthUserSeedService` 组织路径初始化改用 `BeginTransactionAsync`，权限版本修正改用 `ExecuteUpdateAsync`
- [x] `AuthRolesController` 权限版本变更改用 `ExecuteUpdateAsync`

### Task 3: 修复前端问题

**Files:**
- Modify: `web/src/views/smart-fill/components/StrictReuseDialog.vue`
- Modify: `web/src/store/modules/user.ts`
- Modify: `web/src/utils/http/index.ts`
- Modify: `web/src/views/login/index.vue`
- Modify: `web/src/views/smart-fill/index.vue`

- [x] `StrictReuseDialog` 开发期缺失 permission props 时输出 `console.warn`（仅 `import.meta.env.DEV`）
- [x] `user.logOut(redirectPath?)` 支持回跳地址，跳转时携带 `query: { redirect: redirectPath }`
- [x] `http/index.ts` 会话失效改为 `ElMessageBox.alert` 确认弹框，调用 `logOut(currentPath)` 携带当前页
- [x] `login/index.vue` 登录成功后读取 `route.query.redirect` 并校验安全回跳
- [x] `http/index.ts` 新增 `ensureAuditHeaders`，在 `beforeRequestCallback` 前后均补齐审计头
- [x] `smart-fill/index.vue` 下载使用 `document.body.appendChild(a)` 兼容 Firefox/Safari
- [x] `smart-fill/index.vue` `onBeforeUnmount` 调用 `invalidatePendingPreview()` + `stopLlmStream()`

### Task 4: 同步 Round4 文档

**Files:**
- Modify: `docs/CodeReview_Round4.md`

- [x] 更新本轮新增认领项与仍延后的高风险项（见文档「本轮认领结论」章节）
- [x] 把验证命令与结果追加到文档

### Task 5: 完整验证

**Files:**
- Test: `tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj`
- Test: `web/package.json`

- [x] 运行本轮新增定向测试 — 通过（35 条 ReviewRegressionTests 全绿）
- [x] 运行 API 全量测试 — 通过（149 passed, 0 failed）
- [x] 运行 Core 全量测试 — 通过（61 passed, 0 failed）
- [x] 运行 Data 全量测试 — 通过（25 passed, 2 skipped, 0 failed）
- [x] 运行前端 `typecheck` — 通过（tsc --noEmit 无错误输出）

---

## 验证结果（2026-03-27）

| 测试集 | 通过 | 跳过 | 失败 |
|--------|------|------|------|
| Api.Tests | 149 | 0 | 0 |
| Core.Tests | 61 | 0 | 0 |
| Data.Tests | 25 | 2 | 0 |
| 前端 tsc typecheck | ✓ | — | — |
