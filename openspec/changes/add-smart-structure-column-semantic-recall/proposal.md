# Change: 增加智能结构列语义召回评估

## Why
阶段 1 和阶段 2 已经把表头缺口先收敛到统计和确定性规则补齐。仍可能存在低频、跨语言或无共享字面的表头别名，无法稳定通过 `Equals / Contains / Regex / Levenshtein` 覆盖。

## What Changes
- 在智能结构识别链路中新增可选的“列语义召回”评估能力。
- 仅当确定性列映射缺少关键字段或健康检查需要确认时，对未映射表头生成候选字段建议。
- 第一版采用 LLM 列语义裁决，不引入 Embedding 字段向量库。
- 语义召回结果只作为建议和确认辅助，不得单独触发自动采用。
- 复用现有 LLM 调用配置、预算、超时和失败回退策略。

## Impact
- Affected specs: `api`, `matching-engine`
- Affected code:
  - `SmartConfigurationAppService`
  - `RuleBasedMappingStrategy` / 列映射结果模型
  - LLM 结构裁决或新增列语义裁决服务
  - 智能结构识别 API 响应模型
  - Core/API 回归测试
