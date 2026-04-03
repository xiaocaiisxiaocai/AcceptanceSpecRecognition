## Context
当前系统通过 Semantic Kernel 的 OpenAI 兼容连接器接入 Ollama。管理员在配置页选择 `Ollama` 后，既希望能正常探测和测试连接，也希望对支持思考模式的模型关闭推理内容输出。

## Goals / Non-Goals
- Goals:
  - 提供可持久化的“关闭思考模式”配置项
  - 针对 Ollama 聊天调用传递关闭思考参数
  - 对模型返回的思考片段做兜底清洗
  - 不破坏现有 OpenAI / Azure / LM Studio / Embedding 配置
- Non-Goals:
  - 不新增新的 AI 服务类型
  - 不重构整套 LLM Prompt 体系
  - 不把“关闭思考模式”扩展成复杂的多级推理策略配置

## Decisions
- Decision: 在 `AiServiceConfig` 上新增布尔字段 `DisableThinking`，默认 `false`。
  - 原因：这是管理员级配置，应该随 AI 服务持久化，而不是散落在 Prompt 或请求参数里。
- Decision: 仅在 `ServiceType == Ollama` 且 `DisableThinking == true` 时下发兼容参数。
  - 原因：这次问题来源明确在 Ollama，其他服务是否支持需要单独验证，避免误发不兼容参数。
- Decision: 同时在流式和非流式输出上增加 `<think>...</think>` 清洗兜底。
  - 原因：即使底层参数生效，也不能假设所有模型都完全遵守。
- Decision: Ollama `Endpoint` 兼容根地址和 `/api` 地址。
  - 原因：用户现场已存在两种填写习惯，后端应吸收这类路径差异。

## Risks / Trade-offs
- 风险：部分模型即使传参仍可能输出思考内容。
  - Mitigation：增加输出清洗兜底，优先保证系统结果可用。
- 风险：流式清洗处理不当可能误删正文。
  - Mitigation：仅针对明确的 `<think>` 标记段做状态机过滤，不用正则跨块截断。

## Migration Plan
1. 新增数据库字段并生成迁移，默认值为 `false`。
2. 后端 DTO/接口读写该字段。
3. 前端配置页增加开关。
4. LLM 调用链接入参数与输出清洗。
5. 运行构建与测试。
