## 1. Implementation
- [x] 1.1 为表头检测输出候选置信信息，供调用方判断是否不确定。
- [x] 1.2 在智能识别链路中定义 LLM 表头裁决触发条件。
- [x] 1.3 复用现有结构裁决服务，允许 LLM 返回表头结构字段。
- [x] 1.4 校验 LLM 表头结构并按裁决结果重新提取 Word/Excel 表格。
- [x] 1.5 重新执行列映射、结构融合和健康检查，非法或低质量裁决回退规则结果。
- [x] 1.6 保持 `MaxStructureAdjudicationCallsPerDocument` 预算和超时生效。

## 2. Tests
- [x] 2.1 规则明确时不调用 LLM。
- [x] 2.2 规则不确定时调用 LLM 并采用合法表头结构。
- [x] 2.3 LLM 返回非法行号时回退 `NeedConfirm`。
- [x] 2.4 LLM 预算为 0 时不调用。
- [x] 2.5 覆盖 Excel 与 Word 各至少一个裁决样本。
