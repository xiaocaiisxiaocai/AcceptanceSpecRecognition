# Change: 稳定智能结构识别 AI 辅助

## Why

智能结构识别当前把运行状态 `Unknown/Checking` 与明确不可用混为同一候选过滤结果，导致已经指定的 LLM 在短暂探测期间被静默跳过。与此同时，Ollama 列语义召回只通过 Prompt 约束 JSON，模型偶发返回无法解析的内容后，接口仍只返回规则识别结果，前端无法判断 AI 是否真正生效。

## What Changes

- 区分 AI 自动选择与显式服务执行：自动选择仍只返回已确认可用服务；显式指定且配置有效的服务在 `Unknown/Checking` 时执行有界真实调用，只有新鲜 `Unavailable` 才提前回退。
- 为要求 JSON 的 Ollama 非流式调用传递结构化输出 Schema，并在格式不合规且剩余预算允许时最多修正重试一次。
- 在智能结构识别响应中增加兼容的 AI 辅助执行摘要，区分已应用、不需要、部分应用和降级及其原因。
- 前端等待窗口与后端探测上限对齐，并明确展示 AI 未应用或部分应用，禁止静默把规则结果表现为 AI 结果。
- 增加 readiness 状态、结构化输出、降级契约、取消和性能回归覆盖。

## Impact

- Affected specs: `api`, `matching-engine`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Application/Services/AiServiceReadinessRegistry.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.*.cs`
  - `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/OllamaNativeChatCompletionService.cs`
  - `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
  - `web/src/utils/runtime-ai-selection-loader.ts`
  - 数据导入与智能填充共享智能结构识别前端链路
- Compatibility: API 仅增加可选响应字段；不修改数据库结构，不需要迁移。
- Related active change: `add-runtime-ai-availability-and-upload-control`。本变更补强业务执行与结果透明度，不改变其自动选择、健康检查和上传控制要求。
