## Context
智能结构识别当前先通过规则检测表头，再进行列映射和健康检查。规则已经覆盖常见 Excel/Word 表头，但仍会遇到候选行分数接近、客户表头词不足、说明行与表头混杂、复杂多行合并等场景。

## Goals / Non-Goals
- Goals:
  - 仅在规则不确定时使用 LLM 裁决表头结构。
  - 保持规则识别为主路径，降低成本和不稳定性。
  - 裁决结果必须可校验、可回退。
- Non-Goals:
  - 不让 LLM 全量处理每张表。
  - 不让 LLM 直接决定最终自动采用。
  - 不新增前端交互。

## Decisions
- Decision: 将表头裁决纳入现有结构裁决链路，而不是新增独立外部接口。
  - Reason: 当前已经有 `ILlmDocumentStructureAdjudicationService`、预算、超时和融合模型，复用成本低。
- Decision: 触发条件采用保守组合。
  - 规则表头候选低置信或候选分差接近。
  - 规则识别结果经健康检查为 `NeedConfirm`。
  - 表头行数扩展不确定或列映射缺必选字段。
- Decision: 裁决结果先重提取表格，再执行列映射和健康检查。
  - Reason: 表头行变化会影响 headers、sample rows 和最终列映射。

## Risks / Trade-offs
- Risk: LLM 返回非法行号。
  - Mitigation: 校验索引范围、表头行数、数据起始行；非法则丢弃。
- Risk: 成本失控。
  - Mitigation: 复用 `MaxStructureAdjudicationCallsPerDocument`，预算为 0 时不调用。
- Risk: LLM 覆盖正确规则结果。
  - Mitigation: 仅在规则不确定或健康检查降级时调用；健康检查仍是最终门禁。

## Test Plan
- 规则明确时不调用 LLM。
- 规则低置信或冲突时调用 LLM。
- LLM 返回合法表头结构时重新提取并融合成功。
- LLM 返回非法行号时保留规则结果并返回 `NeedConfirm`。
- 预算为 0 时不调用 LLM。
