## MODIFIED Requirements

### Requirement: 候选结果排序与Top-N
系统 SHALL 使用统一的多阶段证据驱动流程对召回候选进行排序与决策。

#### Scenario: 先召回再基于证据决策
- **GIVEN** 候选库中存在多个语义接近的候选
- **WHEN** 系统执行匹配
- **THEN** 系统先按 Embedding 得分召回 TopK 候选
- **AND** 对这些候选生成结构化证据
- **AND** 在非精确一致直达场景下，当实际召回候选数大于 1 时，系统允许 AI 在 TopK 候选中改选当前最佳候选
- **AND** AI 改选后的当前最佳候选仍需通过 AI 等价裁决门禁

### Requirement: AI 等价裁决门禁
系统 SHALL 对最终选中的当前最佳候选执行 AI 等价裁决，并以该裁决作为自动采用前的服务端门禁。

#### Scenario: AI 改选后的当前最佳进入等价裁决
- **GIVEN** 系统已对 TopK 候选执行 AI 重排
- **AND** AI 从候选集中改选出新的当前最佳候选
- **WHEN** 系统完成服务端重排
- **THEN** 系统对 AI 改选后的当前最佳候选执行 AI 等价裁决
- **AND** 不得继续对旧的本地 Top1 执行门禁后直接沿用

#### Scenario: AI 重排失败时回退本地 Top1
- **GIVEN** 系统已召回多个候选
- **AND** AI 重排调用失败、超时、解析失败或返回非法 SpecId
- **WHEN** 系统生成最终当前最佳候选
- **THEN** 系统回退为本地 Top1
- **AND** 不阻断后续预览结果生成
