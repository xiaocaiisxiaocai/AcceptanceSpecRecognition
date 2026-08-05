## ADDED Requirements

### Requirement: 智能结构列语义召回使用结构化输出约束
系统 MUST 对要求 JSON 的列语义召回使用供应商支持的结构化输出约束，并继续执行服务端结构和业务校验。

#### Scenario: Ollama 返回符合 Schema 的候选
- **WHEN** 系统通过 Ollama 原生非流式接口执行列语义召回
- **THEN** 请求携带约束 `suggestions`、字段枚举、列索引和置信度的 JSON Schema
- **AND** 后端仍校验列索引范围、字段冲突和保守采用门禁

#### Scenario: 非法结构在预算内修正一次
- **GIVEN** 模型成功返回但输出未通过结构解析
- **AND** 当前列语义召回总超时预算仍然充足
- **WHEN** 系统处理该格式错误
- **THEN** 系统可以对同一服务执行最多一次结构修正重试
- **AND** 不切换到其他服务
- **AND** 不把格式错误标记为服务基础设施不可用

#### Scenario: 修正失败保留规则结果
- **GIVEN** 结构修正后输出仍然无效或总预算已经耗尽
- **WHEN** 系统生成智能结构识别结果
- **THEN** 系统丢弃该 LLM 结果并保留规则识别结果
- **AND** AI 执行摘要报告 `invalidOutput` 或 `timeout`
