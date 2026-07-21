## ADDED Requirements

### Requirement: AI 服务启用状态与运行可用性分离

系统 MUST 将管理员配置的启用状态与 LLM、Embedding 各自的短期运行可用性分开管理，自动选择只采用同时启用且可用的服务。

#### Scenario: 自动选择可用服务

- **WHEN** 客户端按用途请求自动选择结果
- **THEN** 系统在未禁用且用途匹配的配置中按优先级返回第一个运行可用服务
- **AND** 响应不包含 API Key 或完整 Endpoint

#### Scenario: 所有候选暂时不可用

- **WHEN** 对应用途没有运行可用的已启用服务
- **THEN** 系统返回 `unavailable` 状态和脱敏说明
- **AND** 不因该结果永久修改配置的 `IsDisabled`

#### Scenario: 服务仍在检测

- **WHEN** 候选服务尚未完成轻量探测
- **THEN** 系统返回 `checking` 状态
- **AND** 不把未知状态提前当作可用

#### Scenario: LLM 与 Embedding 独立

- **GIVEN** 同一配置或不同配置分别提供 LLM 与 Embedding
- **WHEN** 其中一种用途探测失败
- **THEN** 系统只把对应用途标记为不可用
- **AND** 不覆盖另一用途的独立状态

### Requirement: AI 可用性探测不阻塞健康端点

系统 MUST 通过有界、缓存的探测维护 AI 组件状态，健康端点不得同步遍历并调用全部外部模型服务。

#### Scenario: 查询系统健康状态

- **WHEN** 运维系统调用健康端点
- **THEN** 系统读取最近的 AI 组件状态并返回整体与组件级结果
- **AND** 不在本次健康请求中发起无界外部模型调用

#### Scenario: 可选 AI 不可用

- **WHEN** LLM 不可用但规则识别和核心依赖仍正常
- **THEN** 系统将 AI 能力表达为 degraded 或 unavailable
- **AND** 不把核心 API liveness 误报为进程死亡
