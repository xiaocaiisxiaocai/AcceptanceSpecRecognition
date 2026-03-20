# Change: 增加 AI 服务关闭思考模式配置

## Why
部分 Ollama 思考模型在推理时会输出思考内容，导致连接测试、LLM 复核流式输出和最终 JSON 解析体验不稳定。当前系统也没有提供控制项来显式关闭该行为。

## What Changes
- 为 AI 服务配置新增“关闭思考模式”开关，面向 LLM 配置持久化保存。
- 针对 Ollama 服务在聊天调用时下发关闭思考参数，并兼容 `Endpoint` 填写为根地址或 `/api` 地址。
- 为 LLM 非流式/流式输出增加思考内容兜底清洗，避免 `<think>` 内容泄露到前端或影响 JSON 解析。
- 更新配置页与规格说明，使管理员可见、可配、可验证。

## Impact
- Affected specs: `user-interface`, `data-storage`, `matching-engine`
- Affected code:
  - `src/AcceptanceSpecSystem.Data/Entities/AiServiceConfig.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs`
  - `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
  - `web/src/views/config/ai-services/index.vue`
  - `web/src/api/ai-service.ts`
