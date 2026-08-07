## Context

当前 readiness 注册表以短 TTL 缓存 `Available/Unavailable`，过期后状态回到 `Unknown` 并触发异步轻量探测。核心候选加载器只保留 `IsAvailable == true` 的服务，因此显式提交的 `llmServiceId` 在 `Unknown/Checking` 窗口内也会被过滤为空。前端虽然会轮询 `checking`，但默认等待约 2 秒，短于后端 5 秒探测上限，仍存在误降级窗口；当响应已经包含明确服务 ID 时，继续轮询还会增加不必要的前置等待。

列语义召回要求固定 JSON，但 Ollama 原生请求没有传递 `format`，只依赖 Prompt。真实复测已观察到模型成功返回但结构解析失败，随后规则结果被静默保留。

## Goals / Non-Goals

- Goals:
  - 消除 `Unknown/Checking` 导致的显式服务误降级。
  - 提高列语义召回结构化输出成功率，同时保持严格业务校验。
  - 让调用方和用户明确知道 AI 是否真正应用。
  - 保持规则识别兜底、取消传播、服务用途隔离和显式服务不跨模型回退。
- Non-Goals:
  - 不改变 Embedding 主匹配排序。
  - 不增加数据库表或运行时服务配置项。
  - 不把 LLM 建议改成自动采用依据。
  - 不在本变更中重写文档解析或列映射算法。

## Decisions

### Decision 1: 自动选择与显式执行使用不同 readiness 语义

- 自动选择端点继续只把 `Available` 服务返回为可用，避免未知服务被页面宣称为可用。
- 前端收到带明确服务 ID 的 `Checking` 时立即交由受场景超时保护的业务调用确认；只有未返回服务 ID 时才在覆盖 5 秒探测上限的窗口内继续可取消等待。
- 业务请求已经显式携带服务 ID 时，先验证配置启用且用途匹配：
  - `Available`：直接执行。
  - `Unknown/Checking`：允许在该业务操作既有超时预算内真实调用；真实成功或基础设施失败刷新 readiness。
  - 新鲜 `Unavailable`：不调用，返回可解释降级原因。
- 显式服务不切换到其他候选，避免本地多模型显存驱逐和结果不一致。

保持仅增加前端轮询次数的方案未被采用，因为 API 调用、旧客户端以及检查完成后 TTL 再次过期仍会触发相同后端竞态。

### Decision 2: 结构化输出 Schema 由调用场景显式传递

- 要求 JSON 的非流式场景通过执行设置携带 JSON Schema。
- Ollama 原生适配器把 Schema 写入 `/api/chat` 的 `format` 字段；未要求结构化输出的普通或流式调用保持现状。
- 列语义召回 Schema 约束 `suggestions`、字段枚举、索引类型、置信度范围和禁止额外字段。
- Prompt 约束和现有解析、字段范围、业务门禁继续保留，Schema 只保证形状，不能替代语义校验。

### Decision 3: 仅对格式错误做一次有预算修正

- 只有模型已成功响应但未通过结构解析时，才允许同服务最多重试一次。
- 网络、认证、取消和超时不按格式重试；显式服务不回退其他服务。
- 重试共享列语义召回总超时预算，预算不足立即回退规则结果。

### Decision 4: 响应增加聚合 AI 执行摘要

识别响应增加可选 `aiAssist`：

- `requested`: 是否请求 AI 辅助。
- `status`: `applied | notNeeded | partial | fallback`。
- `reason`: `checkingTimeout | unavailable | invalidOutput | noApplicableSuggestion | timeout | callFailed | null`。
- `attemptedCalls`、`successfulCalls`、`fallbackCalls`、`elapsedMs`。

字段为加法兼容；旧客户端可忽略。前端在 `partial/fallback` 时明确提示规则结果仍可继续确认。

### Decision 5: 可观察性不记录原始模型输出

- 结构化日志记录场景、服务 ID、结果类别、耗时、尝试次数和 traceId。
- 非法输出只记录长度和哈希摘要，不记录表头、样例数据或模型原文。
- 指标使用低基数 outcome 标签，避免文件、客户或 traceId 进入指标标签。
- 上传、表格元数据读取和智能结构识别日志使用 `FileId` 关联，并记录阶段耗时与请求 `traceId`；不记录文件名或文档内容。

## Risks / Trade-offs

- `Checking` 时执行真实调用可能比快速回退更慢：由现有场景超时和取消限制，且只允许显式配置服务。
- Schema 可能暴露模型兼容差异：仅对 Ollama 原生路径先启用，其他供应商保持现有适配并由测试覆盖。
- 一次格式修正会增加最坏耗时：共享总预算且只在格式失败时发生。
- 新响应字段会增加少量契约复杂度：使用聚合摘要而非逐表大对象，并保持可选字段。

## Migration Plan

1. 先发布后端加法契约和 readiness/Schema 修复。
2. 再发布前端提示与等待策略；旧前端在过渡期仍可使用原响应。
3. 无数据库迁移；回退源码即可恢复原行为。

## Validation

- 单元测试：readiness 状态矩阵、显式候选过滤、Ollama 请求序列化、Schema 解析和单次修正预算。
- API 测试：AI 已应用、无需调用、部分应用、checking/unavailable/invalid/timeout 降级摘要。
- 前端测试：checking 恢复、等待超时、取消、partial/fallback 提示。
- 运行验证：同一真实文件重复识别，分别记录客户端、服务端、解析、映射和模型耗时；检查日志与响应状态一致。

## Open Questions

- 无。用户已批准按本设计实施；具体默认等待时长以现有 5 秒探测上限和测试结果确定。
