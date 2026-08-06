# Change: 强化 Ollama 模型驻留与运行时就绪

## Why

当前 Ollama Chat 请求把 `keep_alive` 固定为 `30m`，Embedding 又通过 OpenAI 兼容接口调用，二者无法共享同一驻留策略。与此同时，后台 readiness 与 Ollama LLM 的完整连接测试只检查 `/api/tags`，模型文件存在就会被判定为可用，无法发现冷启动超时、runner 加载失败或真实推理不可用。

在模型位于机械硬盘时，首次 Embedding 加载曾耗时约 47 秒；上游客户端在 20 秒取消后，Ollama 记录 HTTP 499。虽然部署侧已将模型迁移到 NVMe 并让两个模型同时驻留，应用仍会用每次请求中的 `keep_alive=30m` 覆盖服务器默认值，因此需要从代码层统一驻留、预热和真实就绪语义。

## What Changes

- 为 Ollama Chat 与 Embedding 增加统一、可配置的 `keep_alive`，默认按本系统专用推理节点使用永久驻留。
- Ollama Embedding 改用原生 `/api/embed`，保留批量输入、向量维度与取消语义，并记录不含输入文本和向量内容的耗时指标。
- Ollama readiness 在确认模型已安装后执行最小真实调用，只有 runner 加载且调用成功才报告 available。
- 应用启动后可按配置触发 LLM 与 Embedding 的后台预热，预热失败只更新可用性并记录安全日志，不阻断 API 启动。
- AI 配置页的“快速测试”继续只检查可达性与模型可见性；“完整测试”对 Ollama LLM 也执行真实推理。
- 将 readiness 冷启动超时调整为可覆盖 NVMe 模型首次加载的明确配置，并保持业务请求自身的超时与取消边界不变。

## Impact

- Affected specs: `ai-service-runtime`（新增）、`user-interface`（连接测试语义）
- Affected code:
  - `AcceptanceSpecSystem.Core/AI/SemanticKernel`
  - `AcceptanceSpecSystem.Api/Services/AiServiceReadinessProbeScheduler.cs`
  - `AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs`
  - API 服务注册与 `appsettings.json`
  - Core/API 定向测试
- 不涉及数据库结构、API 路由或前端字段变更。
- 永久驻留会持续占用显存；部署前必须确认目标 GPU 能同时容纳启用的 LLM 与 Embedding 模型。当前 RTX 4090 实测两模型合计约 17.7 GiB，可用余量约 6.4 GiB。
