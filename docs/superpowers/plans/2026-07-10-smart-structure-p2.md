# 智能结构识别 P2 收口 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐仅规格安全门禁，并完成列规则语义、列语义召回和测试守卫的 P2 收口。

**Architecture:** 数据库列规则在 Application 映射为 Core 结构化规则，由单一匹配器供运行时和离线分析复用。列语义召回保留候选建议边界，通过统一表头策略、模板目录和独立超时降低漂移。仅规格首次识别必须经过显式确认后才能生成导入配置。

**Tech Stack:** .NET 8、xUnit、ASP.NET Core、Vue 3、TypeScript、Node Test、Vitest、OpenSpec。

---

### Task 1: OpenSpec 回归任务

**Files:**
- Modify: `openspec/changes/add-specification-only-import-project-backfill/design.md`
- Modify: `openspec/changes/add-specification-only-import-project-backfill/tasks.md`
- Modify: `openspec/changes/migrate-column-mapping-keywords-to-db/design.md`
- Modify: `openspec/changes/migrate-column-mapping-keywords-to-db/tasks.md`
- Modify: `openspec/changes/add-smart-structure-column-semantic-recall/design.md`
- Modify: `openspec/changes/add-smart-structure-column-semantic-recall/tasks.md`

- [ ] 追加本轮门禁、MatchMode 和召回维护性任务。
- [ ] 运行三个 `openspec validate <change-id> --strict`。
- [ ] 提交设计与计划文档。

### Task 2: 仅规格显式确认门禁

**Files:**
- Modify: `web/src/views/data-import/dataImport.smartRecognition.ts`
- Modify: `web/src/views/data-import/dataImport.smartRecognition.test.ts`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeApiTests.cs`

- [ ] 写前端失败测试：`NeedConfirm + isSpecificationOnly` 不得默认入选，确认后的 `AutoApply` 可以入选。
- [ ] 写 API 失败测试：存在未映射且有样本数据的短列时不得自动标记仅规格。
- [ ] 分别运行测试并确认按预期失败。
- [ ] 实现最小门禁并运行定向测试至通过。
- [ ] 提交 `fix: 补齐仅规格导入确认门禁`。

### Task 3: P2-A 统一列规则运行时语义

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/ColumnHeaderMappingRule.cs`
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/ColumnHeaderRuleMatcher.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/IDocumentIntelligenceService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/DocumentIntelligenceService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/IRuleBasedMappingStrategy.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/RuleBasedMappingStrategy.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartStructureHeaderGapAnalyzer.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/RuleBasedMappingStrategyTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartStructureHeaderGapAnalyzerTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeApiTests.cs`

- [ ] 写 Equals、Contains、Regex 及分析器一致性的失败测试。
- [ ] 运行定向测试并确认失败。
- [ ] 增加结构化规则和共享匹配器，映射数据库规则。
- [ ] 更新测试替身签名并运行 Core/API 定向测试。
- [ ] 提交 `fix: 对齐列映射规则运行时语义`。

### Task 4: P2-B 收敛列语义召回

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/AcceptanceResultHeaderPolicy.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/RuleBasedMappingStrategy.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationOptions.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/PromptTemplateModel.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Entities/PromptTemplate.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.RerankStructurePrompts.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.Prompts.cs`
- Modify: Core/Data/API PromptTemplateScene 映射文件
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.json`
- Test: Core Prompt/规则测试与 API 语义召回测试

- [ ] 写方法/结果信号、同列去重、模板场景和独立超时失败测试。
- [ ] 运行定向测试并确认失败。
- [ ] 实现统一策略、去重、模板场景和超时。
- [ ] 为所有 LLM 解析入口显式释放 `JsonDocument`。
- [ ] 运行 Prompt、Core、API 定向测试。
- [ ] 提交 `fix: 收敛列语义召回策略与配置`。

### Task 5: P2-C 加固静态测试

**Files:**
- Modify: `web/tests/column-mapping-rules-default.test.ts`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryFrontendRegressionTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Core.Tests/RuleBasedMappingStrategyTests.cs`

- [ ] 先构造当前弱断言能误通过的失败用例或局部片段测试。
- [ ] 将列宽断言限制到目标列片段。
- [ ] 将执行历史断言改回真实回放组件。
- [ ] 重命名系统元数据测试。
- [ ] 运行 Node 与 API/Core 定向测试。
- [ ] 提交 `test: 加固智能结构P2回归守卫`。

### Task 6: 全量验证与集成

- [ ] 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`。
- [ ] 运行 `pnpm --dir web test`。
- [ ] 运行 `pnpm --dir web typecheck`。
- [ ] 运行 `git diff --check` 并确认工作树状态。
- [ ] 将隔离分支提交合回 `feat/smart-recognition-simplification`，不推送远端。
