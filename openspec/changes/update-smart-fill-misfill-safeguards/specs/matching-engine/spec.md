## MODIFIED Requirements

### Requirement: 关键冲突证据保守降级
系统 SHALL 在最终排序前优先检查关键字段冲突证据，并对存在冲突证据的候选保守降级为人工确认，而不是直接自动采用。当且仅当 `EnableLlmSemanticPriority` 开启时，系统 SHALL 允许 LLM 等价裁决（判定 `equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence`）覆盖比较符、方向/极性、否定语义等语义方向类冲突的保守降级；量纲型硬冲突（数值/单位不等价、温度跨温标、尺寸元组位置不一致）在任何模式下 SHALL 保持保守降级，LLM 裁决不可覆盖。该开关关闭时保守降级为绝对门禁，LLM 裁决不可覆盖。

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

#### Scenario: 语义优先模式下 LLM 等价覆盖语义方向类冲突降级
- **GIVEN** `EnableLlmSemanticPriority` 已开启
- **AND** 当前最佳候选仅存在方向/极性、比较符或否定语义类冲突证据
- **AND** LLM 等价裁决返回 `equivalent` 且自评置信度不低于 `LlmEquivalenceMinConfidence`
- **WHEN** 系统生成最终决策
- **THEN** 系统 SHALL 允许自动采用该候选，覆盖保守降级
- **AND** 系统 SHALL 在结果说明中标注该结果由语义优先模式下的 LLM 裁决放行

#### Scenario: 语义优先模式下量纲型硬冲突不可被覆盖
- **GIVEN** `EnableLlmSemanticPriority` 已开启
- **AND** 当前最佳候选存在数值/单位不等价、温度跨温标或尺寸元组冲突
- **WHEN** LLM 等价裁决返回 `equivalent`，且自评置信度不低于 `LlmEquivalenceMinConfidence`
- **THEN** 系统 SHALL 将该候选保守降级为人工确认
- **AND** 系统 SHALL NOT 自动采用该候选

#### Scenario: 标准模式下冲突降级不可被覆盖
- **GIVEN** `EnableLlmSemanticPriority` 关闭
- **AND** 当前最佳候选存在关键字段冲突证据
- **WHEN** 系统生成最终决策
- **THEN** 系统 SHALL 将该候选保守降级为人工确认
- **AND** 即使 LLM 判定等价也不得自动采用

### Requirement: 自动采用门禁
系统 SHALL 仅在 AI 等价裁决明确通过、证据充分且无需人工确认时自动采用匹配结果。在标准模式下，自动采用以"不存在关键冲突证据"为前提；在 `EnableLlmSemanticPriority` 开启时，存在语义方向类冲突证据的候选 SHALL 改由 LLM 等价裁决决定是否自动采用，而不是被前置拦截；存在量纲型硬冲突的候选 SHALL 转人工确认。

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
- **THEN** 系统 SHALL 调用 LLM 等价裁决
- **AND** 仅当冲突均为语义方向类时，系统 SHALL 由裁决结果决定是否自动采用
- **AND** 存在量纲型硬冲突时，系统 SHALL 转人工确认

### Requirement: LLM 语义优先模式
系统 SHALL 提供可选的 LLM 语义优先模式（`EnableLlmSemanticPriority`，默认关闭）。开启后，LLM 等价裁决对语义方向类硬冲突（比较符、方向/极性、否定语义）具有最高权威，以提高语义命中率；量纲型硬冲突（数值/单位、温度跨温标、尺寸元组）仍为确定性门禁，不受该模式影响。关闭时系统行为与标准模式完全一致。

#### Scenario: 默认关闭不影响标准模式
- **GIVEN** 请求未开启 `EnableLlmSemanticPriority`
- **WHEN** 系统执行匹配门禁决策
- **THEN** 系统 SHALL 保持硬冲突绝对门禁优先级
- **AND** 硬冲突行 SHALL 强制人工确认，AI 裁决不可覆盖

#### Scenario: 语义优先模式下 LLM 等价覆盖语义方向类硬冲突
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 当前最佳候选仅存在比较符、方向/极性或否定语义类硬冲突
- **WHEN** LLM 等价裁决返回 `equivalent`
- **AND** LLM 自评置信度大于等于 `LlmEquivalenceMinConfidence`
- **THEN** 系统 SHALL 将该结果判定为自动填充
- **AND** 系统 SHALL 在结果说明中标注「语义优先模式下交由 LLM 裁决」

#### Scenario: 语义优先模式下量纲型硬冲突仍强制人工
- **GIVEN** 请求开启 `EnableLlmSemanticPriority`
- **AND** 源规格为"转速3000rpm"，候选规格为"转速300rpm"
- **WHEN** LLM 等价裁决返回 `equivalent`
- **THEN** 系统 SHALL 将该行标记为需要人工确认
- **AND** 系统 SHALL NOT 自动填充

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

## ADDED Requirements

### Requirement: 高 Embedding 语义自动通过不得在 LLM 未实际裁决时放行
系统 SHALL 仅在 LLM 等价裁决真实返回 `uncertain` 时，允许 Embedding 分达到 `EmbeddingSemanticAutoApplyThreshold` 的候选凭语义相似度自动通过；LLM 调用失败、超时、熔断或批次额度耗尽而回退的结果 SHALL 视为未裁决并转人工确认。

#### Scenario: LLM 调用失败时高 Embedding 候选转人工
- **GIVEN** `EmbeddingSemanticAutoApplyThreshold` 为 0.9
- **AND** 当前最佳候选 Embedding 分为 0.95 且无冲突证据
- **WHEN** LLM 等价裁决调用抛出异常或超时
- **THEN** 系统 SHALL 将该行标记为需要人工确认
- **AND** 系统 SHALL NOT 自动填充

#### Scenario: LLM 真实返回 uncertain 时允许高 Embedding 兜底
- **GIVEN** `EmbeddingSemanticAutoApplyThreshold` 为 0.9
- **AND** 当前最佳候选 Embedding 分为 0.95、无冲突证据且不歧义
- **WHEN** LLM 等价裁决成功返回 `uncertain`
- **THEN** 系统 MAY 凭 Embedding 语义相似度自动通过该候选
- **AND** 系统 SHALL 在结果说明中标注建议优先复查

### Requirement: 数值单位识别区分大小写
系统 SHALL 在数值单位识别与归一化中区分单位大小写，`mΩ` 与 `MΩ`、`mW` 与 `MW` 等前缀不同的单位 SHALL 归一到各自的量纲基准值，并支持 `mΩ`、`μΩ` 电阻单位。

#### Scenario: 毫欧与兆欧判定为数值冲突
- **GIVEN** 源规格为"阻值 10mΩ"，候选规格为"阻值 10MΩ"
- **WHEN** 系统完成数值单位冲突扫描
- **THEN** 系统 SHALL 生成 `numeric_unit_conflict` 硬冲突
- **AND** 系统 SHALL NOT 自动填充该候选

### Requirement: 近似规范化命中须比对裸数字并判定歧义
系统 SHALL 在近似规范化确定性命中前，除带单位的归一数值外，单独比对规格中的无单位裸数字集合；裸数字数量不同或任一对应项超出工程容差时 SHALL NOT 判为近似命中。仅规格模式下近似规范化命中多条候选时，系统 SHALL 将结果标记为歧义并转人工确认。

#### Scenario: 裸数字差异不得被骨架抹平
- **GIVEN** 源规格为"额定电压220V，长度300"，候选规格为"额定电压220V，长度3000"
- **WHEN** 系统执行近似规范化匹配
- **THEN** 系统 SHALL NOT 将该候选判为近似命中
- **AND** 系统 SHALL NOT 确定性自动填充

#### Scenario: 仅规格模式近似命中多条候选判歧义
- **GIVEN** 匹配模式为仅规格
- **AND** 两条候选规格经规范化后与源规格在工程容差内等价，但验收结论不同
- **WHEN** 系统执行近似规范化匹配
- **THEN** 系统 SHALL 将结果标记为歧义
- **AND** 系统 SHALL 将该行标记为需要人工确认

### Requirement: 项目名包含关系须满足长度比
系统 SHALL 仅在较短项目名长度不低于较长项目名长度 70% 时，将项目名子串包含关系计为接近分（0.85）；否则 SHALL 计为 0.5，避免短项目名被长项目名包含而抬高跨项目候选。

#### Scenario: 短项目名被长项目名包含
- **GIVEN** 源项目为"A机"，候选项目为"A机型B"，规格完全一致
- **WHEN** 系统计算项目得分
- **THEN** 项目得分 SHALL 为 0.5
