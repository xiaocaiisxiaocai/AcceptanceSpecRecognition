## 1. Implementation
- [x] 1.1 定义新的 AI 服务配置模型（SK 连接器参数、服务类型、用途 LLM/Embedding、优先级/离线优先标识）
- [x] 1.2 实现 KernelFactory/AIServiceFactory（统一构建 LLM/Embedding Kernel）
- [x] 1.3 重构匹配引擎：Embedding 检索 Top‑1 + LLM 复核评分/理由 + 不匹配原因
- [x] 1.4 SSE 流式：复核/重排理由增量输出
- [x] 1.5 更新 API/DTO 与连接测试/模型探测接口
- [x] 1.6 更新前端配置页与智能填充页（新字段/流程）
- [x] 1.7 旧代码清理（17 个文件已删除；加密存储后续单独处理）
- [x] 1.8 测试与回归（匹配链路/流式/配置）

