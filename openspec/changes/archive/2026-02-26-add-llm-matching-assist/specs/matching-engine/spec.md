## MODIFIED Requirements

### Requirement: 候选结果排序与Top-N
系统 MUST 仅返回最佳匹配结果（Top‑1），不再返回候选列表。

#### Scenario: 仅返回最佳匹配
- **GIVEN** 匹配结果包含多条候选
- **WHEN** 系统返回匹配结果
- **THEN** 系统仅返回得分最高的1条作为最佳匹配
- **AND** 不返回候选列表

---

## ADDED Requirements

### Requirement: LLM 复核评分与理由
系统 SHALL 在用户启用 LLM 复核时，对最佳匹配输出 LLM 评分与理由说明，并支持流式返回。

#### Scenario: LLM 复核流式返回
- **GIVEN** 用户启用 LLM 复核且存在最佳匹配
- **WHEN** 系统执行匹配预览并启动 SSE 流
- **THEN** 系统以流式事件返回 LLM 评分与理由的增量内容

#### Scenario: LLM 复核降级
- **GIVEN** 用户启用 LLM 复核但 LLM 服务不可用
- **WHEN** 系统执行匹配预览
- **THEN** 系统保留原匹配结果并标记为降级

---

### Requirement: LLM 生成验收建议
系统 SHALL 在用户启用 LLM 生成时，为低置信度或无匹配的行生成验收/备注建议，并支持流式返回。

#### Scenario: 低置信度生成建议
- **GIVEN** 用户启用 LLM 生成且当前行最佳匹配置信度为低
- **WHEN** 系统执行匹配预览并启动 SSE 流
- **THEN** 系统以流式事件返回 LLM 生成的验收/备注建议

#### Scenario: 无匹配生成建议
- **GIVEN** 用户启用 LLM 生成且该行无匹配
- **WHEN** 系统执行匹配预览并启动 SSE 流
- **THEN** 系统以流式事件返回 LLM 生成的验收/备注建议

---

### Requirement: 不匹配原因返回
系统 SHALL 在无匹配时返回明确原因，便于用户理解。

#### Scenario: 无候选数据
- **GIVEN** 用户指定匹配范围且范围内无候选数据
- **WHEN** 系统执行匹配预览
- **THEN** 系统返回“不匹配原因=范围内无候选数据”

#### Scenario: 得分低于阈值
- **GIVEN** 最佳匹配得分低于阈值
- **WHEN** 系统执行匹配预览
- **THEN** 系统返回“不匹配原因=最佳得分低于阈值”

#### Scenario: LLM 降级说明
- **GIVEN** 用户启用 LLM 复核/生成但 LLM 服务不可用或解析失败
- **WHEN** 系统执行匹配预览
- **THEN** 系统在不匹配原因中附加“LLM已降级”的说明

---

### Requirement: LLM 结果可解释
系统 SHALL 在 LLM 复核或生成时返回可解析的结构化信息，支持前端展示与用户确认。

#### Scenario: 结构化结果返回
- **WHEN** LLM 输出匹配结果
- **THEN** 系统将结果解析为结构化字段（得分/理由/建议）
