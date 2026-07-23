# Project Audit Remediation Batch 1 Design

## 1. 目标

本批次修复审核报告中的 P1-02、P1-03、P1-05、P1-06：

- 客户、制程、机型选项必须完整加载，不再因后端 `pageSize <= 200` 的限制静默漏项。
- 删除、批删、恢复和重置操作只忽略用户主动取消，真实请求错误必须可见。
- 文档解析失败不得继续伪装成合法空结果。
- Production 继续关闭 Swagger，部署文档统一使用健康检查端点验收。

本批次不新增主数据远程搜索接口，不处理 P1-04 架构迁移，也不改变 Production Swagger 策略。

## 2. 总体方案

采用“共享能力 + 局部替换”：为全分页选项和确认框取消判断各提供一个小型、无 UI 状态的共享 helper，页面只负责 loading、消息和赋值。文档解析沿用现有 `ApplicationServiceException` 错误映射，不引入新的结果类型。部署部分只修正文档和自动化守卫。

相比逐页复制循环和 `catch` 判断，该方案能统一行为并提供直接单元测试；相比新增 options/search API，它不扩大后端契约和本批次范围。

## 3. 全分页选项

### 3.1 共享加载器

在 `web/src/utils` 增加通用分页加载器，接收分页请求回调、可选 `AbortSignal`、去重键和最大页数。固定使用后端允许的最大 `pageSize = 200`，从第 1 页开始顺序请求。

加载器遵循以下契约：

1. 首次响应提供 `totalPages`，后续按实际响应更新终止页，但不得超过默认最大 1000 页。
2. `total = 0`、`totalPages = 0` 且 `items` 为空表示合法空集合；除此之外，`totalPages < 1` 或响应页码与请求页码不一致均视为分页契约错误。
3. 到达 `totalPages` 后停止；若非首页响应为空，也立即停止并报错，避免静默返回不完整数据。
4. 按实体 ID 去重，保留第一次出现的位置，最终顺序与后端分页顺序一致。
5. 每页请求前检查 `AbortSignal`；取消异常原样向上传递，调用方不得用旧请求覆盖新状态。
6. API 返回非成功业务码时抛出包含后端消息的错误，不返回部分结果。
7. 分页元数据非法或超过最大页数时明确失败，不静默返回不完整数据。

客户、制程、机型列表 API 增加可选请求配置以传递 `signal`，不改变现有调用方式和响应结构。

### 3.2 接入范围

以下入口统一改用共享加载器：

- `web/src/views/smart-fill/index.vue`
- `web/src/views/smart-fill/components/MatchConfig.vue`
- `web/src/views/data-import/composables/useDataImportTarget.ts`

页面继续维护各自的 loading 和错误消息。组件卸载或新一轮加载开始时取消旧请求，只有当前请求可以写入选项状态。

## 4. 删除与恢复错误

### 4.1 取消判断

在 `web/src/utils` 增加 `isMessageBoxCancel(error)`。它只在错误值严格等于 Element Plus 的 `"cancel"` 或 `"close"` 时返回 `true`，不把 Axios 取消、网络断开、超时或未知异常视为用户取消。

确认框和 API 请求可以继续放在同一个 `try` 中，但每个 `catch (error)` 必须：

1. 用户主动取消时直接返回，不显示错误。
2. 其他异常使用现有 `getRequestErrorMessage(error, fallback)` 显示真实错误。
3. 请求失败时不显示成功消息，不清空选择，也不刷新为假成功状态。

### 4.2 替换范围

替换审核确认的单删、批删、恢复和重置路径：

- 客户、制程、机型管理
- 验收规格表格
- 系统用户
- 智能结构路由规则
- 提示词模板
- 角色管理
- 列映射规则
- AI 服务配置

不抽取通用 `confirmAndRun`，避免把不同权限检查、成功状态和刷新动作塞入一个高耦合流程。

## 5. 文档解析失败

### 5.1 分类规则

`DocumentTableAccessService` 对匹配来源和批量回复来源采用一致分类：

- 解析器明确不可用、目标表不存在或表结构合法但没有可用来源行：返回空集合。
- `OperationCanceledException`：原样抛出，保留请求取消语义。
- 文件损坏、I/O、权限、解析器内部错误及其他未知异常：记录结构化错误并抛出 `ApplicationServiceException`，不返回空集合。

用户消息使用稳定、可操作的文本，例如“文档解析失败，请确认文件完整且未被占用”。内部日志只记录文件 ID、文件类型、表索引、异常类型和异常堆栈，不记录文件名、单元格内容或正文。

### 5.2 实现边界

为 `DocumentTableAccessService` 注入 `ILogger<DocumentTableAccessService>`，将当前无类型 `catch { return []; }` 替换为明确分支。匹配来源和批量回复来源复用同一个私有异常转换方法，避免两套错误语义漂移。

不在本批次引入 `Empty/Unsupported/InvalidDocument/Failed` 结果联合类型；现有调用链已经能处理空集合与 `ApplicationServiceException`，保持改动局部。

## 6. Production 部署文档

保持 `Program.cs` 仅在 Development 启用 Swagger，不新增生产开关。

- `docs/DEPLOY-DOCKER.md` 删除 Production `/swagger` 可访问说明，只保留 `/health` 验收。
- `docs/DEPLOY-IIS.md` 删除地址概览和验收步骤中的 `/api/swagger`，以 `/api/health` 作为 API 启动判据。
- `docs/DEPLOY-WINDOWS-DOCKER.md` 已使用健康检查，增加守卫确保不回退到 Swagger 验收。

## 7. 测试策略

### 7.1 前端

- 分页加载器单元测试覆盖 250 条以上数据、跨页顺序、重复 ID、空页、非法 `totalPages`、最大页数和取消。
- 取消判断单元测试覆盖 `cancel`、`close`、403、500、网络错误和 Axios 取消。
- 页面接入测试或源码守卫确认三个选项入口不再直接写死第一页大 pageSize，并统一调用共享加载器。
- 删除/恢复源码守卫确认审核列出的路径使用取消 helper 和 `getRequestErrorMessage`。
- 运行 `pnpm test` 与 `pnpm typecheck`。

### 7.2 后端与文档

- 为文档访问服务增加合法空表、目标表不存在、取消和解析异常测试；解析异常必须产生业务错误而不是空集合。
- 增加部署文档守卫，确认 Production 文档不再把 Swagger 作为验收入口，健康检查端点保持一致。
- 运行后端定向测试、`dotnet test AcceptanceSpecSystem.sln -c Release --no-build --no-restore -m:1` 和 warnings-as-errors Release 构建。

## 8. 提交边界

按四个主题独立提交：

1. 全分页选项加载器及三个入口接入。
2. 删除、恢复和重置错误可见性。
3. 文档解析异常分类。
4. Production 部署文档与批次状态记录。

每个主题先建立失败测试，再实施最小修复；不顺带处理 P1-04 或批次 2 问题。
