# Change: 增加可切换的多阶段召回重排能力

## Why

基于真实样本文档的评估结果，当前单阶段 `Embedding Top1` 匹配在简单样本上可用，但在以下场景存在明显风险：

- 同一项目名下有多条不同规格描述
- 项目字段高度重复，必须依赖规格字段做细粒度区分
- 数值、单位、范围词等结构化信息对区分结果影响很大
- 候选项语义接近时，正确答案可能能被召回，但未必排在第 1 名

与此同时，现有系统已经具备：

- Embedding 主匹配链路
- 文本预处理能力
- LLM 复核与建议能力

因此，当前最合适的演进方向不是替换现有单阶段方案，而是在保持默认行为兼容的前提下，增加“可切换的多阶段召回重排”能力，用于复杂文档和高歧义样本。

## What Changes

- 为匹配请求增加“匹配策略”与“高级重排参数”配置，支持 `SingleStage` 与 `MultiStage` 两种模式。
- 明确 `MinScoreThreshold` 在 `MultiStage` 模式下继续作为第一阶段候选准入阈值；低于阈值的候选不得进入召回集合。
- 在 `MultiStage` 模式下，先按 Embedding 召回 `TopK` 候选，再执行第二阶段规则重排。
- 明确“高歧义样本”采用可配置的分差规则判定，并对仍然高歧义的候选集合按配置触发可选 LLM 复核，而不是对所有样本全量调用 LLM。
- 在匹配预览结果中增加策略、歧义状态与重排明细，便于用户理解结果来源。
- 明确执行填充必须使用用户在预览阶段确认后的匹配结果，不允许在执行阶段静默替换为不同的默认匹配。
- 在智能填充界面中提供多阶段匹配开关和高级参数配置，同时保持默认单阶段行为不变。

## Impact

- Affected specs:
  - `matching-engine`
  - `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/*`
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `web/src/views/smart-fill/*`
  - `tests/AcceptanceSpecSystem.Core.Tests/*`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
- Reference analysis:
  - `docs/matching-evaluation-and-rerank-plan.md`
