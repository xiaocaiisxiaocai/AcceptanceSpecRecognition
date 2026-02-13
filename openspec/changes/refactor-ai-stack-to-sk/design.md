## Context
当前 AI 功能已引入 LLM 复核与流式输出的需求，但实现路径仍依赖直连 HTTP 与多套适配逻辑。为了统一离线优先策略、错误处理、SSE 流式与模型配置，需要将 AI 调用统一收敛到 Semantic Kernel。

## Goals / Non-Goals
- Goals:
  - 使用 Semantic Kernel 统一编排 LLM 与 Embedding 调用。
  - 匹配链路改为 Embedding 检索 Top‑1 + LLM 复核评分/理由。
  - 离线优先（Ollama/LM Studio）与在线兜底（OpenAI/Azure/自定义）。
  - 失败不降级到非 LLM/Embedding，并返回明确失败原因。
  - 保持 SSE 流式输出与前端实时展示能力。
- Non-Goals:
  - 不保留旧版文本相似度算法与权重配置。
  - 不兼容旧版 AI 配置结构与旧 API 字段。
  - 不调整 Word 解析与导入流程。

## Decisions
- 以 KernelFactory 作为唯一 AI 入口，按用途（LLM/Embedding）组装 Kernel。
- OpenAI/Azure 使用 SK 官方连接器；Ollama/LM Studio/自定义端点统一走 OpenAI 兼容连接器。
- 服务选择策略为“离线优先”，本地不可用时再尝试在线服务。
- 匹配流程为：文本预处理 → 组合文本 → Embedding 相似度 → Top‑1 → LLM 复核评分/理由。
- 若 LLM 或 Embedding 在所有可用服务上均失败，直接返回失败原因，不降级到非 LLM/Embedding。
- LLM 复核与理由通过 SSE 流式输出，前端实时展示。

## Risks / Trade-offs
- 旧配置无法自动兼容，需重新录入。
- LLM 依赖提升，服务不可用时可能导致匹配失败。
- SK 版本升级会影响连接器行为，需要固定版本与回归测试。

## Migration Plan
1. 增加新的 AI 服务配置字段与存储结构。
2. 提供配置迁移提示或向导，旧配置标记为弃用。
3. 完成 KernelFactory 与匹配链路改造后切换开关。
4. 回归测试与灰度验证后移除旧路径。

## Open Questions
- 默认的 Embedding 与 LLM 模型是否需要预置？
- Embedding 相似度阈值默认值与可配置范围？
- LLM 复核评分的标度（0‑1 或 0‑100）如何统一？
