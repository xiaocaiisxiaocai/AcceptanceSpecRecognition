# Change: 增加智能表头 LLM 裁决

## Why
当前智能表头识别已支持 Excel/Word、多行表头、客户学习词和候选评分，但本质仍是启发式规则。复杂合并、低关键词、候选分数接近或健康检查降级时，单靠规则仍可能选错表头行。

## What Changes
- 在 `/api/smart-config/recognize` 结构识别链路中增加按需表头裁决能力。
- 仅当规则表头识别不确定、结构健康检查需要确认或规则映射低置信时调用 LLM。
- LLM 只裁决 `headerRowIndex`、`headerRowCount`、`dataStartRowIndex` 等结构字段，不替代列映射主链路。
- 裁决结果必须通过边界校验和现有 `DocumentStructureHealthCheck`，否则保留规则结果并返回待确认。
- 复用现有单文档 LLM 调用预算，避免成本失控。

## Impact
- Affected specs: `api`
- Affected code:
  - `SmartConfigurationAppService`
  - LLM 结构裁决服务与模型
  - 智能识别 API 测试
  - Core 结构融合/健康检查相关测试
