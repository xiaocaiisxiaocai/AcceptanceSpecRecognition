## Context

`IsDisabled` 表达管理员是否允许使用配置，不能承担网络端点的瞬时健康状态。若把一次超时直接写回禁用，会把暂时故障变成持久配置变更；若完全不探测，则自动选择会继续等待不可用服务。上传链路也需要把客户端传输与服务端文档解析拆成不同可观测阶段。

## Goals / Non-Goals

### Goals

- 业务页面只自动采用已启用且当前可用的对应用途服务。
- 配置页返回后不要求整页刷新。
- 上传期间提供真实进度、取消和阶段反馈。

### Non-Goals

- 不因单次超时永久禁用 AI 配置。
- 不让 `/health` 请求同步探测所有模型端点。
- 不让前端重新暴露模型选择器。
- 不宣称“停止等待”一定终止第三方同步解析内部不可取消的单次调用。

## Decisions

### Decision 1: 提供最小自动选择 API

新增 `GET /api/ai-services/selection?purpose=llm|embedding`，只返回自动选择所需的服务 ID、显示名称、模型、状态、检查时间和脱敏消息，不返回 Endpoint 或 API Key。候选仅来自用途匹配且未禁用的配置，按既有优先级选择第一个 ready 服务。

### Decision 2: 运行状态为短期缓存

运行状态按“服务 + 用途”保存 `unknown/probing/ready/unavailable`，通过短 TTL 和有界并发轻量探测刷新。配置新增、修改、启禁用、人工连接测试和真实调用成功/失败会更新或失效对应状态。状态不写入 `IsDisabled`；连续确认不可用是否永久禁用仍由管理员操作和现有完整测试流程决定。

### Decision 3: readiness 与 liveness 分离

数据库和文件存储决定核心 readiness。AI 状态作为组件级可用性返回：规则识别仍可运行时报告 degraded；需要 Embedding 的匹配流程没有 ready 服务时继续显式阻断。探针读取缓存，不在健康请求内发起外部网络调用。

### Decision 4: keep-alive 激活时刷新选择

共享 AI 控件使用 `onActivated` 加载 selection，避免只依赖 `onMounted`。`checking` 时阻止发起 AI 识别并显示检测中；`unavailable` 时关闭并禁用 AI 开关，说明规则识别仍可用；`available` 时默认开启并提交服务 ID。无权配置的用户不显示越权入口。

### Decision 5: 上传 Promise、进度与取消由父页面持有

上传 API 接收 `signal` 和 `onProgress`。共享上传组件接收真实 request Promise，在 Promise 完成前保持 loading，并通过事件向父页面请求取消。网络完成后页面转入 `processing`，显示“文件已上传，正在解析结构”；取消不显示错误 Toast，也不写入已上传文件状态。

## Risks / Trade-offs

- 内存 readiness 在多副本间可能短暂不一致；本次不引入分布式缓存，每个副本独立探测并以真实调用结果纠正。
- 后台探测会产生少量外部请求，必须配置周期、超时、抖动和并发上限。
- 浏览器上传进度在无法获知总长度时只能显示已传字节和不确定进度状态。

## Rollback Plan

保留现有列表和连接测试接口。若 selection 端点需要回退，页面可暂时回到列表读取，但不得恢复“已启用即健康”文案；上传选项为可选参数，旧调用保持可用。
