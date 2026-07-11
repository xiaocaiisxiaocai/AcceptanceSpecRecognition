## ADDED Requirements

### Requirement: 智能填充界面展示 AI 等价裁决
系统 SHALL 在智能填充预览和详情中展示 AI 对当前最佳候选的等价裁决结果。

#### Scenario: 展示等价表达提示
- **GIVEN** 某行匹配结果包含 AI 等价裁决
- **AND** 裁决结果为 `equivalent`
- **WHEN** 用户查看智能填充预览或详情弹窗
- **THEN** 页面 SHALL 展示“AI 判断为等价表达”或对应原因提示
- **AND** 页面 SHALL 不因该提示将风险级别提升为中风险

#### Scenario: 展示普通标点或格式差异
- **GIVEN** AI 裁决原因类型为 `format_only` 或 `punctuation_only`
- **WHEN** 用户查看详情弹窗
- **THEN** 页面 SHALL 保留原文差异展示
- **AND** 页面 SHALL 将该差异标记为提示型差异，而不是决策型风险

#### Scenario: 展示不同或不确定
- **GIVEN** AI 裁决结果为 `different` 或 `uncertain`
- **WHEN** 用户查看智能填充预览或详情弹窗
- **THEN** 页面 SHALL 展示需要人工确认的原因
- **AND** 页面 SHALL 阻止用户在未确认状态下直接自动采用该行

### Requirement: 智能填充差异展示区分提示型与决策型差异
系统 SHALL 在智能填充详情中区分仅用于提示的格式/标点差异和影响决策的语义差异。

#### Scenario: 提示型差异不影响风险
- **GIVEN** 源项与推荐项只有换行、普通标点或 AI 认定的等价表达差异
- **WHEN** 页面生成一句话结论、建议动作和风险级别
- **THEN** 页面 SHALL 保持低风险或原有高置信判断
- **AND** 页面 SHALL 展示提示型差异说明

#### Scenario: 决策型差异影响风险
- **GIVEN** 源项与推荐项存在 AI 认定的语义差异、符号冲突或不确定结论
- **WHEN** 页面生成一句话结论、建议动作和风险级别
- **THEN** 页面 SHALL 显示需要人工确认
- **AND** 页面 SHALL 将该差异作为决策型风险展示
