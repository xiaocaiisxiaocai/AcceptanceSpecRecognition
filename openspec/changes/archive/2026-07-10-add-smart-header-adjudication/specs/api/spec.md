## ADDED Requirements
### Requirement: 智能结构识别按需表头裁决
系统 SHALL 在智能配置识别接口中，仅当规则表头识别不确定或结构健康检查降级时，按需调用 LLM 裁决表头结构。

#### Scenario: 规则明确时不调用 LLM
- **GIVEN** Word 或 Excel 表格的规则表头识别结果置信明确
- **WHEN** 用户调用 `POST /api/smart-config/recognize`
- **THEN** 系统使用规则识别结果继续列映射
- **AND** 不调用 LLM 表头裁决

#### Scenario: 不确定表头触发裁决
- **GIVEN** 规则表头候选分数接近、低置信或结构健康检查结果为 `NeedConfirm`
- **WHEN** 文档仍有 LLM 结构裁决预算
- **THEN** 系统向 LLM 提交表格预览、规则候选和参考模板
- **AND** LLM 仅返回表头结构字段与置信说明

#### Scenario: 合法裁决重新提取表格
- **GIVEN** LLM 返回的 `headerRowIndex`、`headerRowCount` 和 `dataStartRowIndex` 均在表格范围内
- **WHEN** 系统接受该裁决
- **THEN** 系统按该表头结构重新提取 Word 或 Excel 表格
- **AND** 重新执行列映射与结构健康检查

#### Scenario: 非法裁决回退规则结果
- **GIVEN** LLM 返回的表头结构越界、行数无效或数据起始行早于表头结束
- **WHEN** 系统校验裁决结果
- **THEN** 系统丢弃该裁决
- **AND** 保留规则识别结果并返回待确认状态

#### Scenario: 预算耗尽不调用裁决
- **GIVEN** 当前文档的 `MaxStructureAdjudicationCallsPerDocument` 预算为 0 或已耗尽
- **WHEN** 规则表头识别不确定
- **THEN** 系统不调用 LLM 表头裁决
- **AND** 返回规则识别与健康检查结果
