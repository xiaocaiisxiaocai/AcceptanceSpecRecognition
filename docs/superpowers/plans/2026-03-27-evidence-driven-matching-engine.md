# 证据驱动匹配引擎重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将现有智能匹配重构为统一的多阶段证据驱动引擎，移除 `SingleStage`，让 Embedding 仅负责召回，关键字段冲突优先于文本语义参与裁决。

**Architecture:** 后端先补齐证据模型、结构化解析与门禁决策，再把 `SemanticKernelMatchingService` 重构为“召回 -> 证据 -> 冲突 -> 重排 -> 高歧义复核”的固定链路，随后同步更新 API DTO 与前端展示。规则知识不直接写死在打分逻辑里，而是预留配置化入口；首版以前置默认配置对象落地，后续可接入数据库或模板配置。 

**Tech Stack:** .NET 8、xUnit、FluentAssertions、ASP.NET Core、Vue 3、TypeScript、Element Plus

---

### Task 1: 证据模型与基础配置骨架

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchEvidenceModels.cs`
- Create: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingKnowledgeModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenMatchingModelsTests.cs`

- [ ] **Step 1: 写失败测试，锁定新模型的最小行为**

```csharp
[Fact]
public void MatchResult_WhenDecisionIsManualReview_ShouldNotBeHighConfidence()
{
    var result = new MatchResult
    {
        Score = 0.99,
        Decision = MatchDecision.ManualReview
    };

    result.IsHighConfidence.Should().BeFalse();
}
```

- [ ] **Step 2: 运行测试，确认当前模型缺字段或行为失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter EvidenceDrivenMatchingModelsTests -v minimal`
Expected: FAIL，提示 `Decision`、证据类型或新状态字段不存在。

- [ ] **Step 3: 最小实现证据与决策模型**

```csharp
public enum MatchDecision { AutoApply, ManualReview, Reject }
public enum EvidenceRelation { Exact, Compatible, Overlap, Conflict, AliasSame, ParentChild, PossiblyRelated }
public sealed class MatchEvidence { ... }
```

- [ ] **Step 4: 运行测试，确认模型行为通过**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter EvidenceDrivenMatchingModelsTests -v minimal`
Expected: PASS

- [ ] **Step 5: 提交当前小步**

```bash
git add src/AcceptanceSpecSystem.Core/Matching/Models/MatchEvidenceModels.cs src/AcceptanceSpecSystem.Core/Matching/Models/MatchingKnowledgeModels.cs src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenMatchingModelsTests.cs
git commit -m "feat: 添加证据驱动匹配模型骨架"
```

### Task 2: 关键字段解析器

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Matching/Interfaces/IMatchEvidenceBuilder.cs`
- Create: `src/AcceptanceSpecSystem.Core/Matching/Services/MatchEvidenceBuilder.cs`
- Create: `src/AcceptanceSpecSystem.Core/Matching/Services/NumericConstraintParser.cs`
- Create: `src/AcceptanceSpecSystem.Core/Matching/Services/EntityAliasNormalizer.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingKnowledgeModels.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/MatchEvidenceBuilderTests.cs`

- [ ] **Step 1: 写失败测试，覆盖数值相容、数值冲突、品牌别名与型号冲突**

```csharp
[Fact]
public void Build_WhenLessThanMatchesPointValue_ShouldProduceCompatibleNumericEvidence() { ... }

[Fact]
public void Build_WhenBrandAliasMatches_ShouldProduceAliasSameEntityEvidence() { ... }
```

- [ ] **Step 2: 运行测试，确认解析器尚未实现**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter MatchEvidenceBuilderTests -v minimal`
Expected: FAIL，提示解析器或证据构建器不存在。

- [ ] **Step 3: 最小实现解析与标准化**

```csharp
public sealed class NumericConstraintParser
{
    public IReadOnlyList<NumericConstraintEvidence> Parse(string text) { ... }
}

public sealed class EntityAliasNormalizer
{
    public EntityNormalizationResult Normalize(string raw, MatchingKnowledge knowledge) { ... }
}
```

- [ ] **Step 4: 运行测试，确认解析结果正确**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter MatchEvidenceBuilderTests -v minimal`
Expected: PASS

- [ ] **Step 5: 提交当前小步**

```bash
git add src/AcceptanceSpecSystem.Core/Matching/Interfaces/IMatchEvidenceBuilder.cs src/AcceptanceSpecSystem.Core/Matching/Services/MatchEvidenceBuilder.cs src/AcceptanceSpecSystem.Core/Matching/Services/NumericConstraintParser.cs src/AcceptanceSpecSystem.Core/Matching/Services/EntityAliasNormalizer.cs src/AcceptanceSpecSystem.Core/Matching/Models/MatchingKnowledgeModels.cs tests/AcceptanceSpecSystem.Core.Tests/MatchEvidenceBuilderTests.cs
git commit -m "feat: 添加关键字段证据解析器"
```

### Task 3: 重构匹配主链路

**Files:**
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/SemanticKernelMatchingServiceTieBreakTests.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs`

- [ ] **Step 1: 写失败测试，覆盖硬冲突拒绝、相容约束放行与高歧义判定**

```csharp
[Fact]
public async Task BatchMatch_WhenVoltageConflicts_ShouldRejectAutoApply() { ... }

[Fact]
public async Task BatchMatch_WhenConstraintCompatible_ShouldKeepCandidateEligible() { ... }
```

- [ ] **Step 2: 运行测试，确认现有链路仍按单阶段/旧多阶段工作**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter "FullyQualifiedName~EvidenceDrivenSemanticMatchingTests|FullyQualifiedName~SemanticKernelMatchingServiceTieBreakTests" -v minimal`
Expected: FAIL，仍返回旧 `MatchingStrategy` 或未输出证据/决策字段。

- [ ] **Step 3: 最小实现固定多阶段链路**

```csharp
// 召回 -> 证据 -> 硬冲突 -> 重排 -> 歧义判定
var recalled = RecallTopK(...);
var evaluated = recalled.Select(candidate => BuildEvidence(...));
var ordered = RankCandidates(evaluated, config);
```

- [ ] **Step 4: 运行核心测试，确认链路通过**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter "FullyQualifiedName~EvidenceDrivenSemanticMatchingTests|FullyQualifiedName~SemanticKernelMatchingServiceTieBreakTests" -v minimal`
Expected: PASS

- [ ] **Step 5: 提交当前小步**

```bash
git add src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs tests/AcceptanceSpecSystem.Core.Tests/SemanticKernelMatchingServiceTieBreakTests.cs tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs
git commit -m "feat: 重构证据驱动匹配主链路"
```

### Task 4: API DTO 与工作流接入

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/LlmMatchingModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingPreviewScoreDetailsTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingPreviewLlmAssistTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/EvidenceDrivenMatchingApiTests.cs`

- [ ] **Step 1: 写失败测试，锁定预览响应的新字段与复核失败回退行为**

```csharp
[Fact]
public async Task Preview_ShouldExposeDecisionEvidenceAndConflictState() { ... }

[Fact]
public async Task LlmReview_WhenTimedOut_ShouldMarkManualReview() { ... }
```

- [ ] **Step 2: 运行测试，确认 DTO 与工作流尚未输出新结构**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~EvidenceDrivenMatchingApiTests|FullyQualifiedName~MatchingPreviewScoreDetailsTests|FullyQualifiedName~MatchingPreviewLlmAssistTests" -v minimal`
Expected: FAIL，JSON 缺少 `decision`、`evidenceSummary`、`conflictState` 或超时回退字段。

- [ ] **Step 3: 最小实现 DTO 映射与工作流门禁**

```csharp
bestMatchDto.Decision = result.Decision;
bestMatchDto.HasHardConflict = result.HasHardConflict;
bestMatchDto.EvidenceSummary = MapEvidence(...);
```

- [ ] **Step 4: 运行测试，确认 API 行为通过**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~EvidenceDrivenMatchingApiTests|FullyQualifiedName~MatchingPreviewScoreDetailsTests|FullyQualifiedName~MatchingPreviewLlmAssistTests" -v minimal`
Expected: PASS

- [ ] **Step 5: 提交当前小步**

```bash
git add src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs src/AcceptanceSpecSystem.Core/Matching/Models/LlmMatchingModels.cs src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs tests/AcceptanceSpecSystem.Api.Tests/MatchingPreviewScoreDetailsTests.cs tests/AcceptanceSpecSystem.Api.Tests/MatchingPreviewLlmAssistTests.cs tests/AcceptanceSpecSystem.Api.Tests/EvidenceDrivenMatchingApiTests.cs
git commit -m "feat: 接入证据驱动匹配 API 工作流"
```

### Task 5: 前端配置与结果展示

**Files:**
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/components/MatchConfig.vue`
- Modify: `web/src/views/smart-fill/components/MatchPreviewTable.vue`
- Modify: `web/src/views/smart-fill/components/ScoreDetailDialog.vue`
- Modify: `web/src/views/smart-fill/index.vue`
- Test: `web` 构建验证

- [ ] **Step 1: 写失败验证点，明确移除策略切换与新增状态展示**

```text
期望页面不再出现“单阶段/多阶段”切换；
期望详情弹窗出现决策状态、冲突摘要、复核状态。
```

- [ ] **Step 2: 运行构建前检查，确认当前类型仍依赖 MatchingStrategy**

Run: `pnpm build`
Expected: FAIL 或当前界面仍保留旧策略枚举/文案。

- [ ] **Step 3: 最小实现前端类型与界面调整**

```ts
export interface MatchResult {
  decision: "autoApply" | "manualReview" | "reject";
  hasHardConflict?: boolean;
  evidenceSummary?: string[];
}
```

- [ ] **Step 4: 运行构建，确认前端通过**

Run: `pnpm build`
Expected: PASS

- [ ] **Step 5: 提交当前小步**

```bash
git add web/src/api/matching.ts web/src/views/smart-fill/components/MatchConfig.vue web/src/views/smart-fill/components/MatchPreviewTable.vue web/src/views/smart-fill/components/ScoreDetailDialog.vue web/src/views/smart-fill/index.vue
git commit -m "feat: 更新智能填充证据驱动展示"
```

### Task 6: 总体验证与文档回填

**Files:**
- Modify: `openspec/changes/refactor-evidence-driven-matching-engine/tasks.md`
- Modify: `docs/代码问题分析报告.md`
- Verify: `dotnet test AcceptanceSpecSystem.sln -c Debug`
- Verify: `pnpm build`

- [ ] **Step 1: 运行核心后端测试**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug`
Expected: 相关 Core / Api 测试通过；若有历史失败，先记录后再区分本次回归。

- [ ] **Step 2: 运行前端构建**

Run: `pnpm build`
Expected: PASS

- [ ] **Step 3: 更新任务清单与文档**

```markdown
- [x] 已完成证据驱动匹配引擎第一版重构
- [x] 已移除 SingleStage 入口
```

- [ ] **Step 4: 最终提交**

```bash
git add openspec/changes/refactor-evidence-driven-matching-engine/tasks.md docs/代码问题分析报告.md
git commit -m "docs: 更新证据驱动匹配引擎实现状态"
```
