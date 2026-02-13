## 1. Implementation
- [ ] 1.1 定义新的 AI 服务配置模型（SK 连接器参数、服务类型、用途 LLM/Embedding、优先级/离线优先标识）
- [ ] 1.2 实现 KernelFactory/AIServiceFactory（统一构建 LLM/Embedding Kernel）
- [ ] 1.3 重构匹配引擎：Embedding 检索 Top‑1 + LLM 复核评分/理由 + 不匹配原因
- [ ] 1.4 SSE 流式：复核/重排理由增量输出
- [ ] 1.5 更新 API/DTO 与连接测试/模型探测接口
- [ ] 1.6 更新前端配置页与智能填充页（新字段/流程）
- [ ] 1.7 数据迁移与加密存储更新
- [ ] 1.8 测试与回归（匹配链路/流式/配置）
