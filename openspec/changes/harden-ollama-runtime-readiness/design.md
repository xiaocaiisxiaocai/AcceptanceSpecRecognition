## Context

Ollama 有两类容易混淆的状态：模型文件已安装，以及模型 runner 已加载并能完成调用。`GET /api/tags` 只能证明前者。原实现将该结果同时用于后台 readiness 和 Ollama LLM 完整连接测试，因而会在首次真实请求才暴露冷启动成本或加载失败。

Chat 已使用原生 `/api/chat`，但请求固定携带 `keep_alive=30m`；Embedding 使用 `/v1/embeddings`，没有应用级驻留参数和 Ollama 原生耗时字段。服务器全局 `OLLAMA_KEEP_ALIVE=-1` 会被 Chat 的请求级参数覆盖，造成两个模型生命周期不一致。

## Goals / Non-Goals

- Goals:
  - Chat 与 Embedding 使用一致的可配置驻留策略。
  - readiness 的 available 表示最小真实调用成功，而不仅是模型可见。
  - 应用启动后在后台预热优先级最高的启用模型，降低第一位用户承担冷启动的概率。
  - 记录模型加载与推理耗时，但不记录提示词、响应正文或向量。
  - 保留快速测试的低成本语义和现有业务超时/降级机制。
- Non-Goals:
  - 不自动修改远端 Ollama 环境变量、模型文件或 GPU 调度参数。
  - 不保证任意硬件都能同时驻留所有已启用模型。
  - 不把 AI 模型状态加入 `/health/ready` 的硬启动门禁。
  - 不改变 AI 配置数据库结构或前端表单。

## Decisions

### 1. 原生 Ollama 适配器共享驻留配置

在 `SemanticKernelOptions` 中增加 `OllamaKeepAlive`。Chat 与新的原生 Embedding 适配器都在请求体中传递该值。应用默认配置使用 `-1`，明确表示在专用推理节点永久驻留；运维可改为 `30m` 等时长以换取显存释放。

Embedding 直接调用 `/api/embed`，将返回的每个浮点数组映射为 `Embedding<float>`。适配器必须校验返回数量与输入数量一致，并通过既有安全 HttpClient 工厂创建连接。

### 2. 就绪探测分为模型存在检查与最小真实调用

Ollama 探测先调用 `/api/tags` 提供明确的“模型未安装”失败，再通过统一工厂执行：

- LLM：发送最小 `ping` 对话并等待非流式响应；
- Embedding：生成 `ping` 向量并校验非空。

探测沿用有界队列、同服务/用途 single-flight、全局并发上限和取消逻辑。冷启动超时由 `AiServiceReadiness:ProbeTimeoutSeconds` 配置，默认值提高到能覆盖当前 NVMe 实测冷启动，但不得替代业务调用自己的超时。

### 3. 启动预热复用 readiness 调度

新增后台启动预热服务，在 HTTP 主机启动后创建作用域，分别请求 LLM 与 Embedding 的优先配置选择，从而复用 readiness registry、探测去重和并发控制。预热为软依赖：数据库未就绪、未配置模型或远端调用失败均不得阻断进程启动。

通过 `AiServiceReadiness:PreloadOnStartup` 控制是否启用，默认启用。测试环境可覆盖关闭，避免测试主机意外访问外部 AI 服务。

### 4. 快速测试与完整测试保持不同成本

- 快速测试：仅验证 Endpoint 可达且目标模型存在，适合配置编辑时快速反馈。
- 完整测试：通过统一工厂执行真实 Chat/Embedding 调用，返回总耗时和向量维度等安全摘要。

完整测试继续使用 `AiServiceTest` 的独立超时，避免 readiness 的短探测窗口改变管理员主动测试的行为。

### 5. 可观测性不包含业务内容

原生适配器记录服务 ID、模型、总耗时、加载耗时、prompt/eval 计数等结构化字段。禁止记录输入文本、模型回复正文、Embedding 数组和 ApiKey。

## Risks / Trade-offs

- 永久驻留提高显存占用。通过配置可恢复有限时长，部署验收必须检查 `ollama ps` 与 `nvidia-smi`。
- 启动预热可能在应用刚启动时产生 GPU/磁盘负载，但在后台执行且不阻断 HTTP 服务。
- readiness 的真实调用比 `/api/tags` 更慢，但结果语义更可靠；有界队列、TTL 和 single-flight 防止无界探测。
- 原生 Embedding 路径与 OpenAI 兼容路径不同，需要用批量数量、维度和取消测试防止行为回归。

## Migration Plan

1. 发布代码与配置，但不删除原 Ollama 模型备份目录。
2. 启动后检查日志中的两个预热结果、`ollama ps` 驻留状态及 GPU 余量。
3. 分别执行一次完整 LLM/Embedding 连接测试，再执行真实识别业务路径。
4. 若显存不足，将 `SemanticKernel:OllamaKeepAlive` 调整为有限时长或关闭启动预热并重启应用。

## Open Questions

- 无。当前部署已验证两个目标模型能同时完全驻留于 RTX 4090。
