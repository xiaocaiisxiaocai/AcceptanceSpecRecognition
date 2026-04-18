# AI Equivalence Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复当前分支 review 暴露出的执行门禁、Prompt 契约、前端交互、数据库隔离/迁移验证、运行时旧规则残留与文档漂移问题。

**Architecture:** 保持现有“Embedding 召回 -> 服务端重排 -> AI 判别/裁决 -> 执行填充”的主链路不变，但把执行授权收口到服务端可验证状态，去掉误导性的旧兼容输出和运行时死模板；同时补齐隔离式 MySQL 迁移烟测与前端真实交互覆盖。

**Tech Stack:** .NET 8、ASP.NET Core、EF Core + Pomelo MySQL、Vue 3 + Element Plus、xUnit、Node test

---

### Task 1: 后端执行门禁与预览/执行一致性

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/LlmMatchingAssistFillTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingPreviewLlmAssistTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] 1.1 先补失败测试，覆盖“LLM 复核通过后服务端可执行”“执行阶段禁止改 scope/config 旁路”“旧 LLM 复核响应字段不再暴露”。
- [ ] 1.2 运行对应 API 测试，确认先红。
- [ ] 1.3 在服务端落地可验证的复核/执行上下文，执行时绑定预览快照而不是信任客户端重算参数。
- [ ] 1.4 去掉智能填充响应模型里的旧 `LlmScore/LlmReason/LlmCommentary/IsLlmReviewed` 兼容字段。
- [ ] 1.5 回归执行相关 API 测试并修正断言。

### Task 2: Prompt 模板契约与管理接口

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateValidationService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Entities/PromptTemplate.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/PromptTemplateApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/PromptTemplateValidationServiceTests.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/LlmReviewPromptTests.cs`

- [ ] 2.1 先补失败测试，覆盖“读接口不删库”“导入复核共享 matching-review”“空格占位符可预览且可运行时替换”“非系统模板入口拒绝访问”。
- [ ] 2.2 运行 Prompt 相关测试，确认先红。
- [ ] 2.3 收敛系统模板目录，只保留真实运行时会读取的模板；修正占位符渲染/预览样例和控制器读写规则。
- [ ] 2.4 回归 Prompt API/Core 测试并修正 spec 对齐项。

### Task 3: 运行时 AI-only 清理与候选去重/Embedding 退化

**Files:**
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingKnowledgeModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/MatchEvidenceBuilder.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/EntityAliasNormalizer.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/EntitySurfaceExtractor.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/NumericConstraintParser.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/EmbeddingDegradationTests.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/SemanticKernelMatchingServiceTieBreakTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/EvidenceDrivenMatchingApiTests.cs`

- [ ] 3.1 先补失败测试，覆盖“候选去重按项目+规格生效”“候选 embedding 失败直接报错”“运行时不再依赖硬编码知识/别名/单位推断”。
- [ ] 3.2 运行 core/API 匹配测试，确认先红。
- [ ] 3.3 删除或停用旧硬编码知识入口，把召回后判别彻底收束到当前 AI 主链路；同步修正去重键和 API 侧 embedding 异常传播。
- [ ] 3.4 回归匹配相关 core/API 测试。

### Task 4: 数据连接隔离与真实迁移烟测

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContextFactory.cs`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.json`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.Production.json`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`
- Modify: `tests/AcceptanceSpecSystem.Data.Tests/TestBase.cs`
- Add/Modify: `tests/AcceptanceSpecSystem.Data.Tests/DatabaseConnectionConfigurationTests.cs`
- Add/Modify: `tests/AcceptanceSpecSystem.Data.Tests/*Migration*Tests.cs`

- [ ] 4.1 先补失败测试，覆盖“设计时连接串禁止回退到硬编码默认库”“迁移链可在隔离 MySQL 临时库上跑通”。
- [ ] 4.2 运行对应数据层测试，确认先红。
- [ ] 4.3 收紧设计时连接串解析规则，补隔离式 MySQL 迁移烟测辅助代码，不碰业务库。
- [ ] 4.4 回归数据层测试；如需真实 MySQL 临时库验证，使用独立数据库名并在测试结束清理。

### Task 5: 前端交互、文案与文档漂移收口

**Files:**
- Modify: `web/src/views/smart-fill/components/BatchPreviewTabs.vue`
- Modify: `web/src/views/smart-fill/components/MatchConfig.vue`
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/index.vue`
- Modify: `web/tests/smart-fill-ai-equivalence.test.ts`
- Modify: `docs/design-overview.md`
- Modify: `docs/matching-evaluation-and-rerank-plan.md`
- Modify: `openspec/specs/api/spec.md`
- Modify: `openspec/specs/matching-engine/spec.md`

- [ ] 5.1 先补失败测试，覆盖“批量 Tab 可切换”“llm-stream 使用类型化请求”“阈值文案不再暗示默认选中旧逻辑”。
- [ ] 5.2 运行前端源码测试，确认先红。
- [ ] 5.3 修复前端交互和文案，补类型化 API 请求。
- [ ] 5.4 清理设计文档/spec 中的旧链路描述，确保与当前实现一致。
- [ ] 5.5 回归前端测试与文档一致性检查。

### Final Verification

**Commands:**
- `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj`
- `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj`
- `dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj`
- `npm --prefix web test`
- `dotnet build AcceptanceSpecSystem.sln`

- [ ] F1 执行分任务定向测试并修复剩余失败。
- [ ] F2 执行全量 `dotnet build` 与核心测试命令。
- [ ] F3 若启用了 MySQL 临时库烟测，确认只使用独立测试库并已清理。
