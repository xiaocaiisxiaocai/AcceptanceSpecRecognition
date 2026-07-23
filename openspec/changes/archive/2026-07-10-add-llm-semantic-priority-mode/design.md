# Design: LLM 语义优先模式

## Context
`add-ai-equivalence-adjudication` 确立了智能填充的门禁优先级：

1. **硬冲突绝对门禁**：数值、单位、比较符、温度、方向差异 → 强制人工确认，AI 裁决不可覆盖。
2. **AI 等价裁决**：仅对**无硬冲突**且达到中置信门槛的灰区行触发，判等价则不降置信度，判不同/不确定则转人工。

该优先级假设硬冲突在业务上一定是真实差异。但在本地部署、无 API 费用约束的场景下，部分客户认为数值/单位差异往往是可由 LLM 判断的等价上下位表达，硬冲突一律拦截造成人工复核量过大。

本变更引入一个可选模式，**反转**硬冲突与 AI 裁决的优先级。

## Goals
- 提供 `EnableLlmSemanticPriority` 开关，默认关闭，不影响现有标准模式行为。
- 开启后，LLM `Equivalent` 判定（且置信度达标）覆盖硬冲突门禁，结果直接 `AutoApply`。
- 开启后，硬冲突行进入 LLM 裁决而非被前置拦截。
- 通过 `LlmSemanticRecallThreshold` 降低召回阈值，扩大 LLM 裁决覆盖面。
- 保留置信度门槛护栏，避免低置信 LLM 误判自动通过。

## Non-Goals
- 不改变标准模式（开关关闭时）的任何行为。
- 不取消置信度门槛、超时、JSON 校验、失败回退等护栏。
- 不让该模式成为默认值。

## Decisions

### 决策 1：门禁优先级反转的实现位置
在 `DetermineDecision` 中，将语义优先分支置于硬冲突检查**之前**：

```
if (EnableLlmSemanticPriority
    && LlmEquivalence.Verdict == Equivalent
    && (LlmEquivalence.Confidence >= LlmEquivalenceMinConfidence || LlmEquivalenceMinConfidence <= 0))
    return AutoApply;        // 覆盖硬冲突

if (HasHardConflict(...)) return ManualReview;   // 标准模式绝对门禁
```

**权衡**：将开关检查放在最前，保证标准模式（开关关闭）短路到原有硬冲突逻辑，行为零变化；语义优先模式仅在显式开启时生效。

### 决策 2：硬冲突行的 LLM 触发
标准模式下硬冲突行被前置短路（`SelectionSummary` 标注后直接返回，不调用 LLM）。语义优先模式下需让这些行进入 `ApplyLlmEquivalenceAdjudicationAsync`：

```
if (hasHardConflict && !EnableLlmSemanticPriority)
    标注并短路;                       // 标准模式：硬冲突不调 LLM
else if (无硬冲突 && 可确定性自动通过)
    确定性 AutoApply;
else
{
    if (hasHardConflict && EnableLlmSemanticPriority)
        标注「语义优先模式下交由 LLM 裁决」;
    await ApplyLlmEquivalenceAdjudicationAsync(...);   // 硬冲突行也进入 LLM
}
```

### 决策 3：召回阈值
语义优先模式追求高召回，单独使用 `LlmSemanticRecallThreshold`（默认 0.5）替代标准 `MinScoreThreshold`，使语义相近但 Embedding 分偏低的候选也能进入 LLM 裁决。阈值裁剪到 `[0.1, 0.9]`，避免过低导致召回噪声爆炸。

### 决策 4：置信度护栏保留
即使在语义优先模式，`LlmEquivalenceMinConfidence`（默认 0.5）仍是 `AutoApply` 的必要条件。LLM 判等价但置信度低于门槛 → 转人工。这是防止「LLM 既覆盖硬冲突又低置信盲目通过」的最后一道护栏。

## Risks / Trade-offs
- **风险**：误填风险高于标准模式。数值差异若被 LLM 误判等价且置信度虚高，会自动填入错误验收值。
- **缓解**：默认关闭；前端明确标注实验性与风险；置信度门槛兜底；审批令牌将 `EnableLlmSemanticPriority` 与 `LlmEquivalenceMinConfidence` 纳入一致性校验，防止预览与执行配置不一致。

## Migration
无数据迁移。新增配置字段均有默认值，旧请求不带这些字段时按默认（关闭 / 0.5）解析，行为与变更前一致。
