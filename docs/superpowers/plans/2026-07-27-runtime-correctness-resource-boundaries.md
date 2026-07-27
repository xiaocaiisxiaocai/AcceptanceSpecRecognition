# 运行时正确性与资源边界加固 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复已确认的审计、异步竞态、并发缓存、文件一致性、资源预算、AI 端点安全和质量门禁问题，同时保持现有匹配语义与数据兼容。

**Architecture:** 使用 OpenSpec 变更 `harden-runtime-correctness-and-resource-boundaries` 作为唯一规范来源，按审计/API、前端流程、后端资源安全、质量治理四个可回滚切片实施。每项修复先建立可复现的失败测试，再做最小实现；跨数据库与浏览器行为分别由真实 MySQL 契约和定向 Playwright 验证。

**Tech Stack:** ASP.NET Core 8、EF Core 8、Pomelo MySQL 8、Vue 3、TypeScript、Vite、Element Plus、Vitest、Node Test Runner、Playwright、OpenSpec。

## Global Constraints

- 开始业务代码前使用 `superpowers:using-git-worktrees` 检查隔离方案；未经用户同意不新建 worktree。
- 所有代码任务遵循 RED → 确认预期失败 → GREEN → 定向回归。
- 重复分析默认上限：2,000 个候选、1,000,000 次近似比较。
- 文件比较默认上限：单文件 50 MiB、1,000,000 个单元格或等价节点、100,000 条差异。
- `postcss` 最低版本为 `8.5.18`，`brace-expansion` 最低版本为 `5.0.8`。
- 用户密码长度在后端、前端、部署脚本和文档中统一为 4～200。
- 上传工作区的文件操作统一使用“移出当前流程”语义，不调用持久文件删除 API。
- Ollama、LM Studio 可按明确策略访问本机或私网；公网 AI 提供商不得复用该例外。
- UI、错误信息、配置说明和测试名称使用中文业务语义。
- 不改变 Embedding 主匹配、LLM 辅助裁决和文件级 SmartFill 统一确认业务规则。
- 不归档缺少真实目标环境发布证据的既有 OpenSpec 变更。
- 未经用户再次明确授权，不推送远端、不合并到 `main`、不执行生产部署。

## 实施顺序与提交边界

| 切片 | 任务 | 可独立回滚结果 |
|---|---|---|
| A 审计/API | 1–2 | 最终状态审计、真实 HTTP 状态和稳定异常响应 |
| B 前端流程 | 3–6 | 执行历史、语义搜索、批量回复、SmartFill 生命周期和移出语义 |
| C 后端资源安全 | 7–12 | Embedding 并发、文件删除、重复分析、文件比较、SSRF、取消/批量边界 |
| D 质量治理 | 13–14 | 依赖锁定、脆弱测试替换、全量验证与 OpenSpec 证据 |

---

### Task 1: 最终状态审计与独立持久化作用域

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/AuditOperationAttribute.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/AuditLogsTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`

**Interfaces:**
- Consumes: `IAuditTrailAppService.WriteAsync(AuditTrailWriteCommand, CancellationToken)`
- Produces: `AuditOperationFilter : IAsyncActionFilter, IAsyncAlwaysRunResultFilter, IAsyncExceptionFilter`
- Produces: 每个带 `[AuditOperation]` 的请求最多一条、使用最终 HTTP 状态的审计记录

- [ ] **Step 1: 编写 409、500 和独立作用域失败测试**

在 `AuditLogsTests.cs` 增加集成测试，使用唯一 `X-Client-Trace-Id` 定位审计：

```csharp
[Fact]
public async Task AuditedConflict_ShouldPersistFinal409WithoutReplayingFailedBusinessEntity()
{
    var traceId = $"audit-conflict-{Guid.NewGuid():N}";
    using var request = BuildStaleAiConfigUpdateRequest(traceId);

    using var response = await _client.SendAsync(request);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var audit = await FindAuditByTraceIdAsync(traceId);
    audit.GetProperty("statusCode").GetInt32().Should().Be(409);
    audit.GetProperty("level").GetString().Should().Be("Warning");
}
```

增加一个替换 `IAuditTrailAppService` 的测试工厂，使 `WriteAsync` 抛出异常，并断言原始 409/500 响应不被审计异常覆盖。

- [ ] **Step 2: 运行测试并确认 RED**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AuditLogsTests|FullyQualifiedName~ConfigApisTests.AiServiceConfig_StaleRowVersion_ShouldReturnConflict" -m:1
```

Expected: 409 业务断言通过，但审计仍记录 200 或审计写入重复触发业务并发异常。

- [ ] **Step 3: 分离采集、结果和异常阶段**

在 `AuditOperationAttribute.cs` 内增加请求状态对象，并用 `HttpContext.Items` 防止重复写入：

```csharp
internal sealed record AuditOperationState(
    AuditOperationAttribute Attribute,
    string Controller,
    string? Action,
    IReadOnlyDictionary<string, string?> RouteValues,
    string? Username,
    long StartedTimestamp);
```

过滤器规则：

```csharp
public sealed class AuditOperationFilter :
    IAsyncActionFilter,
    IAsyncAlwaysRunResultFilter,
    IAsyncExceptionFilter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        var executed = await next();
        await TryWriteOnceAsync(
            context.HttpContext,
            context.HttpContext.Response.StatusCode,
            executed.Exception,
            context.HttpContext.RequestAborted);
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        await TryWriteOnceAsync(
            context.HttpContext,
            StatusCodes.Status500InternalServerError,
            context.Exception,
            CancellationToken.None);
    }
}
```

`TryWriteOnceAsync` 必须创建新作用域：

```csharp
await using var scope = _scopeFactory.CreateAsyncScope();
var auditTrail = scope.ServiceProvider.GetRequiredService<IAuditTrailAppService>();
await auditTrail.WriteAsync(command, cancellationToken);
```

审计异常只调用 `_logger.LogWarning`，不得抛回 MVC 管道。

- [ ] **Step 4: 运行审计定向测试并确认 GREEN**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AuditLogsTests|FullyQualifiedName~ConfigApisTests.AiServiceConfig_StaleRowVersion_ShouldReturnConflict" -m:1
```

Expected: 全部通过，测试输出不再出现同一业务实体被审计保存重放的 `DbUpdateConcurrencyException`。

- [ ] **Step 5: 提交审计修复**

```powershell
git add src/AcceptanceSpecSystem.Api/Controllers/AuditOperationAttribute.cs src/AcceptanceSpecSystem.Api/Program.cs tests/AcceptanceSpecSystem.Api.Tests/AuditLogsTests.cs tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs
git diff --cached --check
git commit -m "fix: 修正控制器审计最终状态"
```

### Task 2: 真实 HTTP 状态与稳定异常响应

**Files:**
- Create: `src/AcceptanceSpecSystem.Api/Models/ApiHttpStatusMapper.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Models/ApiResponse.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/BaseApiController.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Middleware/ExceptionHandlingMiddleware.cs`
- Modify: controllers returned by `rg -l "Error<|return Error\\(" src/AcceptanceSpecSystem.Api/Controllers`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BaseApiControllerTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExceptionHandlingMiddlewareTests.cs`

**Interfaces:**
- Produces: `ApiHttpStatusMapper.Resolve(int code) : int`
- Produces: `ApiResponse.TraceId`
- Consumes: `RequestTracingMiddleware.TraceIdItemKey`

- [ ] **Step 1: 编写状态映射和未知异常失败测试**

```csharp
[Theory]
[InlineData(404, StatusCodes.Status404NotFound)]
[InlineData(409, StatusCodes.Status409Conflict)]
[InlineData(422, StatusCodes.Status422UnprocessableEntity)]
[InlineData(429, StatusCodes.Status429TooManyRequests)]
[InlineData(500, StatusCodes.Status500InternalServerError)]
public void Resolve_ShouldMapStableErrorCode(int code, int expected)
{
    ApiHttpStatusMapper.Resolve(code).Should().Be(expected);
}
```

在 `ExceptionHandlingMiddlewareTests` 断言未知异常响应包含非空 `traceId`，但不包含异常类型、消息、SQL 或路径。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BaseApiControllerTests|FullyQualifiedName~ExceptionHandlingMiddlewareTests" -m:1
```

Expected: 映射器不存在，且未知异常响应没有 `traceId`。

- [ ] **Step 3: 实现统一状态映射**

```csharp
public static class ApiHttpStatusMapper
{
    public static int Resolve(int code) => code switch
    {
        401 => StatusCodes.Status401Unauthorized,
        403 => StatusCodes.Status403Forbidden,
        404 => StatusCodes.Status404NotFound,
        409 => StatusCodes.Status409Conflict,
        413 => StatusCodes.Status413PayloadTooLarge,
        422 => StatusCodes.Status422UnprocessableEntity,
        429 => StatusCodes.Status429TooManyRequests,
        >= 500 => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest
    };
}
```

`BaseApiController.Error` 使用：

```csharp
var status = ApiHttpStatusMapper.Resolve(code);
return StatusCode(status, ApiResponse.Error(code, message, ResolveTraceId()));
```

`ApiResponse<T>` 增加可空 `TraceId`，错误工厂显式赋值。中间件对 `ApplicationServiceException` 使用其代码映射；未知异常固定返回 `500 / 服务器内部错误，请稍后重试 / traceId`。

- [ ] **Step 4: 定向检查所有非 400 调用**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BaseApiControllerTests|FullyQualifiedName~ExceptionHandlingMiddlewareTests|FullyQualifiedName~ConfigApisTests" -m:1
rg -n "BadRequest\\(ApiResponse\\.Error\\((401|403|404|409|413|422|429|500)" src/AcceptanceSpecSystem.Api
```

Expected: 测试通过，搜索无结果；文件下载等 `IActionResult` 分支也使用统一映射。

- [ ] **Step 5: 提交 API 错误边界**

```powershell
git add src/AcceptanceSpecSystem.Api tests/AcceptanceSpecSystem.Api.Tests/BaseApiControllerTests.cs tests/AcceptanceSpecSystem.Api.Tests/ExceptionHandlingMiddlewareTests.cs
git diff --cached --check
git commit -m "fix: 统一API错误状态与异常响应"
```

### Task 3: 执行历史服务端分页、请求代次与完整行回放

**Files:**
- Create: `web/src/views/other/execution-history/useExecutionHistoryRequests.ts`
- Create: `web/src/views/other/execution-history/useExecutionHistoryRequests.test.ts`
- Create: `web/src/views/other/execution-history/components/ExecutionHistorySmartFillRowDetail.vue`
- Modify: `web/src/api/execution-history.ts`
- Modify: `web/src/views/other/execution-history/index.vue`
- Modify: `web/src/views/other/execution-history/components/ExecutionHistorySmartFillPlayback.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`
- Test: `web/e2e/execution-history-playback.spec.ts`

**Interfaces:**
- Produces: `createExecutionHistoryRequestGate()`
- Changes: `getExecutionHistoryDetail(id, signal?)`
- Changes: `getExecutionHistorySmartFillRow(id, params, signal?)`
- Consumes: existing `GET /api/execution-history/{id}/smart-fill/rows`

- [ ] **Step 1: 编写迟到响应和按需逐行读取失败测试**

```ts
it("任务 A 的迟到响应不能覆盖已选中的任务 B", async () => {
  const gate = createExecutionHistoryRequestGate();
  const a = gate.begin("detail:1");
  const b = gate.begin("detail:2");

  expect(a.isCurrent()).toBe(false);
  expect(b.isCurrent()).toBe(true);
  expect(a.signal.aborted).toBe(true);
});
```

API 集成测试确认归档行接口返回完整 `previewSnapshot.bestMatch`、证据和执行快照，而轻量详情仍可精简。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
pnpm --dir web vitest run src/views/other/execution-history/useExecutionHistoryRequests.test.ts
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~ExecutionHistoryApiTests" -m:1
```

Expected: 请求闸门文件不存在；完整行测试按当前夹具暴露缺失的归档字段或缺少取消入口。

- [ ] **Step 3: 实现分页和请求代次**

`index.vue` 使用 `page=1`、`pageSize=50` 的响应式分页状态，渲染 `[20, 50, 100, 200]` 选项。列表、详情各自使用请求闸门：

```ts
const request = detailGate.begin(`detail:${id}`);
const res = await getExecutionHistoryDetail(id, request.signal);
if (!request.isCurrent() || selectedTaskId.value !== id) return;
currentDetail.value = res.data;
```

卸载时调用 `listGate.cancel()` 和 `detailGate.cancel()`。

- [ ] **Step 4: 实现按需完整行详情和降级提示**

在回放表格行点击时，以 `recordId:fileIndex:sheetIndex:rowIndex` 为缓存键调用完整行 API。`ExecutionHistorySmartFillRowDetail.vue` 显示候选、评分证据、AI 裁决、人工覆盖、最终验收和备注。

当 `hasPlaybackArchive=true` 但行接口返回 404/失败时，显示：

```text
完整逐行回放暂不可用，当前仅展示精简概要。可重试加载该行详情。
```

批量回复仍走 `ExecutionHistoryBatchReplyDetail.vue`。

- [ ] **Step 5: 运行定向前后端测试**

```powershell
pnpm --dir web vitest run src/views/other/execution-history/useExecutionHistoryRequests.test.ts
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~ExecutionHistoryApiTests" -m:1
pnpm --dir web typecheck
```

Expected: 全部通过。

- [ ] **Step 6: 提交执行历史修复**

```powershell
git add web/src/api/execution-history.ts web/src/views/other/execution-history tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs web/e2e/execution-history-playback.spec.ts
git diff --cached --check
git commit -m "fix: 恢复执行历史完整回放"
```

### Task 4: 语义搜索作用域隔离

**Files:**
- Create: `web/src/views/base-data/specs/components/specSemanticSearchScope.ts`
- Create: `web/src/views/base-data/specs/components/specSemanticSearchScope.test.ts`
- Modify: `web/src/api/spec.ts`
- Modify: `web/src/views/base-data/specs/components/SpecSemanticSearchDialog.vue`
- Modify: `web/src/views/base-data/specs/components/SpecTable.vue`

**Interfaces:**
- Produces: `buildSemanticSearchScopeKey(request) : string`
- Changes: `semanticSearchSpecs(request, signal?)`
- Produces: edit event payload retains the immutable request scope

- [ ] **Step 1: 编写作用域 A/B 竞态失败测试**

```ts
it("作用域变化后拒绝旧搜索结果", () => {
  const a = buildSemanticSearchScopeKey({
    customerId: 1,
    machineModelId: 2,
    processId: 3,
    queries: ["平台精度"],
    topK: 5,
    minScore: 0.5
  });
  const b = buildSemanticSearchScopeKey({
    customerId: 9,
    machineModelId: 2,
    processId: 3,
    queries: ["平台精度"],
    topK: 5,
    minScore: 0.5
  });
  expect(a).not.toBe(b);
});
```

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
pnpm --dir web vitest run src/views/base-data/specs/components/specSemanticSearchScope.test.ts
```

Expected: 模块不存在。

- [ ] **Step 3: 实现不可变作用域键、取消和编辑绑定**

`SpecSemanticSearchDialog.vue` 保存：

```ts
type ScopedSemanticResult = {
  scopeKey: string;
  request: Readonly<SpecSemanticSearchRequest>;
  response: SpecSemanticSearchResponse;
};
```

每次搜索取消前一 `AbortController`。监听 `customerId/machineModelId/processId`，立即取消、递增代次并清空 `result`、`lastRequest`。响应只有在 `scopeKey === currentScopeKey` 且代次一致时写入。

`edit` 事件传递 `{ row, scope: result.request }`；`SpecTable.vue` 使用 `row.id` 打开编辑，不以当前 props 重新推断目标分组。

- [ ] **Step 4: 运行语义搜索测试与类型检查**

```powershell
pnpm --dir web vitest run src/views/base-data/specs/components/specSemanticSearchScope.test.ts
pnpm --dir web typecheck
```

Expected: 全部通过。

- [ ] **Step 5: 提交语义搜索修复**

```powershell
git add web/src/api/spec.ts web/src/views/base-data/specs/components
git diff --cached --check
git commit -m "fix: 隔离语义搜索异步作用域"
```

### Task 5: 批量回复预览指纹与下载重试

**Files:**
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/batch-reply/batch-reply-preview-state.ts`
- Modify: `web/src/views/batch-reply/batch-reply-state.ts`
- Modify: `web/src/views/batch-reply/composables/useBatchReplyPreview.ts`
- Modify: `web/src/views/batch-reply/composables/useBatchReplyExecution.ts`
- Modify: `web/src/views/batch-reply/components/BatchReplyResultPanel.vue`
- Modify: `web/src/views/batch-reply/index.vue`
- Test: `web/tests/batch-reply-preview-state.test.ts`
- Test: `web/src/views/batch-reply/composables/useBatchReplyExecution.test.ts`

**Interfaces:**
- Produces: `buildBatchReplyPreviewFingerprint(sessionId, targetId, config) : string`
- Changes: `previewBatchReplyTable(data, { signal })`
- Produces: `retryDownload()`, `downloadError`, `downloadLoading`

- [ ] **Step 1: 编写旧预览响应和下载失败测试**

```ts
test("配置指纹变化后旧响应不能恢复 canApply", () => {
  const before = buildBatchReplyPreviewFingerprint("s1", "t1", config(1));
  const after = buildBatchReplyPreviewFingerprint("s1", "t1", config(2));
  assert.notEqual(before, after);
});
```

Vitest 模拟 `executeBatchReply` 成功、`downloadBatchReplyResult` 失败，断言 `executeResult.taskId` 保留且错误文案只描述下载失败。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
pnpm --dir web exec node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test --test-name-pattern="配置指纹变化后旧响应不能恢复 canApply" ./tests/batch-reply-preview-state.test.ts
pnpm --dir web vitest run src/views/batch-reply/composables/useBatchReplyExecution.test.ts
```

Expected: 指纹函数或执行 composable 测试文件不存在，当前下载异常被报告为“批量回复执行失败”。

- [ ] **Step 3: 实现预览请求所有权**

为每个 `targetId:tableIndex` 保存 `{ fingerprint, controller }`。发起请求前取消旧 controller；响应写入条件：

```ts
if (
  requestState.controller.signal.aborted ||
  requestState.fingerprint !== buildCurrentFingerprint(targetId, item.tableIndex)
) {
  return;
}
```

配置、来源会话、目标文件变化时同步清除对应 `previewResults` 和 controller。

- [ ] **Step 4: 分离执行与下载**

`executeReadyTargets` 只在 `executeBatchReply` 失败时显示执行失败；成功后立即保存 `executeResult` 并切换结果页。下载包装为：

```ts
const retryDownload = async () => {
  const result = executeResult.value;
  if (!result) return;
  downloadLoading.value = true;
  downloadError.value = "";
  try {
    const blob = await downloadBatchReplyResult(result.taskId);
    triggerBrowserDownload(blob, result.downloadFileName);
  } catch {
    downloadError.value = "批量回复已执行成功，但结果下载失败，请重试下载";
  } finally {
    downloadLoading.value = false;
  }
};
```

结果面板展示单独“重新下载”按钮。

- [ ] **Step 5: 运行批量回复测试**

```powershell
pnpm --dir web exec node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test --test-name-pattern="预览|配置指纹" ./tests/batch-reply-preview-state.test.ts
pnpm --dir web vitest run src/views/batch-reply/composables/useBatchReplyExecution.test.ts src/views/batch-reply/composables/useBatchReplyTargetUploads.test.ts
pnpm --dir web typecheck
```

Expected: 全部通过。

- [ ] **Step 6: 提交批量回复修复**

```powershell
git add web/src/api/matching.ts web/src/views/batch-reply web/tests/batch-reply-preview-state.test.ts
git diff --cached --check
git commit -m "fix: 绑定批量预览配置并支持下载重试"
```

### Task 6: SmartFill keep-alive 生命周期与移出语义

**Files:**
- Create: `src/AcceptanceSpecSystem.Application/Contracts/MatchingTaskStatusDto.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/MatchingTaskSnapshotService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/MatchingTaskController.cs`
- Modify: `web/src/api/matching.ts`
- Create: `web/src/views/smart-fill/composables/useSmartFillActivation.ts`
- Create: `web/src/views/smart-fill/composables/useSmartFillActivation.test.ts`
- Modify: `web/src/views/smart-fill/index.vue`
- Modify: `web/src/views/shared/useSmartStructureRecognition.ts`
- Modify: `web/src/views/data-import/components/FileUpload.vue`
- Create: `web/src/views/data-import/components/FileUpload.test.ts`
- Modify: `web/tests/smart-fill-keep-alive.test.ts`
- Modify: `web/tests/smart-fill-recognition-selection.test.ts`

**Interfaces:**
- Produces: `GET /api/matching/tasks/{taskId}/status`
- Produces: `pauseForDeactivation()` and `reconcileOnActivation()`
- Produces: `useSmartStructureRecognition.cancelActiveRecognition()`

- [ ] **Step 1: 编写失活取消、激活对账和移出文案测试**

```ts
it("失活时停止当前页面拥有的后台工作", () => {
  const stop = {
    abortScope: vi.fn(),
    invalidatePreview: vi.fn(),
    stopProgress: vi.fn(),
    stopStream: vi.fn(),
    cancelRecognition: vi.fn()
  };
  const activation = useSmartFillActivation(stop);
  activation.pauseForDeactivation();
  Object.values(stop).forEach(fn => expect(fn).toHaveBeenCalledOnce());
});
```

挂载 `FileUpload.vue`，点击“移出当前流程”，断言只发出 `update:modelValue(null)`，未调用文档删除 API。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
pnpm --dir web vitest run src/views/smart-fill/composables/useSmartFillActivation.test.ts src/views/data-import/components/FileUpload.test.ts
```

Expected: 新模块和组件测试失败，当前按钮仍显示“删除”。

- [ ] **Step 3: 增加任务状态只读接口**

`MatchingTaskStatusDto` 固定字段：

```csharp
public sealed class MatchingTaskStatusDto
{
    public string TaskId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool CanDownload { get; init; }
    public DateTime UpdatedAt { get; init; }
}
```

接口只返回当前公司、当前用户有权访问的任务；不存在返回 404。不得包含文件绝对路径或他人任务状态。

- [ ] **Step 4: 接入 Vue 生命周期**

`index.vue` 导入 `onActivated`、`onDeactivated`。失活时取消 scope options、上传/识别、预览、进度轮询和 SSE，但不清空 `taskId` 与已完成步骤状态。激活时：

```ts
onActivated(() => {
  void activation.reconcileOnActivation(taskId.value);
});

onDeactivated(() => {
  activation.pauseForDeactivation();
});
```

状态为完成则恢复下载能力；运行中才恢复轮询；失败则显示服务端状态，不复用旧响应。

- [ ] **Step 5: 修改上传工作区语义**

`FileUpload.vue` 按钮文案改为“移出当前流程”，方法改名 `removeFromCurrentFlow`。同一组件的新文件选择入口可使用“更换文件”。不导入、不调用 `deleteFile`。

- [ ] **Step 6: 运行定向测试**

```powershell
pnpm --dir web vitest run src/views/smart-fill/composables/useSmartFillActivation.test.ts src/views/data-import/components/FileUpload.test.ts src/views/shared/useSmartStructureRecognition.test.ts
pnpm --dir web exec node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test --test-name-pattern="上传文件|keep-alive|移出" ./tests/smart-fill-keep-alive.test.ts ./tests/smart-fill-recognition-selection.test.ts
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~MatchingTask" -m:1
```

Expected: 全部通过。

- [ ] **Step 7: 提交生命周期和文案修复**

```powershell
git add src/AcceptanceSpecSystem.Application/Contracts/MatchingTaskStatusDto.cs src/AcceptanceSpecSystem.Application/Services/MatchingTaskSnapshotService.cs src/AcceptanceSpecSystem.Api/Controllers/MatchingTaskController.cs web/src/api/matching.ts web/src/views/smart-fill web/src/views/shared/useSmartStructureRecognition.ts web/src/views/data-import/components/FileUpload.vue web/src/views/data-import/components/FileUpload.test.ts web/tests/smart-fill-keep-alive.test.ts web/tests/smart-fill-recognition-selection.test.ts
git diff --cached --check
git commit -m "fix: 暂停失活SmartFill任务并明确移出语义"
```

### Task 7: Embedding 缓存唯一键竞争恢复

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Repositories/DatabaseConstraintClassifier.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/SpecEmbeddingCacheService.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/IEmbeddingCacheRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/EmbeddingCacheRepositoryTests.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/EmbeddingCacheConcurrencyMySqlTests.cs`

**Interfaces:**
- Produces: `DatabaseConstraintClassifier.IsUniqueViolation(DbUpdateException)`
- Produces: `GetBySpecModelUsageAsync(int specId, string modelName, string usage, CancellationToken)`

- [ ] **Step 1: 编写真实 MySQL 同键并发测试**

```csharp
[MySqlSmokeFact]
public async Task SameCacheKey_WhenInsertedConcurrently_ShouldConvergeToOneRow()
{
    await using var database = await MySqlMigrationTestDatabase.CreateAsync();
    await database.MigrateAsync();

    var results = await Task.WhenAll(
        CreateCacheThroughServiceAsync(database.ConnectionString),
        CreateCacheThroughServiceAsync(database.ConnectionString));

    results[0].Should().BeEquivalentTo(results[1]);
    (await CountTargetCachesAsync(database.ConnectionString)).Should().Be(1);
}
```

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
$env:ACCEPTANCE_SPEC_ENABLE_MYSQL_MIGRATION_SMOKE_TESTS='true'
$env:ACCEPTANCE_SPEC_MYSQL_MIGRATION_BASE_CONNECTION=$env:ACCEPTANCE_SPEC_TEST_MYSQL_BASE_CONNECTION
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj --filter "FullyQualifiedName~EmbeddingCacheConcurrencyMySqlTests" -m:1
```

Expected: 一个并发调用收到唯一约束 `DbUpdateException`。

- [ ] **Step 3: 只恢复目标 Embedding 唯一键冲突**

保存时传入 `cancellationToken`。捕获条件必须同时满足：

```csharp
catch (DbUpdateException ex) when (
    DatabaseConstraintClassifier.IsUniqueViolation(ex) &&
    ex.Entries.Count > 0 &&
    ex.Entries.All(entry => entry.Entity is EmbeddingCache))
```

把失败新增实体设为 `EntityState.Detached`，按 `(SpecId, ModelName, Usage)` 重新读取胜出记录并验证存在。若不存在，重新抛出原异常。其他约束异常不吞掉。

- [ ] **Step 4: 运行缓存测试**

```powershell
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj --filter "FullyQualifiedName~EmbeddingCacheRepositoryTests|FullyQualifiedName~EmbeddingCacheConcurrencyMySqlTests" -m:1
```

Expected: SQLite 模型测试和真实 MySQL 并发契约通过。

- [ ] **Step 5: 提交缓存并发修复**

```powershell
git add src/AcceptanceSpecSystem.Data/Repositories/DatabaseConstraintClassifier.cs src/AcceptanceSpecSystem.Data/Repositories/IEmbeddingCacheRepository.cs src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs src/AcceptanceSpecSystem.Api/Services/SpecEmbeddingCacheService.cs tests/AcceptanceSpecSystem.Data.Tests/EmbeddingCacheRepositoryTests.cs tests/AcceptanceSpecSystem.Data.Tests/EmbeddingCacheConcurrencyMySqlTests.cs
git diff --cached --check
git commit -m "fix: 恢复Embedding缓存并发唯一冲突"
```

### Task 8: 持久文件待删除状态机与幂等清理

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Entities/WordFileDeletionStatus.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Entities/WordFile.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/20260727090000_AddWordFilePendingDeletion.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/20260727090000_AddWordFilePendingDeletion.Designer.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/DocumentFileAppService.cs`
- Create: `src/AcceptanceSpecSystem.Application/Services/WordFileDeletionCleanupAppService.cs`
- Create: `src/AcceptanceSpecSystem.Api/Options/WordFileDeletionCleanupOptions.cs`
- Create: `src/AcceptanceSpecSystem.Api/Services/WordFileDeletionCleanupHostedService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.json`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.Production.json`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/DocumentFileDeletionTests.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/WordFilePendingDeletionMigrationTests.cs`

**Interfaces:**
- Produces: `WordFileDeletionStatus.Active` and `PendingDeletion`
- Produces: `IWordFileDeletionCleanupAppService.RunBatchAsync(int batchSize, CancellationToken)`

- [ ] **Step 1: 编写标记、失败重试和文件不存在测试**

测试断言删除请求后数据库行仍存在且状态为 `PendingDeletion`；注入抛 `IOException` 的文件存储，断言 `RetryCount` 增加、`LastDeletionError` 非空；文件已不存在时清理成功删除元数据。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~DocumentFileDeletionTests" -m:1
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj --filter "FullyQualifiedName~WordFilePendingDeletionMigrationTests" -m:1
```

Expected: 状态字段和清理服务不存在。

- [ ] **Step 3: 增加迁移和查询过滤**

`WordFile` 新增：

```csharp
public WordFileDeletionStatus DeletionStatus { get; set; } = WordFileDeletionStatus.Active;
public DateTime? DeletionRequestedAt { get; set; }
public int DeletionRetryCount { get; set; }
public DateTime? NextDeletionAttemptAt { get; set; }
public string? LastDeletionError { get; set; }
```

索引覆盖 `(DeletionStatus, NextDeletionAttemptAt, Id)`。普通文件列表和可访问查询排除 `PendingDeletion`。

- [ ] **Step 4: 将删除入口改为事务内标记**

`DeleteFileAsync` 验证范围和引用后只更新状态并使用 `SaveChangesAsync(cancellationToken)`，不直接删除行或物理文件。重复删除待删除记录返回幂等成功。

- [ ] **Step 5: 实现幂等清理器**

清理批次按 `Id` 排序。对每条记录：

1. `DeleteIfExistsAsync(FilePath, cancellationToken)`。
2. 成功或不存在：删除元数据并提交。
3. `IOException`/`UnauthorizedAccessException`：保留行，记录经清洗的错误类别，指数退避上限 24 小时。
4. 每条使用独立 DI 作用域，避免一条失败污染整批。

- [ ] **Step 6: 生成并验证迁移**

```powershell
dotnet ef migrations add AddWordFilePendingDeletion --project src/AcceptanceSpecSystem.Data --startup-project src/AcceptanceSpecSystem.Api
Rename-Item -LiteralPath (Get-ChildItem src/AcceptanceSpecSystem.Data/Migrations/*_AddWordFilePendingDeletion.cs | Where-Object Name -NotLike '*.Designer.cs').FullName -NewName '20260727090000_AddWordFilePendingDeletion.cs'
Rename-Item -LiteralPath (Get-ChildItem src/AcceptanceSpecSystem.Data/Migrations/*_AddWordFilePendingDeletion.Designer.cs).FullName -NewName '20260727090000_AddWordFilePendingDeletion.Designer.cs'
# 同步修改两个迁移文件中的 Migration 标识为 20260727090000_AddWordFilePendingDeletion。
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj --filter "FullyQualifiedName~WordFilePendingDeletionMigrationTests" -m:1
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~DocumentFileDeletionTests" -m:1
```

Expected: 迁移升级、清理重试和幂等删除测试通过。

- [ ] **Step 7: 提交文件删除状态机**

```powershell
git add src/AcceptanceSpecSystem.Data src/AcceptanceSpecSystem.Application/Services/DocumentFileAppService.cs src/AcceptanceSpecSystem.Application/Services/WordFileDeletionCleanupAppService.cs src/AcceptanceSpecSystem.Api/Options/WordFileDeletionCleanupOptions.cs src/AcceptanceSpecSystem.Api/Services/WordFileDeletionCleanupHostedService.cs src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs src/AcceptanceSpecSystem.Api/appsettings.json src/AcceptanceSpecSystem.Api/appsettings.Production.json tests/AcceptanceSpecSystem.Api.Tests/DocumentFileDeletionTests.cs tests/AcceptanceSpecSystem.Data.Tests/WordFilePendingDeletionMigrationTests.cs
git diff --cached --check
git commit -m "fix: 增加持久文件幂等删除状态"
```

### Task 9: 重复分析分桶、预算和取消

**Files:**
- Modify: `src/AcceptanceSpecSystem.Application/Services/ResourceBudgetGovernor.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecQueryService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SpecDuplicateDetectionService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.json`
- Modify: `src/AcceptanceSpecSystem.Api/appsettings.Production.json`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SpecDataScopeTests.cs`
- Create: `tests/AcceptanceSpecSystem.Api.Tests/SpecDuplicateResourceBudgetTests.cs`

**Interfaces:**
- Produces: `ValidateDuplicateCandidates(int)`
- Produces: `ValidateDuplicateComparisons(long)`
- Changes: `SpecDuplicateDetectionService.Detect(..., IResourceBudgetGovernor, CancellationToken)`

- [ ] **Step 1: 编写候选、比较和取消失败测试**

用 2,001 条有效候选断言 `ResourceBudgetExceededException.Code == 422`；用多个不同精确键但同桶候选触发第 1,000,001 次比较；取消 token 后断言不返回部分组。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~SpecDuplicateResourceBudgetTests|FullyQualifiedName~SpecDataScopeTests.GetDuplicateGroups" -m:1
```

Expected: 当前实现执行全量平方比较或返回 400。

- [ ] **Step 3: 扩展资源预算**

`ResourceBudgetOptions` 增加：

```csharp
public int MaxDuplicateCandidates { get; set; } = 2_000;
public long MaxDuplicatePairComparisons { get; set; } = 1_000_000;
public long MaxFileCompareCells { get; set; } = 1_000_000;
public int MaxFileCompareDiffItems { get; set; } = 100_000;
```

`ResourceBudgetExceededException` 改为错误码 422。配置启动校验要求四项均大于 0。

- [ ] **Step 4: 实现精确分组后分桶比较**

精确重复成员先排除。近似候选按归一化项目的首个稳定词元和长度带分桶；只比较同桶。每次比较前：

```csharp
cancellationToken.ThrowIfCancellationRequested();
comparisonCount++;
resourceBudgetGovernor.ValidateDuplicateComparisons(comparisonCount);
```

在全部计算成功后才构建返回对象；超限不得返回已有部分组。

- [ ] **Step 5: 运行重复分析测试**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~SpecDuplicateResourceBudgetTests|FullyQualifiedName~SpecDataScopeTests.GetDuplicateGroups" -m:1
```

Expected: 作用域结果保持不变，预算和取消测试通过。

- [ ] **Step 6: 提交重复分析加固**

```powershell
git add src/AcceptanceSpecSystem.Application/Services/ResourceBudgetGovernor.cs src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecQueryService.cs src/AcceptanceSpecSystem.Application/Services/SpecDuplicateDetectionService.cs src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs src/AcceptanceSpecSystem.Api/appsettings.json src/AcceptanceSpecSystem.Api/appsettings.Production.json src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs tests/AcceptanceSpecSystem.Api.Tests/SpecDataScopeTests.cs tests/AcceptanceSpecSystem.Api.Tests/SpecDuplicateResourceBudgetTests.cs
git diff --cached --check
git commit -m "fix: 限制重复分析候选与比较次数"
```

### Task 10: 文件比较流式暂存与结果预算

**Files:**
- Create: `src/AcceptanceSpecSystem.Application/Services/TemporaryFileLease.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/IFileStorageService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/FileStorageService.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/TestFileStorageService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/FileCompareController.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/FileCompareAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/FileCompareService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/ResourceBudgetGovernor.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/FileCompareTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/FileCompareResourceBudgetTests.cs`

**Interfaces:**
- Adds: `IFileStorageService.SaveUploadedAsync(string originalFileName, Stream content, CancellationToken)`
- Changes: `FileCompareUploadDocument` carries a staged path/stream lease instead of `byte[]`
- Produces: delete-on-dispose `TemporaryFileLease`

- [ ] **Step 1: 编写无 `byte[]`、预算超限和清理测试**

测试使用会在读取超过阈值时抛错的流，证明控制器不调用 `ToArray()`。增加 1,000,001 扫描节点和 100,001 差异边界测试，断言错误码 422。成功、异常、取消后比较临时目录均恢复到基线。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~FileCompareTests|FullyQualifiedName~FileCompareResourceBudgetTests" -m:1
```

Expected: 当前控制器完整缓冲上传，下载使用 `MemoryStream`，新预算测试失败。

- [ ] **Step 3: 实现流式上传暂存**

控制器为两份上传分别创建请求隔离 `TemporaryFileLease`，使用 `CopyToAsync(FileStream, cancellationToken)`。哈希通过 `IncrementalHash` 在流复制过程中计算。`FileCompareUploadDocument` 固定为：

```csharp
public sealed record FileCompareUploadDocument(
    string FileName,
    UploadedFileType FileType,
    string TemporaryPath,
    long Length,
    string Sha256);
```

应用服务从暂存文件流式写入持久存储；请求结束释放 lease。

- [ ] **Step 4: 在比较循环实时执行预算**

Word 段落提取、Excel 单元格联合枚举和差异追加都检查取消。累计扫描节点调用 `ValidateFileCompareCells`；加入非 `Unchanged` 差异前调用 `ValidateFileCompareDiffItems`。解析租约持有到 `FileCompareResult` 完成投影。

- [ ] **Step 5: 使用磁盘后备流式下载**

`DownloadAsync` 把 JSON 直接序列化到请求隔离临时文件，返回 `TemporaryFileLease.OpenRead()`；返回流释放时删除临时目录。不得先创建完整 JSON `MemoryStream`。

- [ ] **Step 6: 运行文件比较测试**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~FileCompareTests|FullyQualifiedName~FileCompareResourceBudgetTests|FullyQualifiedName~ResourceBudgetGovernorTests" -m:1
```

Expected: 全部通过，无遗留 `acceptance-file-compare-*` 临时文件。

- [ ] **Step 7: 提交文件比较加固**

```powershell
git add src/AcceptanceSpecSystem.Application/Services/TemporaryFileLease.cs src/AcceptanceSpecSystem.Application/Services/IFileStorageService.cs src/AcceptanceSpecSystem.Application/Services/FileCompareAppService.cs src/AcceptanceSpecSystem.Application/Services/FileCompareService.cs src/AcceptanceSpecSystem.Application/Services/ResourceBudgetGovernor.cs src/AcceptanceSpecSystem.Api/Services/FileStorageService.cs src/AcceptanceSpecSystem.Api/Controllers/FileCompareController.cs tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/TestFileStorageService.cs tests/AcceptanceSpecSystem.Api.Tests/FileCompareTests.cs tests/AcceptanceSpecSystem.Api.Tests/FileCompareResourceBudgetTests.cs
git diff --cached --check
git commit -m "fix: 流式处理并限制文件比较资源"
```

### Task 11: AI 端点连接期 SSRF 策略

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/IAiEndpointAccessPolicy.cs`
- Create: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/AiEndpointAccessPolicy.cs`
- Create: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/IAiDnsResolver.cs`
- Create: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SafeAiHttpMessageHandlerFactory.cs`
- Modify: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/AiEndpointNormalizer.cs`
- Modify: `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AiServiceReadinessProbeScheduler.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Modify: `src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Ai/SemanticKernel/AiEndpointAccessPolicyTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/AiServiceReadinessProbeSchedulerTests.cs`

**Interfaces:**
- Produces: `ResolveAllowedAddressesAsync(Uri endpoint, AiServiceType serviceType, CancellationToken)`
- Produces: `CreateClient(AiServiceConfigModel config) : HttpClient`
- Consumes: `AiServiceType.Ollama`, `AiServiceType.LMStudio`

- [ ] **Step 1: 编写地址、重定向和 DNS 变化失败测试**

覆盖：

```csharp
[Theory]
[InlineData("127.0.0.1")]
[InlineData("169.254.169.254")]
[InlineData("::1")]
[InlineData("::ffff:127.0.0.1")]
public async Task PublicProvider_ShouldRejectBlockedAddress(string address)
```

伪 DNS 第一次返回公网、第二次返回 `127.0.0.1`，实际连接必须拒绝。公网 302 到环回地址不得发送第二跳。Ollama/LM Studio 到允许私网地址必须通过。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter "FullyQualifiedName~AiEndpointAccessPolicyTests" -m:1
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AiServiceReadinessProbeSchedulerTests" -m:1
```

Expected: 连接期策略和安全 handler 不存在。

- [ ] **Step 3: 分离 URI 规范化与访问策略**

`AiEndpointNormalizer` 只处理 URI 格式、scheme 和尾斜杠。`AiEndpointAccessPolicy` 统一拒绝环回、RFC1918、链路本地、CGNAT、未指定、IPv6 ULA/链路本地/IPv4 映射以及已知元数据主机。

本地例外条件必须同时满足：

```csharp
serviceType is AiServiceType.Ollama or AiServiceType.LMStudio
```

以及地址属于配置允许的本机/私网范围。

- [ ] **Step 4: 实现连接期地址约束**

`SocketsHttpHandler` 设置：

```csharp
var handler = new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    ConnectCallback = connectCallback,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
};
```

`connectCallback` 在每次新连接时重新解析并调用策略，按允许地址建立 `Socket`，返回 `NetworkStream`。原始 URI 主机名保持不变，使 Host、SNI 和证书验证仍针对配置主机。

OpenAI SDK 使用 `HttpClientPipelineTransport` 注入该 `HttpClient`；Azure/OpenAI Semantic Kernel 连接器使用接收 `HttpClient` 的重载。探测调度器使用同一安全客户端。缓存键必须包含提供商、Endpoint 和策略版本。

- [ ] **Step 5: 运行安全测试和现有 AI 工厂测试**

```powershell
dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter "FullyQualifiedName~AiEndpoint" -m:1
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AiServiceReadinessProbeSchedulerTests|FullyQualifiedName~ConfigApisTests" -m:1
```

Expected: 恶意地址全部拒绝，本地 AI 合法用例通过。

- [ ] **Step 6: 提交 AI 出站安全修复**

```powershell
git add src/AcceptanceSpecSystem.Core/AI/SemanticKernel src/AcceptanceSpecSystem.Api/Services/AiServiceReadinessProbeScheduler.cs src/AcceptanceSpecSystem.Api/Program.cs src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs tests/AcceptanceSpecSystem.Core.Tests/Ai/SemanticKernel tests/AcceptanceSpecSystem.Api.Tests/AiServiceReadinessProbeSchedulerTests.cs
git diff --cached --check
git commit -m "fix: 约束AI端点实际连接地址"
```

### Task 12: CRUD 取消传播与有界批量删除

**Files:**
- Modify: `src/AcceptanceSpecSystem.Application/Services/CustomerAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/ProcessAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/MachineModelAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecAppService.cs`
- Modify: related repository interfaces and implementations identified by `rg -n "GetByIdAsync\\([^,\\)]*\\)" src/AcceptanceSpecSystem.Application`
- Create: `tests/AcceptanceSpecSystem.Api.Tests/CrudCancellationAndBatchDeleteTests.cs`

**Interfaces:**
- All async repository calls in touched CRUD paths accept and forward `CancellationToken`
- Batch delete normalizes IDs with `Where(id > 0).Distinct().Take(MaxBatchDeleteItems + 1)`
- Produces: `MaxBatchDeleteItems = 500`

- [ ] **Step 1: 编写取消和超大批次失败测试**

测试使用已取消 token 调用客户、制程、机型和规格删除，断言 `OperationCanceledException`；501 个唯一 ID 返回 422；重复 ID 只处理一次。

- [ ] **Step 2: 运行测试并确认 RED**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~CrudCancellationAndBatchDeleteTests" -m:1
```

Expected: 至少一个无 token 仓储调用继续执行，超大批次未被拒绝。

- [ ] **Step 3: 批量读取引用并收敛异常**

把循环内逐 ID 引用检查改为单次批量查询，先构建 `referencedIds` 集合，再形成每项结果。仅映射已知外键、唯一键和并发错误为 409；其他异常交给统一 500 边界。

- [ ] **Step 4: 运行 CRUD 定向测试**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~CrudCancellationAndBatchDeleteTests|FullyQualifiedName~CrudApisTests|FullyQualifiedName~SpecDataScopeTests" -m:1
```

Expected: 全部通过。

- [ ] **Step 5: 提交取消和批量边界**

```powershell
git add src/AcceptanceSpecSystem.Application src/AcceptanceSpecSystem.Data/Repositories tests/AcceptanceSpecSystem.Api.Tests/CrudCancellationAndBatchDeleteTests.cs
git diff --cached --check
git commit -m "fix: 传播CRUD取消并限制批量删除"
```

### Task 13: 依赖、CI、密码规则和脆弱测试治理

**Files:**
- Modify: `web/package.json`
- Modify: `web/pnpm-lock.yaml`
- Modify: `web/src/plugins/elementPlus.ts`
- Modify: `Directory.Build.props`
- Modify: `src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj`
- Create: `src/AcceptanceSpecSystem.Api/packages.lock.json`
- Create: `src/AcceptanceSpecSystem.Application/packages.lock.json`
- Create: `src/AcceptanceSpecSystem.Core/packages.lock.json`
- Create: `src/AcceptanceSpecSystem.Data/packages.lock.json`
- Create: `tests/AcceptanceSpecSystem.Api.Tests/packages.lock.json`
- Create: `tests/AcceptanceSpecSystem.Core.Tests/packages.lock.json`
- Create: `tests/AcceptanceSpecSystem.Data.Tests/packages.lock.json`
- Create: `tools/E2ETest/packages.lock.json`
- Create: `tools/MatchingRegressionReport/packages.lock.json`
- Create: `tools/SmartFillInsightReport/packages.lock.json`
- Create: `tools/SmartStructureHeaderGapReport/packages.lock.json`
- Modify: `.github/workflows/ci.yml`
- Modify: `web/Dockerfile`
- Modify: `deploy/validate-production-env.sh`
- Modify: `.deploy/production.env.example`
- Modify: `web/tests/data-import-confirm-layout.test.ts`
- Modify: `web/tests/data-import-progress-state.test.ts`
- Modify: `web/tests/frontend-shell-guard.test.ts`
- Modify: `web/tests/layout-density.test.ts`
- Modify: `web/tests/master-data-options-pagination.test.ts`
- Modify: `web/tests/smart-fill-preview-runtime.test.ts`
- Create: `web/src/views/shared/SmartStructureRangeEditorDrawer.test.ts`
- Create: `web/src/views/data-import/composables/useDataImportExecution.test.ts`
- Modify: `web/src/views/data-import/dataImport.confirmImport.test.ts`
- Create: `web/src/views/smart-fill/components/MatchConfig.test.ts`
- Create: `web/src/views/smart-fill/components/SmartFillPreviewStep.test.ts`

**Interfaces:**
- Dependency floors and locks are enforced by package managers
- CI third-party Actions use immutable commit SHA plus version comments
- Node source tests retain only static rules that cannot be expressed as behavior tests

- [ ] **Step 1: 记录当前质量基线**

```powershell
pnpm --dir web test:node
pnpm --dir web audit --audit-level high
dotnet list AcceptanceSpecSystem.sln package --vulnerable --include-transitive
```

Expected baseline: Node 324/332，通过 324、失败 8；npm audit 报告 `postcss@8.5.15` 和 `brace-expansion@5.0.7` 高危路径。

- [ ] **Step 2: 升级前端依赖并移除无效注册**

`web/package.json`：

```json
{
  "pnpm": {
    "overrides": {
      "postcss": "8.5.18",
      "brace-expansion@>=5.0.0 <5.0.8": "5.0.8"
    }
  }
}
```

保留其他现有 overrides；运行 `pnpm --dir web install --lockfile-only`。从 `elementPlus.ts` 的 import 和 `components` 清单删除 `ElResult`，确认模板无 `<el-result>`。

- [ ] **Step 3: 将 8 个失败源码断言改为行为测试**

处理规则：

- 删除“默认逐表确认学习操作”断言，因为文件级统一确认已明确覆盖它；新增文件级确认行为测试。
- A1 范围和移动触控写入 `SmartStructureRangeEditorDrawer.test.ts`；导入错误写入 `useDataImportExecution.test.ts`；文件级确认写入 `dataImport.confirmImport.test.ts`。
- 主数据完整分页写入 `MatchConfig.test.ts`；空状态和语义颜色 class 写入 `SmartFillPreviewStep.test.ts`，模拟输入、用户动作和渲染输出，不匹配函数源码文本。
- `frontend-shell-guard` 只保留“已注册标签必须被模板使用”的有效集合检查，并由移除 `ElResult` 通过。

运行：

```powershell
pnpm --dir web test:node
pnpm --dir web test:vitest
```

Expected: 两套测试均通过。

- [ ] **Step 4: 固定 NuGet 解析结果**

将：

```xml
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.DataProtection.Abstractions" Version="8.0.29" />
```

替换通配版本。执行：

```powershell
dotnet restore AcceptanceSpecSystem.sln --use-lock-file
dotnet restore AcceptanceSpecSystem.sln --locked-mode
```

提交生成的锁文件，并在 CI restore 使用 `--locked-mode`。

- [ ] **Step 5: 固定 CI Actions**

对每个当前 tag 执行只读解析：

```powershell
git ls-remote https://github.com/actions/checkout refs/tags/v4
git ls-remote https://github.com/actions/setup-node refs/tags/v4
git ls-remote https://github.com/actions/setup-dotnet refs/tags/v4
git ls-remote https://github.com/actions/upload-artifact refs/tags/v4
git ls-remote https://github.com/pnpm/action-setup refs/tags/v4
```

把 `uses: owner/repo@v4` 替换为命令返回的 40 位提交 SHA，并在同一行保留 `# v4`。不得凭记忆填写 SHA。

- [ ] **Step 6: 统一 registry 和密码规则**

`web/Dockerfile` 增加：

```dockerfile
ARG NPM_REGISTRY=https://registry.npmjs.org
RUN pnpm config set registry "${NPM_REGISTRY}"
```

移除强制 `npmmirror`。部署脚本对管理员和普通用户种子密码同时验证长度 4～200，并继续拒绝已知占位符和弱默认值。示例与 CI 占位符文案改为 4～200，不降低生产随机密码建议。

- [ ] **Step 7: 运行依赖和质量定向验证**

```powershell
pnpm --dir web install --frozen-lockfile
pnpm --dir web audit --audit-level high
pnpm --dir web test
dotnet restore AcceptanceSpecSystem.sln --locked-mode
dotnet list AcceptanceSpecSystem.sln package --vulnerable --include-transitive --no-restore
```

Expected: 无 High/Critical npm 漏洞；Node 332/332 或因新增行为测试得到更高总数且 0 失败；NuGet 无已知漏洞。

- [ ] **Step 8: 提交质量治理**

```powershell
git add web/package.json web/pnpm-lock.yaml web/src/plugins/elementPlus.ts web/tests/data-import-confirm-layout.test.ts web/tests/data-import-progress-state.test.ts web/tests/frontend-shell-guard.test.ts web/tests/layout-density.test.ts web/tests/master-data-options-pagination.test.ts web/tests/smart-fill-preview-runtime.test.ts web/src/views/shared/SmartStructureRangeEditorDrawer.test.ts web/src/views/data-import/composables/useDataImportExecution.test.ts web/src/views/data-import/dataImport.confirmImport.test.ts web/src/views/smart-fill/components/MatchConfig.test.ts web/src/views/smart-fill/components/SmartFillPreviewStep.test.ts Directory.Build.props src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj src/AcceptanceSpecSystem.Api/packages.lock.json src/AcceptanceSpecSystem.Application/packages.lock.json src/AcceptanceSpecSystem.Core/packages.lock.json src/AcceptanceSpecSystem.Data/packages.lock.json tests/AcceptanceSpecSystem.Api.Tests/packages.lock.json tests/AcceptanceSpecSystem.Core.Tests/packages.lock.json tests/AcceptanceSpecSystem.Data.Tests/packages.lock.json tools/E2ETest/packages.lock.json tools/MatchingRegressionReport/packages.lock.json tools/SmartFillInsightReport/packages.lock.json tools/SmartStructureHeaderGapReport/packages.lock.json .github/workflows/ci.yml web/Dockerfile deploy/validate-production-env.sh .deploy/production.env.example
git diff --cached --check
git commit -m "chore: 固定依赖并修复质量门禁"
```

提交前用 `git diff --cached --name-only` 排除与本任务无关的历史文档和产物。

### Task 14: 跨切片验收、迁移回滚与 OpenSpec 证据

**Files:**
- Modify: `openspec/changes/harden-runtime-correctness-and-resource-boundaries/tasks.md`
- Add: `web/e2e/execution-history-playback.spec.ts`
- Add: `web/e2e/semantic-search-scope.spec.ts`
- Add: `web/e2e/batch-reply-download-retry.spec.ts`
- Add: `web/e2e/smart-fill-activation.spec.ts`
- Add verification logs only under existing ignored artifact directories; do not commit runtime logs or credentials

**Interfaces:**
- Acceptance evidence maps every one of the 17 OpenSpec deltas to a passing automated or environment test
- Existing production rollout tasks remain unchecked without the real target environment

- [ ] **Step 1: 运行 .NET 全量验证，严格串行**

```powershell
dotnet restore AcceptanceSpecSystem.sln --locked-mode
dotnet test AcceptanceSpecSystem.sln -c Debug --no-restore -m:1
dotnet build AcceptanceSpecSystem.sln -c Release --no-restore -m:1 -p:TreatWarningsAsErrors=true
dotnet list AcceptanceSpecSystem.sln package --vulnerable --include-transitive --no-restore
```

Expected: 0 失败、0 警告、无已知 NuGet 漏洞。测试和构建不得并行，避免共享 `obj` 锁冲突。

- [ ] **Step 2: 运行前端全量门禁**

```powershell
pnpm --dir web test
pnpm --dir web typecheck
pnpm --dir web lint:check
pnpm --dir web format:check
pnpm --dir web stylelint:check
pnpm --dir web build
pnpm --dir web check:bundle-budget
pnpm --dir web audit --audit-level high
```

Expected: 全部退出 0。

- [ ] **Step 3: 运行真实 MySQL 契约和迁移回滚**

在确认测试连接串只指向隔离测试实例后：

```powershell
$env:ACCEPTANCE_SPEC_ENABLE_MYSQL_MIGRATION_SMOKE_TESTS='true'
$env:ACCEPTANCE_SPEC_MYSQL_MIGRATION_BASE_CONNECTION=$env:ACCEPTANCE_SPEC_TEST_MYSQL_BASE_CONNECTION
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj -c Release --no-restore -m:1
```

验证升级迁移、旧数据读取、Embedding 并发、待删除重试；随后对临时数据库执行回滚到前一迁移并重新升级。不得对开发或生产库执行回滚演练。

- [ ] **Step 4: 运行定向浏览器验收**

```powershell
pnpm --dir web test:e2e:typecheck
pnpm --dir web exec playwright test e2e/execution-history-playback.spec.ts e2e/semantic-search-scope.spec.ts e2e/batch-reply-download-retry.spec.ts e2e/smart-fill-activation.spec.ts
```

Expected: 执行历史快速切换与分页、语义范围 A→B、批量下载失败重试、SmartFill 离页/返回均通过。

- [ ] **Step 5: 运行 OpenSpec 与仓库卫生检查**

```powershell
openspec validate harden-runtime-correctness-and-resource-boundaries --strict
openspec validate --all --strict --no-interactive
python tools/test_assert_repository_hygiene.py
python tools/assert_repository_hygiene.py
git diff --check
git status --short
```

Expected: 全部通过；工作区只包含计划内文件。

- [ ] **Step 6: 运行生产式 Docker 演练**

使用项目现有 Docker 验证脚本和隔离 MySQL 容器，确认：

- `/health/live`
- `/health/ready`
- `/health/capabilities/ai`
- `/api/health/ready`
- 待删除清理器启动与停止
- 数据库升级与回滚步骤
- Web 登录及四个定向页面流程

Docker 验证只能作为生产式本地证据，不勾选真实内网受控发布任务。

- [ ] **Step 7: 更新本变更任务清单并提交证据状态**

仅把实际完成且有命令输出支持的条目标记为 `[x]`。保留：

- `harden-single-company-production-boundaries` 的真实受控发布/回滚项未完成。
- `harden-browser-auth-token-lifecycle` 的真实部署后归档项未完成。

```powershell
git add openspec/changes/harden-runtime-correctness-and-resource-boundaries/tasks.md web/e2e
git diff --cached --check
git commit -m "test: 完成运行时加固验收矩阵"
```

- [ ] **Step 8: 最终本地交付检查**

```powershell
git status --short --branch
git log --oneline --decorate origin/main..HEAD
git diff --stat origin/main...HEAD
```

Expected: 工作区干净；所有提交位于本地修复分支；不执行 `git push`、`git merge main` 或部署命令。

## Spec Coverage Self-Review

| OpenSpec requirement | Covered by |
|---|---|
| 控制器操作审计反映最终响应 | Task 1 |
| API 错误响应使用真实 HTTP 语义 | Task 2 |
| 高成本 API 支持取消与预算拒绝 | Tasks 9–10 |
| AI 端点策略覆盖实际网络连接 | Task 11 |
| 高成本操作使用统一资源预算 | Tasks 9–10 |
| 构建依赖可重复解析 | Task 13 |
| Embedding 缓存并发写入幂等 | Task 7 |
| 持久文件删除状态可恢复 | Task 8 |
| 文件比较使用有界流式临时存储 | Task 10 |
| 持久文件物理删除由幂等清理器执行 | Task 8 |
| 重复项近似分析避免全量平方比较 | Task 9 |
| 异步页面结果绑定发起上下文 | Tasks 3–5 |
| 智能填充执行历史支持完整回放与分页 | Task 3 |
| 长任务页面失活时暂停连接并恢复对账 | Task 6 |
| 批量执行成功与下载结果分离 | Task 5 |
| 上传区移除文件不删除持久文件 | Task 6 |
| SmartFill 文件级确认覆盖旧逐表确认交互 | Task 13 |
