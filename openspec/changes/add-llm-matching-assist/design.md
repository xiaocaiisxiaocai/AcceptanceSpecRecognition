## Context
现有匹配流程基于文本相似度与 Embedding 加权，候选列表会增加用户选择成本且不符合“只取最接近一条”的业务需求。系统已有 AI 服务配置与 Prompt 模板管理，但尚未接入 LLM 复核与流式输出流程。

## Goals / Non-Goals
- Goals:
- LLM 参与“最佳匹配”的复核评分与理由说明
- 低置信度/无匹配时生成验收/备注建议
- LLM 复核与生成均支持 SSE 流式输出，前端实时展示
  - 支持在线与本地 LLM（OpenAI/Azure/Ollama/LM Studio/自定义）
- Non-Goals:
- 不在本次引入向量数据库或持久化向量索引
- 不做自动批量写入（仍需用户确认）

## Decisions
- Decision: 仅返回最佳匹配（Top‑1），不再输出候选列表
  - Why: 符合业务流程，减少操作成本
- Decision: LLM 输出必须为结构化 JSON（含 score / reason / acceptance / remark）
  - Why: 便于解析与前端展示
- Decision: LLM 不可用时自动降级为原匹配结果，同时提供不匹配原因
  - Why: 保证离线与可用性，并提升可解释性
- Decision: LLM 复核/生成通过 SSE 流式返回，前端实时渲染
  - Why: 降低等待感并提升交互体验

## Risks / Trade-offs
- LLM 结果可能不稳定 → 使用固定提示词 + JSON schema 限制
- 成本与延迟上升 → 仅对“最佳匹配/低置信度”触发，SSE 渐进展示
- 生成建议误导 → 强制用户确认与可视化标识

## Migration Plan
- 仅新增逻辑与配置，不涉及数据迁移

## Open Questions
- SSE 事件结构与前端状态更新策略
- LLM 输出是否需要持久化到历史记录？
