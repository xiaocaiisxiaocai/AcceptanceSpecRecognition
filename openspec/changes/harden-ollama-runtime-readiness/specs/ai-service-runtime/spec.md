## ADDED Requirements

### Requirement: Ollama 请求驻留策略一致

系统 SHALL 为 Ollama Chat 与 Embedding 请求使用同一个可配置的模型驻留策略，并 SHALL 允许部署将模型配置为永久驻留或有限时长驻留。

#### Scenario: Chat 与 Embedding 使用统一驻留值

- **GIVEN** 应用配置了 Ollama 驻留值
- **WHEN** 系统分别发送 Chat 与 Embedding 原生请求
- **THEN** 两类请求体均携带该驻留值
- **AND** 不再由 Chat 单独写死为 30 分钟

#### Scenario: 非 Ollama 服务不受影响

- **GIVEN** AI 服务类型为 OpenAI、Azure OpenAI、LM Studio 或其他 OpenAI 兼容服务
- **WHEN** 系统创建 Chat 或 Embedding 客户端
- **THEN** 系统继续使用该服务原有的连接器与超时语义

### Requirement: Ollama Embedding 使用原生运行时

系统 SHALL 通过 Ollama 原生 Embedding API 生成向量，并 SHALL 保持批量输入、调用取消与结果顺序。

#### Scenario: 批量输入返回等量向量

- **GIVEN** 系统向 Ollama 提交多条 Embedding 输入
- **WHEN** Ollama 成功返回向量
- **THEN** 系统按输入顺序返回相同数量的向量
- **AND** 每个向量保留远端返回的维度

#### Scenario: 返回数量不一致

- **GIVEN** Ollama 返回的向量数量与输入数量不一致
- **WHEN** 系统解析响应
- **THEN** 系统将本次调用视为失败
- **AND** 不向业务层返回错位向量

### Requirement: AI 就绪状态基于真实调用

系统 SHALL 仅在目标模型存在且完成对应用途的最小真实调用后，将 Ollama 服务报告为 available。

#### Scenario: 模型存在但 runner 无法完成调用

- **GIVEN** `/api/tags` 中存在已配置模型
- **AND** 模型加载、Chat 或 Embedding 调用失败或超时
- **WHEN** readiness 执行探测
- **THEN** 系统不得将该服务报告为 available
- **AND** 系统记录不包含输入、回复正文、向量或 ApiKey 的失败日志

#### Scenario: 真实调用成功

- **GIVEN** 已配置模型存在
- **WHEN** LLM 完成最小对话或 Embedding 返回非空向量
- **THEN** 系统将对应服务用途报告为 available

### Requirement: 应用启动后台预热

系统 SHALL 支持在应用启动后后台触发已启用优先 AI 服务的预热，并 SHALL 保证预热失败不阻断 HTTP 服务启动。

#### Scenario: 启用启动预热

- **GIVEN** 启动预热配置为启用
- **WHEN** 应用主机启动
- **THEN** 系统分别请求 LLM 与 Embedding 优先配置的 readiness 探测
- **AND** 复用既有 single-flight、并发上限与状态上报

#### Scenario: 预热失败

- **GIVEN** 远端 Ollama 不可用或模型加载超时
- **WHEN** 启动预热执行
- **THEN** API 进程继续启动并提供非 AI 功能
- **AND** 对应 AI 服务状态为 unavailable 或待后续重试

### Requirement: Ollama 运行时日志保护业务内容

系统 SHALL 记录模型加载与推理耗时等运行指标，并 SHALL 避免记录业务输入、模型回复正文、Embedding 数据和凭据。

#### Scenario: 记录成功调用指标

- **WHEN** Ollama Chat 或 Embedding 调用成功
- **THEN** 日志可包含服务标识、模型、总耗时、加载耗时与 token 计数
- **AND** 日志不包含提示词、回复正文、向量或 ApiKey
