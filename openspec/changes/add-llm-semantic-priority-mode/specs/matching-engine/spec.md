## MODIFIED Requirements

### Requirement: 关键冲突证据保守降级
系统 SHALL 在最终排序前优先检查关键字段冲突证据，并对存在冲突证据的候选保守降级为人工确认，而不是直接自动采用。当且仅当 `EnableLlmSemanticPriority` 开启时，系统 SHALL 允许 LLM 等价裁决（判定 `equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence`）覆盖该保守降级；该开关关闭时保守降级为绝对门禁，LLM 裁决不可覆盖。

#### Scenario: 数值约束明确冲突
- **GIVEN** 源文本包含"宽度小于0.5cm"
- **AND** 候选文本包含"宽度等于0.7cm"
- **WHEN** 系统完成数值约束比较
- **THEN** 系统为该候选生成冲突证据与问题说明
- **AND** 该候选不得自动采用

#### Scenario: 型号或品牌明确冲突
- **GIVEN** 源文本与候选文本在同一槽位上提取到不同的型号或不同的标准化品牌实体
- **WHEN** 系统完成证据判定
- **THEN** 系统为该候选生成冲突证据与问题说明
- **AND** 不得因为文本语义接近而提升为自动采用结果

#### Scenario: 语义优先模式下 LLM 等价覆盖冲突降级
- **GIVEN** `EnableLlmSemanticPriority` 已开启
- **AND** 当前最佳候选存在关键字段冲突证据
- **AND** LLM 等价裁决返回 `equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence`
- **WHEN** 系统生成最终决策
- **THEN** 系统 SHALL 允许自动采用该候选，覆盖保守降级
- **AND** 系统 SHALL 在结果说明中标注该结果由语义优先模式下的 LLM 裁决放行

#### Scenario: 标准模式下冲突降级不可被覆盖
- **GIVEN** `EnableLlmSemanticPriority` 关闭
- **AND** 当前最佳候选存在关键字段冲突证据
- **WHEN** 系统生成最终决策
- **THEN** 系统 SHALL 将该候选保守降级为人工确认
- **AND** 即使 LLM 判定等价也不得自动采用

### Requirement: 自动采用门禁
系统 SHALL 仅在 AI 等价裁决明确通过、证据充分且无需人工确认时自动采用匹配结果。在标准模式下，自动采用以"不存在关键冲突证据"为前提；在 `EnableLlmSemanticPriority` 开启时，存在关键冲突证据的候选 SHALL 改由 LLM 等价裁决决定是否自动采用，而不是被前置拦截。

#### Scenario: 满足自动采用条件
- **GIVEN** 最佳候选不存在关键冲突证据
- **AND** 关键字段关系为 `Exact` 或 `Compatible`
- **AND** 当前样本未被标记为高歧义，或高歧义但 LLM 复核通过
- **WHEN** 系统生成最终决策
- **THEN** 系统允许自动采用该候选

#### Scenario: 证据不足时禁止自动采用
- **GIVEN** 最佳候选不存在明确关键冲突证据
- **AND** 但关键字段仅得到 `Overlap` 或 `PossiblyRelated` 结果
- **WHEN** 系统生成最终决策
- **THEN** 系统将该样本标记为需要人工确认
- **AND** 不自动采用该候选

#### Scenario: 语义优先模式下冲突候选进入 LLM 裁决而非前置拦截
- **GIVEN** `EnableLlmSemanticPriority` 已开启
- **AND** 最佳候选存在关键冲突证据
- **WHEN** 系统进入自动采用门禁
- **THEN** 系统 SHALL 调用 LLM 等价裁决，由裁决结果决定是否自动采用
- **AND** 系统 SHALL NOT 仅因存在冲突证据就直接拦截为人工确认

## ADDED Requirements

### Requirement: LLM 语义优先模式
系统 SHALL 提供可选的 LLM 语义优先模式（`EnableLlmSemanticPriority`，默认关闭）。开启后，LLM 等价裁决具有最高权威，覆盖确定性硬冲突门禁，以最大化语义命中率；关闭时系统行为与标准模式完全一致。

#### Scenario: 默认关闭不影响标准模式
- **GIVEN** 请求未开启 `EnableLlmSemanticPriority`
- **WHEN** 系统执行匹配门禁决策
- **THEN** 系统 SHALL 保持硬冲突绝对门禁优先级
- **AND** 硬冲突行 SHALL 强制人工确认，AI 裁决不可覆盖

#### Scenario: 语义优先模式下 LLM 等价覆盖硬冲突
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 当前最佳候选存在硬冲突（数值/单位/比较符/温度/方向差异）
- **WHEN** LLM 等价裁决返回 `equivalent`
- **AND** LLM 自评置信度大于等于 `LlmEquivalenceMinConfidence`
- **THEN** 系统 SHALL 将该结果判定为自动填充
- **AND** 系统 SHALL 在结果说明中标注「语义优先模式下交由 LLM 裁决」

#### Scenario: 语义优先模式下硬冲突行进入 LLM 裁决
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 当前最佳候选存在硬冲突
- **WHEN** 系统进入门禁决策
- **THEN** 系统 SHALL 调用 LLM 等价裁决，而不是前置拦截为人工确认

#### Scenario: 置信度门槛护栏在语义优先模式下仍生效
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** `LlmEquivalenceMinConfidence` 大于 0
- **WHEN** LLM 等价裁决返回 `equivalent` 但自评置信度低于 `LlmEquivalenceMinConfidence`
- **THEN** 系统 SHALL 将该结果标记为需要人工确认
- **AND** 系统 SHALL NOT 自动填充

### Requirement: 语义优先模式召回阈值
系统 SHALL 在语义优先模式下使用独立的召回分数下限 `LlmSemanticRecallThreshold`（默认 0.5，取值范围 `[0.1, 0.9]`），以扩大 LLM 等价裁决的候选覆盖面。

#### Scenario: 低 Embedding 分候选被召回进入 LLM
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 候选 Embedding 分低于标准高置信阈值但高于 `LlmSemanticRecallThreshold`
- **WHEN** 系统执行召回
- **THEN** 该候选 SHALL 被召回并进入 LLM 等价裁决
