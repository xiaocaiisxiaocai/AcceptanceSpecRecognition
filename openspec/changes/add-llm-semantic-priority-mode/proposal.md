# Proposal: LLM 语义优先模式

## Why
现有智能填充匹配以**确定性硬冲突门禁**为最高优先级：当源项与候选项在数值、单位、比较符、温度、方向上存在差异时，无论 Embedding 分数多高、无论 AI 是否判定等价，系统都强制转人工确认（见 `add-ai-equivalence-adjudication`：「AI 裁决不覆盖硬冲突」）。

该策略在云端按量计费、对误填零容忍的场景下是合理的。但本项目主要为**本地部署**，LLM 调用无费用约束。部分客户的验收规格中，数值/单位差异在业务上常常是**等价的上下位表达**（例如「电压 ≥100V」与「电压 ≥220V」在特定环境下满足同一上位要求，或「20±2℃」与「18~22℃」表达同一区间）。这类行被硬冲突规则一律拦截后，人工复核量居高不下，削弱了智能填充的价值。

客户希望提供一个可选的高召回模式：在明确知情的前提下，让 LLM 的语义等价判断具有最高权威，覆盖确定性硬冲突规则，用更高的人工成本换取更高的自动命中率。

## What Changes
- 新增 `EnableLlmSemanticPriority` 配置开关（**默认关闭**），仅在用户主动开启时改变门禁优先级。
- 开启后，当 LLM 等价裁决返回 `Equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence` 时，结果判定为 `AutoApply`，**覆盖**数值/单位/比较符/温度/方向等硬冲突门禁。
- 开启后，硬冲突行不再被前置短路拦截，而是被送入 LLM 等价裁决流程。
- 新增 `LlmSemanticRecallThreshold` 配置（默认 `0.5`），在语义优先模式下降低 Embedding 召回阈值，使更多语义相近但向量分偏低的候选进入 LLM 裁决覆盖面。
- 置信度门槛（`LlmEquivalenceMinConfidence`）在该模式下**仍然生效**：LLM 判等价但置信度不足时，仍转人工确认，避免盲目自动通过。
- 标准模式（开关关闭）行为完全不变，硬冲突仍为绝对门禁。

## Impact
- Affected specs: `matching-engine`
  - MODIFIED: `关键冲突证据保守降级`、`自动采用门禁` — 声明语义优先模式下 LLM 等价裁决可覆盖冲突降级，消除与原"AI 裁决不覆盖硬冲突"约束的语义矛盾。
  - ADDED: `LLM 语义优先模式`、`语义优先模式召回阈值`。
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`（新增 `EnableLlmSemanticPriority`、`LlmSemanticRecallThreshold`）
  - `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`（门禁决策与召回逻辑）
  - `src/AcceptanceSpecSystem.Api/Services/MatchingConfigResolver.cs`（解析与边界裁剪）
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`（DTO 暴露字段）
  - `web/src/views/smart-fill/components/MatchConfig.vue`（前端开关）
- 风险提示：该模式有意降低安全门禁，必须在前端明确标注「实验性 / 高召回但需谨慎」并默认关闭。
