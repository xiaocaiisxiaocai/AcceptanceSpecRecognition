## ADDED Requirements

### Requirement: 智能填充当前最佳候选 AI 等价裁决门禁
系统 SHALL 在智能填充当前最佳候选中使用 AI 判断源项与候选项是否属于等价表达，而不是要求客户维护等价规则。

#### Scenario: 当前最佳候选触发 AI 等价裁决
- **GIVEN** 智能填充已通过 Embedding 召回并完成多阶段证据重排
- **AND** 最佳候选没有硬冲突
- **AND** 最佳候选最终得分大于等于 `0.6`
- **WHEN** 系统进入 AI 等价裁决门禁
- **THEN** 系统调用 AI 判断源项与候选项是否为等价表达
- **AND** 系统不要求客户维护符号、标点或等价表达规则

#### Scenario: AI 判断等价表达
- **GIVEN** 源项与候选项在换行、普通标点、符号表达或自然语言表达上不同
- **AND** AI 返回 `equivalent`
- **WHEN** 系统生成最终匹配结果
- **THEN** 系统 SHALL 不因该表现差异降低置信度
- **AND** 系统 SHALL 在结果中保留 AI 等价裁决说明

#### Scenario: AI 判断不同或不确定
- **GIVEN** AI 等价裁决返回 `different` 或 `uncertain`
- **WHEN** 系统生成最终匹配结果
- **THEN** 系统 SHALL 将该结果标记为需要人工确认
- **AND** 系统 SHALL 在结果中保留 AI 裁决原因

#### Scenario: AI 裁决失败回退
- **GIVEN** AI 等价裁决调用超时、失败或返回无法解析的结构
- **WHEN** 系统生成最终匹配结果
- **THEN** 系统 SHALL 将该结果按 `uncertain` 处理
- **AND** 系统 SHALL 默认进入人工确认

### Requirement: AI 等价裁决结构化输出
系统 SHALL 要求 AI 等价裁决返回固定结构，便于后端稳定解析和前端展示。

#### Scenario: 返回固定 JSON
- **WHEN** 系统调用 AI 等价裁决
- **THEN** AI 输出 SHALL 包含 `verdict`
- **AND** AI 输出 SHALL 包含 `reasonType`
- **AND** AI 输出 SHALL 包含中文 `reason`
- **AND** `verdict` 仅允许 `equivalent`、`different`、`uncertain`

#### Scenario: 等价表达原因分类
- **GIVEN** AI 判断源项与候选项语义等价
- **WHEN** 系统解析 AI 裁决结果
- **THEN** `reasonType` SHALL 支持 `format_only`、`punctuation_only`、`equivalent_expression` 或 `symbol_equivalent`
- **AND** 这些原因类型 SHALL 不单独触发置信度降低

#### Scenario: 差异原因分类
- **GIVEN** AI 判断源项与候选项存在语义差异或无法确认
- **WHEN** 系统解析 AI 裁决结果
- **THEN** `reasonType` SHALL 支持 `semantic_difference`、`symbol_conflict` 或 `uncertain`
- **AND** 这些原因类型 SHALL 使结果进入人工确认
