# Change: 全面采用 Semantic Kernel 的 AI 服务编排

## Why
当前 AI 链路混合了直连 HTTP、局部 LLM/Embedding 调用与分散配置，难以统一日志、流式、离线优先与错误处理策略。需要以 Semantic Kernel 作为唯一编排层，统一各类 AI 服务调用并重构匹配引擎全链路。

## What Changes
- **BREAKING** 使用 Semantic Kernel 作为唯一 AI 运行时，移除非 SK 的 LLM/Embedding 调用路径。
- **BREAKING** 取消传统文本相似度算法（Levenshtein/Jaccard/Cosine）作为主匹配链路。
- 引入 SK 统一的 Embedding 向量检索 + LLM 复核评分/理由（SSE 流式）。
- 离线优先：优先本地（Ollama/LM Studio），失败再使用在线（OpenAI/Azure/自定义）。
- 失败不降级到非 LLM/Embedding，必须返回明确失败原因。
- 调整 AI 服务配置结构与 UI；不保证旧 API/UI 兼容。

## Impact
- **Affected specs**:
  - matching-engine
  - user-interface
  - api
- **Affected code**:
  - AI 服务配置实体/迁移/加密存储
  - Semantic Kernel KernelFactory 与连接器装配
  - 匹配引擎（Embedding Top‑1 + LLM 复核）
  - AI 连接测试与模型探测
  - SSE 流式输出接口
  - 前端配置与智能填充页面
