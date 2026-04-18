# Design: 智能填充当前最佳候选 AI 等价裁决门禁

## Context
现有智能填充以 Embedding 召回、多阶段证据重排和门禁决策为主。换行、空格、普通标点、等价表达差异会在前端详情中形成“差异”，并可能让用户感知为中风险。客户希望不再维护规则，充分利用 AI 判断文本是否等价。

## Goals
- 客户无需维护等价符号、表达方式或标点规则。
- 对达到中置信门槛且无硬冲突的当前最佳候选固定触发 AI，避免继续依赖旧的分数直通逻辑。
- AI 判断等价时不降低置信度，同时前端保留可解释提示。
- AI 判断不同或不确定时，默认人工确认。
- 前后端都展示裁决结果，避免只在后端改变分数。

## Non-Goals
- 不替换第一阶段 Embedding 召回。
- 不让 AI 全量处理所有匹配行。
- 不取消系统的超时、JSON 校验和失败回退护栏。
- 不要求用户维护规则词表或符号映射。

## Backend Design
新增一个 AI 等价裁决模型，挂到匹配结果和候选快照中：

- `LlmEquivalenceVerdict`：`Equivalent`、`Different`、`Uncertain`。
- `LlmEquivalenceReasonType`：`FormatOnly`、`PunctuationOnly`、`EquivalentExpression`、`SymbolEquivalent`、`SemanticDifference`、`SymbolConflict`、`Uncertain`。
- `LlmEquivalenceResult`：包含结论、原因类型、简短说明、是否经过 AI 裁决。

触发条件建议：

- 当前最佳候选没有硬冲突。
- 当前最佳候选最终得分达到中置信门槛（`>= 0.6`）。
- AI 等价裁决作为固定服务端门禁，不再提供单独开关。

裁决流程：

1. 正常执行 Embedding 召回与多阶段证据重排。
2. 对满足门禁条件的 Top1 候选调用 AI 等价裁决 Prompt。
3. 要求 AI 返回严格 JSON。
4. `equivalent`：不因文本表现差异降低置信度，保留提示。
5. `different`：保持或转为人工确认。
6. `uncertain`、超时、解析失败：转人工确认。

## Prompt Contract
Prompt 输入：

- 源项目、源规格。
- 候选项目、候选规格。
- 当前证据摘要、冲突状态、分数明细。
- 要求 AI 只判断文本表达关系，不编造业务事实。

Prompt 输出：

```json
{
  "verdict": "equivalent | different | uncertain",
  "reasonType": "format_only | punctuation_only | equivalent_expression | symbol_equivalent | semantic_difference | symbol_conflict | uncertain",
  "reason": "不超过80字的中文原因",
  "confidence": 0.0
}
```

## Frontend Design
智能填充预览和详情需要同步展示 AI 裁决：

- 详情页新增“AI 等价裁决”标签或说明。
- `format_only`、`punctuation_only`、`equivalent_expression`、`symbol_equivalent` 显示为提示，不推动风险变中。
- `semantic_difference`、`symbol_conflict`、`uncertain` 显示为需确认。
- 差异高亮保留原文细节，但要区分“仅提示差异”和“影响决策差异”。

## Error Handling
- AI 服务不可用、超时、JSON 无法解析：结果按 `uncertain` 处理，默认人工确认。
- AI 返回未知枚举：按 `uncertain` 处理。
- AI 裁决不覆盖硬冲突，硬冲突仍优先拒绝或人工确认。

## Testing
- 后端单元测试覆盖：等价表达不降置信度、不同/不确定进入人工确认、AI 失败回退人工确认。
- 前端测试覆盖：等价表达提示展示、普通标点差异不导致中风险、语义差异仍需确认。
- 冒烟验证：智能填充预览、详情弹窗、确认填充流程。
