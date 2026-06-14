# 等价裁决 Prompt few-shot 示例 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给「智能填充等价裁决」Prompt 增加 9 个紧凑 few-shot 示例，提升本地 LLM 灰区裁决质量与输出一致性，并经现有自动升级链下发给老库。

**Architecture:** 仅改 `PromptTemplateCatalog` 中等价裁决模板的默认内容字符串——把当前默认内容原样保留为新的 legacy 条目（供自动升级识别），新默认内容 = 当前内容 + 在输出格式说明前插入【判定示例】段。不动规则、反义词、JSON 上下文、输出协议；不动其余模板、确定性层、阈值；无 EF 迁移、无 DTO/前端改动。

**Tech Stack:** C# / .NET 8，xUnit + FluentAssertions。

设计依据：`docs/superpowers/specs/2026-06-14-fewshot-equivalence-adjudication-design.md`

---

## File Structure

- `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs` — 改：新默认内容 + 旧默认入 `AdditionalLegacyContents`。
- `tests/AcceptanceSpecSystem.Core.Tests/LlmReviewPromptTests.cs` — 改：新增模板内容/升级断言。

---

### Task 1: 模板内容/升级断言（TDD 红）

**Files:**
- Test: `tests/AcceptanceSpecSystem.Core.Tests/LlmReviewPromptTests.cs`

- [ ] **Step 1: 写失败测试**

在 `LlmReviewPromptTests` 类中、`MatchingEquivalencePromptTemplate_ShouldDescribeUnifiedReviewGate` 方法之后，新增：

```csharp
    [Fact]
    public void MatchingEquivalencePromptTemplate_ShouldIncludeFewShotExamples_AndPreservePreviousDefaultAsLegacy()
    {
        var definition = PromptTemplateCatalog
            .GetSystemTemplates()
            .Single(template => template.Scene == PromptTemplateScene.MatchingEquivalenceAdjudication);

        // 新默认内容含 few-shot 示例段
        definition.DefaultContent.Should().Contain("【判定示例】");

        // 升级链保留历次旧默认（含本次之前的默认），且旧内容都不含 few-shot 段
        definition.AdditionalLegacyContents.Should().NotBeNull();
        definition.AdditionalLegacyContents!.Should().HaveCountGreaterThanOrEqualTo(3);
        definition.AdditionalLegacyContents!.Should().OnlyContain(content => !content.Contains("【判定示例】"));
    }
```

- [ ] **Step 2: 跑测试确认红**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MatchingEquivalencePromptTemplate_ShouldIncludeFewShotExamples"`
Expected: FAIL —— `DefaultContent` 不含「【判定示例】」，且 `AdditionalLegacyContents` 当前仅 2 条。

---

### Task 2: 实现 few-shot 示例 + 保留旧默认为 legacy（TDD 绿）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs`

- [ ] **Step 1: 把当前默认内容原样另存为 V3 const**

在 `PromptTemplateCatalog` 中，紧接现有 `MatchingEquivalenceAdjudicationV2Content` 之后，新增一个 const
`MatchingEquivalenceAdjudicationV3Content`，其值 = **当前 `MatchingEquivalenceAdjudicationDefaultContent` 的完整原文，逐字复制，不做任何改动**（这是本次之前的默认内容，用于老库自动升级识别）。

- [ ] **Step 2: 在新默认内容里插入【判定示例】段**

修改现有 `MatchingEquivalenceAdjudicationDefaultContent`（`PromptTemplateCatalog.cs` 内、原 `:252-308`）：
在其末尾的 `仅返回严格 JSON：` 这一行**之前**，插入下面整段（其余文字保持不变）：

```
        【判定示例】（仅供参照风格，按此输出 JSON，不要照抄示例内容、也不要把示例当作待判对象）
        1) 源：电机 | 功率 7.5kW
           候选：电机 | 功率 7500W
           {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"7.5kW 换算为 7500W，数值与量纲完全一致","confidence":0.97}
        2) 源：伺服电机 | 品牌 Panasonic 型号 MSMF012L1U2M
           候选：伺服电机 | 品牌 松下 型号 MSMF012L1U2M
           {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"Panasonic 与 松下 为同一品牌中英文名，型号一致","confidence":0.95}
        3) 源：输出电压：DC 24V（±5%）
           候选：输出电压: DC24V (±5%)
           {"verdict":"equivalent","reasonType":"format_only","reason":"仅全半角/空格/冒号格式差异，数值与容差一致","confidence":0.96}
        4) 源：噪音 ≤ 60dB
           候选：噪音 ≥ 60dB
           {"verdict":"different","reasonType":"symbol_conflict","reason":"比较符方向相反（≤ vs ≥），约束含义相反","confidence":0.98}
        5) 源：气缸动作 | 到位后气缸上升
           候选：气缸动作 | 到位后气缸下降
           {"verdict":"different","reasonType":"semantic_difference","reason":"动作方向相反（上升 vs 下降）","confidence":0.97}
        6) 源：循环时间 ≤ 1秒
           候选：循环时间 ≤ 2秒
           {"verdict":"different","reasonType":"semantic_difference","reason":"同单位下数值不同（1秒 vs 2秒），指标要求不同","confidence":0.97}
        7) 源：轴承 SKF-6204
           候选：轴承 SKF-6205
           {"verdict":"different","reasonType":"semantic_difference","reason":"型号尾数不同（6204 vs 6205），为不同物料","confidence":0.96}
        8) 源：轴承 SKF-6204-2Z
           候选：轴承 SKF 6204 2Z
           {"verdict":"equivalent","reasonType":"format_only","reason":"仅连字符/空格分隔差异，型号 6204-2Z 一致","confidence":0.95}
        9) 源：视觉检测 | 检测精度 高
           候选：视觉检测 | 检测精度 ±0.02mm
           {"verdict":"uncertain","reasonType":"uncertain","reason":"源为定性描述'高'，无法与定量 ±0.02mm 判定等价，需人工确认","confidence":0.3}

```

注意：该模板是 C# raw string literal（`"""`），示例段缩进需与同段其他行一致；示例里的 `{` `}` 是字面文本（此模板用 `{{placeholder}}` 双花括号占位，单花括号不会被 `PromptTemplatePlaceholderRenderer` 当占位符，无需转义）。

- [ ] **Step 3: 把 V3 加入自动升级链**

在 `Definitions` 数组中等价裁决模板定义的 `AdditionalLegacyContents`（`PromptTemplateCatalog.cs` 原 `:395-399`），追加 `MatchingEquivalenceAdjudicationV3Content`：

```csharp
            AdditionalLegacyContents:
            [
                MatchingEquivalenceAdjudicationV1Content,
                MatchingEquivalenceAdjudicationV2Content,
                MatchingEquivalenceAdjudicationV3Content
            ]),
```

- [ ] **Step 4: 跑 Task 1 测试确认绿**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~MatchingEquivalencePromptTemplate_ShouldIncludeFewShotExamples"`
Expected: PASS。

---

### Task 3: 回归 + 提交

**Files:** 无（仅运行与提交）

- [ ] **Step 1: 跑 Prompt/复核/语义回归**

Run: `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~LlmReviewPromptTests|FullyQualifiedName~ReviewRegression|FullyQualifiedName~EvidenceDrivenSemantic|FullyQualifiedName~PromptTemplateValidation"`
Expected: PASS（全绿；本改动只动 Prompt 文本，不应影响决策逻辑与现有断言）。

- [ ] **Step 2: 跑 Api 端 Prompt 模板回归**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~PromptTemplateApiTests|FullyQualifiedName~LlmMatchingAssist"`
Expected: PASS。

- [ ] **Step 3: 提交**

```bash
git add src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs \
        tests/AcceptanceSpecSystem.Core.Tests/LlmReviewPromptTests.cs \
        docs/superpowers/specs/2026-06-14-fewshot-equivalence-adjudication-design.md \
        docs/superpowers/plans/2026-06-14-fewshot-equivalence-adjudication.md
git commit -m "feat: 等价裁决 Prompt 增加 few-shot 示例提升灰区裁决"
```

---

## 验证小结（对照 spec 第 4 节）

- CI 可保证：Task 1 模板内容/升级断言 + Task 3 全套回归不破。
- 真实遵从（非 CI）：上线后用 `tools/SmartFillInsightReport` 观察灰区 `uncertain` 占比与裁决稳定性，据此迭代示例。
