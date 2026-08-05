## ADDED Requirements

### Requirement: 智能结构识别显式服务执行不因检测中静默降级
系统 MUST 区分 AI 自动选择与业务请求显式指定服务的执行语义，不得把 `Unknown` 或 `Checking` 直接等同于服务不可用。

#### Scenario: 检测中服务执行真实有界调用
- **GIVEN** 智能结构识别请求明确启用 LLM 并携带有效的 LLM 服务 ID
- **AND** 该服务当前运行状态为 `Unknown` 或 `Checking`
- **WHEN** 系统进入列语义召回或结构裁决
- **THEN** 系统在场景超时预算内调用该显式服务
- **AND** 根据真实调用结果刷新对应用途的运行状态
- **AND** 不切换到其他 LLM 服务

#### Scenario: 已确认不可用时保守回退
- **GIVEN** 请求指定的 LLM 服务存在新鲜 `Unavailable` 运行状态
- **WHEN** 系统准备调用该服务
- **THEN** 系统不发起新的模型调用
- **AND** 保留规则识别结果
- **AND** 返回可解释的 AI 降级原因

### Requirement: 智能结构识别响应报告 AI 辅助执行结果
系统 SHALL 在智能结构识别响应中以兼容字段报告 AI 辅助是否真正执行和应用。

#### Scenario: AI 结果成功应用
- **GIVEN** 请求启用了 AI 辅助且至少一个 LLM 结果通过结构和业务校验
- **WHEN** API 返回识别结果
- **THEN** `aiAssist.status` 为 `applied` 或 `partial`
- **AND** 响应包含尝试、成功、回退次数和 AI 阶段耗时

#### Scenario: AI 未应用时明确返回原因
- **GIVEN** 请求启用了 AI 辅助
- **AND** LLM 因不可用、超时、调用失败或非法输出未被采用
- **WHEN** API 返回规则识别结果
- **THEN** `aiAssist.status` 为 `fallback` 或 `partial`
- **AND** `aiAssist.reason` 使用稳定、非敏感的原因编码
- **AND** API 不把规则结果伪装为 AI 已应用

#### Scenario: 规则明确无需调用 AI
- **GIVEN** 请求启用了 AI 辅助但规则识别已经满足确定性门禁
- **WHEN** API 返回识别结果
- **THEN** `aiAssist.status` 为 `notNeeded`
- **AND** 尝试调用次数为 0
