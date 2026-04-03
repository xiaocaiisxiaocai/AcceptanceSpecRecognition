## ADDED Requirements
### Requirement: LLM思考内容抑制
系统 SHALL 在启用关闭思考模式时，尽量避免向前端和解析逻辑暴露模型思考内容。

#### Scenario: Ollama请求关闭思考模式
- **GIVEN** LLM 服务类型为 Ollama
- **AND** AI 服务配置已开启关闭思考模式
- **WHEN** 系统调用 LLM 复核或生成能力
- **THEN** 系统向底层模型请求传递关闭思考模式参数

#### Scenario: 模型仍返回思考内容
- **GIVEN** 模型响应中包含 `<think>` 思考片段
- **WHEN** 系统处理 LLM 非流式或流式输出
- **THEN** 系统清理思考片段后再用于前端展示或 JSON 解析
