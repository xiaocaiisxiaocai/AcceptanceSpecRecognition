# 同义表述靠 AI 自动匹配 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让"意思相同、措辞不同"的规格更多靠 AI 自动匹配——智能填充默认开语义优先(召回兜底)+ 新增"高 Embedding 自动通过"(突破 LLM uncertain 瓶颈)+ 预览可见 AI 依据。

**Architecture:** 新增匹配配置 `EmbeddingSemanticAutoApplyThreshold`(后端默认 0=关闭,零回归);`DetermineDecision` 增一分支:Emb≥阈值且无硬冲突/型号冲突/未识别警告、不歧义、LLM≠different 时自动通过(即使 uncertain)。前端智能填充默认开启语义优先并把该阈值设 0.90,并在预览展示依据。

**Tech Stack:** C#/.NET 8、xUnit+FluentAssertions;Vue 3 + Element Plus。

设计依据:`docs/superpowers/specs/2026-06-15-ai-semantic-auto-match-design.md`。实测基线:客户30/制程1,heavy.docx=fileId 369。

---

## File Structure
- `src/.../Core/Matching/Models/MatchingModels.cs` — `MatchingConfig` 增字段(默认 0)。
- `src/.../Core/Matching/Services/SemanticKernelMatchingService.cs` — `DetermineDecision` 增"高 Emb 自动通过"分支。
- `src/.../Api/DTOs/MatchingDtos.cs` — `MatchConfigDto` 增字段。
- `src/.../Api/Services/MatchingConfigResolver.cs` — DTO→Config 映射 + 裁剪。
- `web/src/api/matching.ts` — 类型 + 默认(语义优先 true、阈值 0.90)。
- `web/src/views/smart-fill/components/MatchConfig.vue` — 暴露阈值控件。
- `web/src/views/smart-fill/components/MatchPreviewBestMatchCell.vue` — "AI语义命中"依据标签 + Emb%。
- `tests/.../Core.Tests/EvidenceDrivenSemanticMatchingTests.cs` — 新增 5 个决策用例。

---

### Task 1: 后端"高 Embedding 自动通过"决策 + 配置字段(TDD)

**Files:**
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs`

- [ ] **Step 1: 写失败测试(5 例)**

在 `EvidenceDrivenSemanticMatchingTests` 类中、"LLM 语义优先模式测试"区段(约 `:3255`)之后追加。复用现有 stub:`FixedSourceEmbeddingService(combinedText, sourceEmb, defaultCandidateEmbedding: candEmb)`(其 `ComputeSimilarity` 为点积,单元素向量下 = 两值相乘)与 `FixedLlmEquivalenceAdjudicationService(result)`。

```csharp
    // ── 高 Embedding 语义自动通过测试 ──────────────────────────────
    private static SemanticKernelMatchingService BuildEmbAutoApplyService(
        MatchSource source, double srcEmb, LlmEquivalenceVerdict verdict)
    {
        var equ = new FixedLlmEquivalenceAdjudicationService(new LlmEquivalenceAdjudicationResult
        {
            Verdict = verdict,
            ReasonType = verdict == LlmEquivalenceVerdict.Different
                ? LlmEquivalenceReasonType.SemanticDifference
                : LlmEquivalenceReasonType.Uncertain,
            Confidence = 0,
            Reason = "测试裁决"
        });
        return new SemanticKernelMatchingService(
            new FixedSourceEmbeddingService(source.CombinedText, [(float)srcEmb], defaultCandidateEmbedding: [1f]),
            NullLogger<SemanticKernelMatchingService>.Instance,
            llmEquivalenceAdjudicationService: equ);
    }

    private static MatchingConfig EmbAutoApplyConfig(double threshold) => new()
    {
        MinScoreThreshold = 0,
        RecallTopK = 1,
        HighConfidenceThreshold = 0.95,
        AmbiguityMargin = 0.01,
        EnableDeterministicAutoApply = false,
        EnableLlmEquivalenceAdjudication = true,
        EnableLlmSemanticPriority = false,
        EmbeddingSemanticAutoApplyThreshold = threshold
    };

    [Fact]
    public async Task EmbAutoApply_WhenHighEmbeddingAndUncertain_ShouldAutoApply()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7001, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].MatchedSpecId.Should().Be(7001);
        r.Results[0].Decision.Should().Be(MatchDecision.AutoApply, "高 Emb + 无冲突 + LLM uncertain + 阈值0.90 → 自动通过");
    }

    [Fact]
    public async Task EmbAutoApply_WhenThresholdZero_ShouldStayManual()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7001, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0)); // 默认关闭
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "阈值0(默认)→ 行为不变,uncertain 仍转人工");
    }

    [Fact]
    public async Task EmbAutoApply_WhenHardNumericConflict_ShouldStayManual()
    {
        var source = new MatchSource { Project = "安装", Specification = "电压 ≥100V" };
        var cand = new MatchCandidate { SpecId = 7002, Project = "安装", Specification = "电压 ≥220V", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "硬冲突(数值)不被高 Emb 自动通过覆盖");
    }

    [Fact]
    public async Task EmbAutoApply_WhenLlmDifferent_ShouldStayManual()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7003, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.93, LlmEquivalenceVerdict.Different);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "LLM 明确判 Different 不被覆盖");
    }

    [Fact]
    public async Task EmbAutoApply_WhenEmbeddingBelowThreshold_ShouldStayManual()
    {
        var source = new MatchSource { Project = "下料", Specification = "机械手臂运行不应产生碎屑" };
        var cand = new MatchCandidate { SpecId = 7004, Project = "下料", Specification = "机械手臂各机构不得摩擦产生磨屑", Embedding = [1f] };
        var service = BuildEmbAutoApplyService(source, 0.80, LlmEquivalenceVerdict.Uncertain);
        var r = await service.BatchMatchAsync([source], [cand], EmbAutoApplyConfig(0.90));
        r.Results[0].Decision.Should().Be(MatchDecision.ManualReview, "Emb 0.80 < 阈值0.90 → 不自动通过");
    }
```

- [ ] **Step 2: 跑测试确认红**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~EmbAutoApply"`
Expected: 编译失败 / FAIL —— `MatchingConfig` 无 `EmbeddingSemanticAutoApplyThreshold`,且决策分支未实现。

- [ ] **Step 3: 加配置字段**

在 `MatchingModels.cs` 的 `MatchingConfig` 类中、`LlmSemanticRecallThreshold` 属性之后追加:

```csharp
    /// <summary>
    /// 高 Embedding 语义自动通过阈值。取值 (0,1] 时启用：候选 Embedding 分 ≥ 此值，
    /// 且无硬冲突/型号料号冲突/未识别警告、不歧义、且 LLM 未判 Different 时，
    /// 即使 LLM 判 uncertain 也自动通过（把强 Embedding 作为足够的语义证据）。
    /// 默认 0（关闭，行为零变化）。智能填充前端默认 0.90。
    /// </summary>
    public double EmbeddingSemanticAutoApplyThreshold { get; set; }
```

- [ ] **Step 4: 加决策分支**

在 `SemanticKernelMatchingService.DetermineDecision` 中,定位"标准模式：硬冲突绝对门禁"的
`if (HasHardConflict(candidate.Issues)) return MatchDecision.ManualReview;` 之后、
`if (candidate.LlmEquivalence?.Verdict is ... Different or ... Uncertain) return MatchDecision.ManualReview;` 之前,插入:

```csharp
        // 高 Embedding 自动通过：强语义相似 + 无结构化冲突 + 不歧义 + LLM 未判不同 → 自动通过（即使 uncertain）。
        // 硬冲突已在上方拦截；此处再排除型号/料号冲突与未识别(单位/品牌/格式)警告，作为精度闸门。
        if (config.EmbeddingSemanticAutoApplyThreshold > 0 &&
            config.EmbeddingSemanticAutoApplyThreshold <= 1 &&
            candidate.EmbeddingScore >= config.EmbeddingSemanticAutoApplyThreshold - ScoreTieEpsilon &&
            !isAmbiguous &&
            !HasIdentifierConflict(candidate.Issues) &&
            !HasAutoApplyBlockingWarning(candidate.Issues) &&
            candidate.LlmEquivalence?.Verdict != LlmEquivalenceVerdict.Different)
        {
            candidate.SelectionSummary = AppendReason(
                candidate.SelectionSummary,
                $"高 Embedding 语义相似（{candidate.EmbeddingScore:P0}）且无结构化冲突，LLM 未确认，凭语义相似度自动通过，建议优先复查");
            return MatchDecision.AutoApply;
        }
```

(`ScoreTieEpsilon`、`HasIdentifierConflict`、`HasAutoApplyBlockingWarning`、`AppendReason` 均为该类已有成员。)

- [ ] **Step 5: 跑测试确认绿**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~EmbAutoApply"`
Expected: 5 PASS。

---

### Task 2: DTO + 配置解析透传

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingConfigResolver.cs`

- [ ] **Step 1: DTO 增字段**

在 `MatchConfigDto`(`MatchingDtos.cs`)的 `LlmSemanticRecallThreshold` 属性之后追加:

```csharp
    /// <summary>
    /// 高 Embedding 语义自动通过阈值（0~1，默认 0=关闭）。
    /// </summary>
    public double EmbeddingSemanticAutoApplyThreshold { get; set; }
```

- [ ] **Step 2: 解析器映射 + 裁剪**

在 `MatchingConfigResolver.ResolveAsync` 返回的 `new MatchingConfig { ... }` 里,把
`LlmSemanticRecallThreshold = Math.Clamp(... 0.1, 0.9)` 这一行末尾补逗号并追加:

```csharp
            EmbeddingSemanticAutoApplyThreshold = Math.Clamp(
                dto?.EmbeddingSemanticAutoApplyThreshold ?? fallbackConfig.EmbeddingSemanticAutoApplyThreshold, 0, 1)
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Debug`
Expected: 成功(0 错误)。注意:若 API 在后台运行会锁 exe,先停掉后台 API 任务再编译。

---

### Task 3: 前端默认开启 + 阈值控件 + 可见性

**Files:**
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/components/MatchConfig.vue`
- Modify: `web/src/views/smart-fill/components/MatchPreviewBestMatchCell.vue`

- [ ] **Step 1: 类型 + 默认值**

`web/src/api/matching.ts`:在 `MatchConfig` 接口的 `llmSemanticRecallThreshold?: number;` 之后加:

```ts
  /** 高 Embedding 语义自动通过阈值（0~1，0=关闭） */
  embeddingSemanticAutoApplyThreshold?: number;
```

并把 `defaultMatchConfig` 中 `enableLlmSemanticPriority: false` 改为 `true`,`llmSemanticRecallThreshold: 0.5` 这一行之后加 `embeddingSemanticAutoApplyThreshold: 0.9`。

- [ ] **Step 2: 配置面板控件**

`MatchConfig.vue`:在脚本顶部传出字段的数组(含 `"enableLlmSemanticPriority"`、`"llmSemanticRecallThreshold"`)里追加 `"embeddingSemanticAutoApplyThreshold"`。在模板中「LLM 语义优先」`el-form-item`(约 `:612`)所在 `el-row` 内,紧随召回阈值控件之后追加:

```html
          <el-col :span="12">
            <el-form-item label="高Emb自动通过">
              <el-input-number
                v-model="config.embeddingSemanticAutoApplyThreshold"
                :min="0"
                :max="1"
                :step="0.01"
                :precision="2"
                controls-position="right"
              />
              <div class="form-tip">
                候选 Embedding≥此值且无硬冲突时直接自动通过（即使 AI 不确定）；0=关闭。越高越严。
              </div>
            </el-form-item>
          </el-col>
```

(`.form-tip` 类该文件已有同类提示用法;若类名不同,复用同文件现有提示元素的类。)

- [ ] **Step 3: 预览"AI语义命中"依据标签**

`MatchPreviewBestMatchCell.vue`:`<script setup>` 内加计算属性:

```ts
const basisLabel = computed(() => {
  const m = props.item.bestMatch;
  if (!m) return "";
  if (m.selectionMode === "exactShortcut") return "精确直达";
  if (m.decision === "autoApply") return "AI语义命中";
  return "需人工确认";
});
const basisTagType = computed(() =>
  basisLabel.value === "精确直达"
    ? "success"
    : basisLabel.value === "AI语义命中"
      ? "primary"
      : "warning"
);
```

模板中 `match-meta` 这个 `div` 内,首个 `el-tag`(召回)之前插入:

```html
        <el-tag size="small" :type="basisTagType" effect="dark">
          {{ basisLabel }}
        </el-tag>
        <el-tag size="small" type="info" effect="plain">
          Emb {{ formatPreviewScore(item.bestMatch.embeddingScore) }}
        </el-tag>
```

(`formatPreviewScore` 已在该文件 import。)

- [ ] **Step 4: 前端校验**

Run: `cd web && pnpm typecheck`
Expected: 通过(0 类型错误)。

---

### Task 4: 回归 + 真实验证 + 提交

**Files:** 无新文件。

- [ ] **Step 1: 后端回归**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~EvidenceDrivenSemantic|FullyQualifiedName~ReviewRegression|FullyQualifiedName~PromptTemplateValidation"`
Expected: 全绿(默认阈值 0 → 既有行为零变化)。

- [ ] **Step 2: 真实验证(需后台 API 跑新构建 + 登录)**

重启 API(带本次改动);登录拿 token 写入 `/tmp/tok`;对 fileId 369(heavy.docx,客户30/制程1)跑预览,config 用前端默认(`enableLlmSemanticPriority:true, embeddingSemanticAutoApplyThreshold:0.9`)。
Expected: 三条重度改写(原 manualReview)中,Emb≥0.90 的转为 **autoApply**;Emb<0.90 的仍人工但已被召回;均不误判硬冲突。

- [ ] **Step 3: 提交**

```bash
git add src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs \
        src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs \
        src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs \
        src/AcceptanceSpecSystem.Api/Services/MatchingConfigResolver.cs \
        web/src/api/matching.ts \
        web/src/views/smart-fill/components/MatchConfig.vue \
        web/src/views/smart-fill/components/MatchPreviewBestMatchCell.vue \
        docs/superpowers/specs/2026-06-15-ai-semantic-auto-match-design.md \
        docs/superpowers/plans/2026-06-15-ai-semantic-auto-match.md \
        tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs
git commit -m "feat: 同义表述靠AI自动匹配(默认语义优先+高Embedding自动通过+预览依据)"
```
(推送待用户确认;近期网络多次中断,可能需手动补推。)

---

## 验证小结(对照 spec)
- §2.1 默认开语义优先 → Task 3 Step 1(前端默认,后端默认不变)。
- §2.2 高 Emb 自动通过 + 三类闸门 + 不覆盖 different → Task 1 Step 4 + 5 个单测。
- §2.3 可见性(AI语义命中标签+Emb) → Task 3 Step 3。
- §4 验证 → Task 1 单测 + Task 4 回归 + 真实验证。
- §5 回归保护(默认 0 零变化)→ Task 1 `EmbAutoApply_WhenThresholdZero` + Task 4 Step 1。
