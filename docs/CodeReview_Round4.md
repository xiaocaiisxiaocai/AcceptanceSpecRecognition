# AcceptanceSpecificationSystem — 全项目 Code Review 报告（第四轮）

> 审查范围：所有后端 C#（API / Core / Data / Tests）+ 前端 TypeScript/Vue 3
> 审查时间：2026-03-27
> 审查工具：Claude Opus 4.6（5 并行 Agent 覆盖全仓库）

## 本轮认领结论（Codex 复核，2026-03-27）

### 已认领并完成修复

- `P0-2` CORS 在无配置来源时全开放
  - 已修复：`Program.cs` 启动期强校验 `Cors:AllowedOrigins`，生产/开发环境禁止空配置和 `*`，不再 fallback 到 `AllowAnyOrigin()`。
- `P1-2` `SemanticKernelServiceFactory` 强制解引用可空模型字段
  - 已修复：新增 `RequireLlmModel` / `RequireEmbeddingModel` 显式 Guard，移除 `config.LlmModel!` / `config.EmbeddingModel!`。
- `P1-3` `AuthDataScopeService` 每次请求全量查询组织树
  - 已修复：引入 `IMemoryCache`，仅缓存公司组织树快照，不缓存用户级 scope 结果；缓存 key 绑定组织数量与最近变更时间，避免旧 scope 污染后续请求。
- `P1-5` `MatchingExecutionController.llm-stream` 缺少审计标记
  - 已修复：为 `llm-stream` 端点补充 `[AuditOperation("llm-stream", "matching-fill")]`。
- `P1-6` `MatchingFillTask.CreatedByUserId` / `CompanyId` 可空导致归属不明确
  - 已修复：`MatchingWorkflowService.EnsureTaskOwnership` 对空归属元数据显式拒绝，下载/严格复用统一返回“任务不存在或已过期”。
- `P1-7` `ExceptionHandlingMiddleware` 对 `OperationCanceledException` 捕获过窄
  - 已修复：扩大到统一捕获所有 `OperationCanceledException`；客户端断连仅记 `Debug`，服务端取消返回 `408`，不再误记为 `500`。
- `P2-1` `AcceptanceSpecQueryOptions.PageSize` 缺少集中上限保护
  - 已修复：在 `AcceptanceSpecQueryOptions` 内部集中收敛 `Page`/`PageSize`，`PageSize` 统一限制为 `1..200`。
- `P2-2` `AuthSeedOptions` 密码字段缺启动期校验
  - 已修复：新增 `AuthSeedOptionsValidator`，并在 `Program.cs` 中通过 `AddOptions().ValidateOnStart()` 启动期校验口令非空/长度。
- `P2-3` `SynonymService` 缓存击穿 / 静态缓存污染
  - 已修复：移除静态缓存，改为实例级缓存 + `SemaphoreSlim` double-check，避免并发穿透和跨测试污染。
- `P2-5` `AuthAccessService` 直接依赖 `AppDbContext`
  - 已修复：改为通过 `ISystemUserRepository` / `IAuthRoleLookupRepository` 查询，不再直接持有 `AppDbContext`。
- `P2-6` `EmbeddingCacheRepository` 批量删除走内存加载
  - 已修复：`DeleteByModelNameAsync`、`DeleteExpiredAsync`、`DeleteByModelVersionAsync` 全部改为 `ExecuteDeleteAsync()` 下推数据库。
- `P2-9` `MatchingApiControllerBase` 异常处理边界不清晰
  - 已修复：补充中文注释，明确仅消费 `MatchingApiException`，其余异常继续交给全局中间件。
- `P3-1` `IAiServiceSelector` / `IAiServiceConfigProvider` 缺少中文注释
  - 已修复：为接口与公开方法补充中文 XML 注释。
- `P3-4` `MatchingTaskController` 的 `taskId` 参数无格式约束
  - 已修复：下载路由增加 32 位小写十六进制约束，只允许合法任务 ID 进入控制器。
- `F2` `StrictReuseDialog.vue` 权限 props 缺失时无开发期提示
  - 已修复：开发环境下检测 `canPreview` / `canExecute` / `canDownload` 是否缺失，缺失时输出 `console.warn`。
- `F4` `beforeRequestCallback` 分支丢失审计 headers
  - 已修复：新增 `ensureAuditHeaders(config)`，在 callback 前后补齐 `X-Client-Trace-Id`、`X-Client-Id`、`X-Frontend-Route`。
- `F5` `user.logOut` 未清理关键权限/路由缓存
  - 已修复：登出时同步清理 `async-routes`、权限缓存、标签页缓存，并重置头像/昵称/权限等基础用户态。

### 已部分收敛，但暂不宣称完全修复

- `P0-3` `PromptTemplateProvider` 直接持有 `AppDbContext` 并提交
  - 已收敛：已移除对 `AppDbContext` 的直接依赖，改走 `IPromptTemplateRepository + IUnitOfWork`。
  - 仍未完全闭合：Provider 依旧在内部调用 `SaveChangesAsync`，尚未做到“由调用方统一提交”。
- `P0-6` `ApiKey` 解密静默兼容
  - 已收敛：兼容回退路径新增 `TraceWarning`，不再完全静默吞错。
  - 仍未完全闭合：出于历史明文数据兼容，当前仍保留回退而非直接抛错。
- `P2-4` `TextProcessingConfigRepository.GetConfigAsync` 存在竞态
  - 已收敛：新增 `SemaphoreSlim` 串行化默认配置创建，单进程内不再重复插入。
  - 仍未完全闭合：数据库级唯一约束 / 多实例并发兜底尚未补齐。

### 已核实，但本轮明确不处理

- `P0-1` `AppDbContext` 硬编码数据库 root 密码
  - 用户已明确：**本轮不修改**。因此该项不认领，不做代码变更。
- `P0-4` `EmbeddingCache` 唯一索引缺 `ModelVersion`
  - 问题存在，但需要迁移脚本、唯一索引调整和写入链路的 upsert 语义一起处理；本轮未纳入。
- `P0-5` `MatchingFillTask` 缺少乐观并发控制
  - 需要继续核实 `MatchingWorkflowService` 的实际并发写入路径，再决定是否加 `RowVersion` 与 `409` 语义；本轮未纳入。

### 已复核为误报 / 不成立 / 优先级下调

- `P1-4` “迁移失败时仍会继续执行种子数据”
  - 当前代码中 `DatabaseInitializer.InitializeAsync(db)` 直接抛异常，`Program.cs` 未吞异常；迁移失败时应用启动会中断，种子逻辑不会继续执行。该项按**当前代码状态不成立**处理。

### 本轮验证

- `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~ReviewRegressionTests|FullyQualifiedName~AuthSeedOptionsValidationTests" --no-restore -m:1`
- `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AuthDataScopeServiceTests|FullyQualifiedName~SpecDataScopeTests" --no-restore -m:1`
- `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --filter FullyQualifiedName~CoreProviderBoundaryTests --no-restore -m:1`
- `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --no-restore -m:1`
- `dotnet test tests/AcceptanceSpecSystem.Core.Tests/AcceptanceSpecSystem.Core.Tests.csproj --no-restore -m:1`
- `dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj --no-restore -m:1`
- `pnpm --dir web typecheck`
- `pnpm --dir web build`

### 本轮验证结果（2026-03-27）

| 测试集 | 通过 | 跳过 | 失败 |
|--------|------|------|------|
| Api.Tests（含 35 条 ReviewRegressionTests） | 149 | 0 | **0** |
| Core.Tests | 61 | 0 | **0** |
| Data.Tests | 25 | 2 | **0** |
| 前端 `tsc --noEmit` / `vue-tsc --noEmit` | ✓ | — | **0** |

---

## 目录

1. [P0 — 严重问题（上线前必须修复）](#p0)
2. [P1 — 重要问题](#p1)
3. [P2 — 中等问题](#p2)
4. [P3 — 轻微问题](#p3)
5. [测试层问题](#tests)
6. [前端问题](#frontend)
7. [亮点](#highlights)
8. [综合评分](#score)
9. [行动清单](#actions)

---

## P0 — 严重问题（上线前必须修复）

### P0-1. `AppDbContext` 硬编码数据库 root 密码

**文件:** `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`

```csharp
public const string DefaultConnectionString =
    "Server=localhost;Database=acceptance_spec_db;User=root;Password=abc+123;CharSet=utf8mb4;";
```

- **风险:** 明文 root 密码提交至 Git；任何 clone 仓库的人都能看到；生产环境若沿用此默认值将直接暴露整个数据库。
- **修复:** 删除该常量，改为抛出 `InvalidOperationException("ConnectionStrings:Default 未配置")`；连接字符串通过环境变量 / Secret Manager 注入，`appsettings.json` 只保留空占位符。

---

### P0-2. CORS 配置在未配置来源时全开放

**文件:** `src/AcceptanceSpecSystem.Api/Program.cs`，约第 231–235 行

```csharp
if (allowedOrigins.Length == 0 || allowedOrigins.Contains("*"))
{
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}
```

- **风险:** `appsettings.json` 若未配置 `AllowedOrigins`，则任意域名均可跨域访问 API；这是常见的生产配置遗漏场景。
- **修复:** 无合法来源配置时直接启动失败（`throw`），或拒绝注册 CORS 策略；不允许 fallback 为全开放。

---

### P0-3. `PromptTemplateProvider` 直接持有 `AppDbContext` 并调用 `SaveChangesAsync`

**文件:** `src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs`，第 32–74 行

```csharp
public sealed class PromptTemplateProvider : IPromptTemplateProvider
{
    private readonly IPromptTemplateRepository _repository;
    private readonly AppDbContext _dbContext; // 直接依赖具体 DbContext
    ...
    await _dbContext.SaveChangesAsync(cancellationToken);
}
```

- **风险（三重）:**
  1. Service 层打算在同一 UoW 中保存多个聚合时，Provider 提前提交，破坏原子性；
  2. `IUnitOfWork.SaveChangesAsync` 与 Provider 内的 `SaveChanges` 产生双重提交风险；
  3. 唯独此 Provider 绕过了 UoW 抽象，其余 Provider 均通过 Repository 接口操作，一致性被破坏。
- **修复:** 移除 `AppDbContext` 注入；Provider 只做 tracked 操作，由调用方通过 `IUnitOfWork.SaveChangesAsync()` 统一提交。

---

### P0-4. `EmbeddingCache` 唯一索引缺 `ModelVersion`，并发插入会抛未处理异常

**文件:** `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`（第 264 行）；`src/AcceptanceSpecSystem.Data/Entities/EmbeddingCache.cs`

- **问题 1:** 唯一索引为 `(SpecId, ModelName)`，`ModelVersion` 字段存在但未入索引。同名模型升级版本时，写入会因唯一约束冲突失败，无法多版本共存。
- **问题 2:** 两线程并发为同一 `(SpecId, ModelName)` 计算 embedding，均读到「不存在」后并发插入，唯一约束异常冒泡为 500；代码中未见 `DbUpdateException` 捕获。
- **修复:** 唯一索引改为 `(SpecId, ModelName, ModelVersion)`；写入路径捕获 `DbUpdateException` 后重新查询（upsert 语义）。

---

### P0-5. `MatchingFillTask` 无乐观并发控制，状态机更新可被静默覆盖

**文件:** `src/AcceptanceSpecSystem.Data/Entities/MatchingFillTask.cs`

- **问题:** 任务实体含 `PayloadJson`（序列化快照）等关键字段，但无 `RowVersion` 并发标记。SSE 流式写入 + 下载接口并发读写同一任务时，最后写入者静默覆盖，可能导致用户下载到错误文件。
- **修复:** 增加 `[Timestamp] public byte[] RowVersion { get; set; }`，配置 `.IsRowVersion()`；更新操作捕获 `DbUpdateConcurrencyException` 并返回 409。

---

### P0-6. `ApiKey` 解密向后兼容逻辑静默吞错

**文件:** `src/AcceptanceSpecSystem.Data/Entities/AiServiceConfig.cs`（ApiKey 解密相关方法）

- **问题:** 解密失败时代码静默返回空字符串或原始密文，而非抛出异常。调用方无法区分「密钥未配置」和「解密失败（密钥被篡改 / 加密算法不匹配）」，可能导致 AI 服务以错误密钥静默失败，日志中无任何告警。
- **修复:** 解密失败时抛出明确的业务异常（如 `InvalidOperationException("ApiKey 解密失败，请检查加密配置")` ），或至少写入 `ILogger.LogError`。

---

## P1 — 重要问题

### P1-1. `MatchingWorkflowService` 职责过重（超 1300+ 行）

**文件:** `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`

- **问题:** 同时承担预览、SSE 流式执行、严格复用、ZIP 打包下载、任务快照存储、LLM 建议、断路器等十余个职责域，构造函数注入超过 11 个依赖，严重违反 SRP。控制器层已完成拆分，但 Service 层未跟进。
- **后果:** 可测试性差；合并冲突风险高；单个方法难以独立演化。
- **建议:** 拆分为以下独立服务：
  - `LlmStreamOrchestrationService`（SSE 流式 LLM 编排 + 断路器）
  - `StrictReuseWorkflowService`（严格复用预览 / 执行 / 下载）
  - `FillTaskSnapshotService`（任务快照序列化 / 持久化）

---

### P1-2. `SemanticKernelServiceFactory` 强制解引用 `LlmModel!`，缺配置完整性校验

**文件:** `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelServiceFactory.cs`

```csharp
kernel.AddOpenAIChatCompletion(config.LlmModel!, config.ApiKey!);
```

- **问题:** 使用 `!` 强制解引用可空字段，若配置未填写则在运行时抛 `NullReferenceException`，而非在服务启动时快速失败并给出明确错误信息。
- **修复:** 在工厂方法入口处增加 `Guard`（如 `ArgumentException.ThrowIfNullOrEmpty(config.LlmModel)`），在 `Program.cs` 启动阶段校验 AI 配置完整性。

---

### P1-3. `AuthDataScopeService` 每次请求全量查询组织树，无缓存

**文件:** `src/AcceptanceSpecSystem.Api/Services/AuthDataScopeService.cs`

- **问题:** 每次鉴权调用都从数据库全量加载组织树进行子树遍历，在组织结构较大或高并发场景下会产生大量重复查询，成为性能瓶颈。
- **建议:** 对组织树添加内存缓存（`IMemoryCache`），TTL 建议 5 分钟，组织结构变更时主动失效；或改为 closure table / materialized path 查询模式，直接在 SQL 层完成子树过滤。

---

### P1-4. `Program.cs` 迁移失败时种子数据仍会执行

**文件:** `src/AcceptanceSpecSystem.Api/Program.cs`

- **问题:** 启动流程中迁移与种子数据是顺序调用，若迁移部分失败（捕获异常并记录日志后继续），种子数据操作会在不一致的 schema 上运行，导致数据污染或二次异常，且错误信息被掩盖。
- **修复:** 迁移失败时应让应用启动失败（`Environment.Exit(1)` 或 rethrow），不允许在迁移异常后继续执行种子逻辑。

---

### P1-5. `MatchingExecutionController` 的 SSE 流式端点缺少审计日志

**文件:** `src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs`

- **问题:** `llm-stream` 端点（SSE 流式 LLM 填充）是系统最核心的 AI 调用入口，但未标注 `[AuditOperation]` 或等效审计属性，而其他高权限操作均有审计日志覆盖。
- **修复:** 为该端点补充审计日志标注，记录调用者、入参摘要和触发时间。

---

### P1-6. `MatchingFillTask.CreatedByUserId` 和 `CompanyId` 设计为可空，缺乏业务约束

**文件:** `src/AcceptanceSpecSystem.Data/Entities/MatchingFillTask.cs`，第 26、31 行

- **问题:** 任务归属字段 `int? CreatedByUserId` 和 `int? CompanyId` 设计为可空，且 `AppDbContext.cs` 配置了 `OnDelete(DeleteBehavior.SetNull)`。用户被删除后任务归属丢失，所有权校验行为不明确；若基于 `CompanyId` 做数据隔离，null 值会导致隔离规则无法应用。
- **修复:** 明确文档化「CompanyId 为 null 时禁止下载」的守卫逻辑；或考虑软删除用户而非物理删除，保持外键有效。

---

### P1-7. `ExceptionHandlingMiddleware` 的 `OperationCanceledException` 捕获条件过窄

**文件:** `src/AcceptanceSpecSystem.Api/Middleware/ExceptionHandlingMiddleware.cs`，第 38 行

```csharp
catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
```

- **问题:** 仅在客户端主动断开时匹配。若是超时、后台任务取消、测试中的 `CancellationToken` 触发的 `OperationCanceledException`，`context.RequestAborted` 未必取消，`when` 条件不满足，异常会冒泡为 500 并被记录为 Error 级别日志，产生大量误报。
- **修复:** 将条件改为直接捕获所有 `OperationCanceledException`，统一按 499/408 处理（不写入 Error 日志，或降级为 Warning）。

---

## P2 — 中等问题

### P2-1. `AcceptanceSpecQueryOptions` 分页缺上限保护

**文件:** `src/AcceptanceSpecSystem.Data/Repositories/AcceptanceSpecQueryOptions.cs`

- **问题:** `PageSize` 字段无最大值约束。若调用方传入 `PageSize=100000`，将触发全表扫描后加载进内存，形成 OOM 风险。
- **修复:** 增加 `if (PageSize > 500) PageSize = 500;` 的上限保护，或通过 `[Range(1, 200)]` 注解在 DTO 层限制。

---

### P2-2. `AuthSeedOptions` 密码字段缺启动期校验

**文件:** `src/AcceptanceSpecSystem.Api/Options/AuthSeedOptions.cs`；`src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs`

- **问题:** 种子账号密码通过配置注入，字段可空，无强度校验。若密码为空或 `appsettings.json` 被提交到版本控制，存在弱密码泄漏风险。
- **修复:** 在 `Program.cs` 启动阶段通过 `IOptions<AuthSeedOptions>` 校验密码非空且长度 ≥ 12；密码通过环境变量或 User Secrets 注入，不写入版本控制。

---

### P2-3. `SynonymService` 静态缓存锁在 `await` 前释放，存在并发穿透

**文件:** `src/AcceptanceSpecSystem.Core/TextProcessing/Services/SynonymService.cs`

- **问题:** 使用 `static` 字段存缓存，`lock` 块检查过期后释放锁，再执行 `await GetAllGroupsAsync()`。由于不能在 `lock` 内 `await`，多个并发请求会同时通过缓存检查，各自调用一次数据库查询，形成缓存击穿。同时静态缓存在测试隔离场景下造成跨测试污染。
- **修复:** 使用 `SemaphoreSlim(1,1)` + double-check 模式；或改用 `IMemoryCache`（已在 DI 中注册），去除 `static` 字段，改为实例级缓存。

---

### P2-4. `TextProcessingConfigRepository.GetConfigAsync` 存在竞态，可能插入重复「单例」记录

**文件:** `src/AcceptanceSpecSystem.Data/Repositories/TextProcessingConfigRepository.cs`

- **问题:** 方法先查询是否存在记录，若不存在则插入默认配置。两个并发请求都通过「不存在」检查后，会各自尝试插入，导致重复记录。数据库层无唯一约束兜底。
- **修复:** 在数据库表上增加唯一约束（如 `(ConfigKey)` 或直接限制表最多一行）；或在代码中捕获 `DbUpdateException` 后重新查询返回已存在的记录。

---

### P2-5. `AuthAccessService` 同时注入 `IUnitOfWork` 和 `AppDbContext`，绕过 UoW 抽象

**文件:** `src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs`

- **问题:** 构造函数同时接受 `IUnitOfWork` 和 `AppDbContext`，直接持有 `AppDbContext` 的查询操作绕过了 Repository 层，使测试难以 Mock，也产生同一 Scope 内两个 DbContext 引用的隐患。
- **修复:** 统一通过 `IUnitOfWork.Repository<T>()` 或专用 Repository 接口访问数据；移除直接的 `AppDbContext` 注入。

---

### P2-6. `EmbeddingCacheRepository` 批量删除使用 `ToListAsync` + `RemoveRange`，性能差

**文件:** `src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs`

- **问题:** `DeleteByModelNameAsync`、`DeleteExpiredAsync`、`DeleteByModelVersionAsync` 均先将所有匹配行加载进内存，再调用 `RemoveRange`。数据量大时会大量消耗内存并产生 N+1 的 DELETE 语句。同一项目中 `AuditLogRepository` 和 `MatchingFillTaskRepository` 已经正确使用 `ExecuteDeleteAsync`。
- **修复:** 统一改为 `await _dbContext.EmbeddingCaches.Where(...).ExecuteDeleteAsync()`。

---

### P2-7. `StrictReusePreview` 对同一 `sourceTaskId` 无幂等保护

**文件:** `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`（严格复用 Preview 段）

- **问题:** 前端因网络问题重试时，会触发多次全量 Embedding 匹配，造成无意义的 AI 资源消耗。
- **建议:** 对 `(sourceTaskId, targetFileId)` 组合做短期结果缓存（如 5 分钟内相同入参返回缓存结果），或在数据库中持久化 Preview 快照。

---

### P2-8. `CoreProviderAdapters` Entity→Model 映射代码大量重复

**文件:** `src/AcceptanceSpecSystem.Data/Providers/CoreProviderAdapters.cs`

- **问题:** 多个 Provider 类内的 Entity-to-Model 映射方法结构高度重复，修改实体结构时需多处同步更改。
- **建议:** 提取为各实体类的扩展方法 `ToModel()`，或引入 AutoMapper Profile 集中管理映射。

---

### P2-9. `MatchingApiControllerBase.HandleAsync` 与全局中间件异常处理职责不清晰

**文件:** `src/AcceptanceSpecSystem.Api/Controllers/MatchingApiControllerBase.cs`

- **问题:** 控制器层只处理 `MatchingApiException`，其余异常交给全局中间件，导致同一请求链路中异常可能被记录两次；无注释说明这是有意设计。
- **建议:** 在方法上添加注释明确说明异常处理边界；确保 `OperationCanceledException` 在中间件层不被记录为 Error。

---

## P3 — 轻微问题

### P3-1. `IAiServiceSelector` / `IAiServiceConfigProvider` 接口无中文注释

**文件:** `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/IAiServiceSelector.cs`；`src/AcceptanceSpecSystem.Core/AI/SemanticKernel/IAiServiceConfigProvider.cs`

- **问题:** 按全局规范，接口与公开方法需有中文 XML doc 说明，两个接口仅有签名，无注释。

---

### P3-2. `SemanticKernelOptions` 结构过于单薄，未规划扩展点

**文件:** `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/SemanticKernelOptions.cs`

- **问题:** 当前仅含 Azure OpenAI 版本号一个字段。若后续添加 Ollama / LM Studio 等提供商选项，结构会随意堆叠，建议提前规划嵌套 Options 结构（如 `AzureOptions`、`OllamaOptions` 子节点）。

---

### P3-3. `IPromptTemplateProvider.GetOrCreateSystemAsync` 参数过多

**文件:** `src/AcceptanceSpecSystem.Core/Matching/Interfaces/IPromptTemplateProvider.cs`

```csharp
Task<PromptTemplateModel> GetOrCreateSystemAsync(
    PromptTemplateScene scene, string name, string displayName,
    string defaultContent, CancellationToken cancellationToken = default);
```

- **问题:** 4 个业务参数内聚性强，建议引入 `SystemPromptTemplateSpec` Value Object 封装，提升可读性与可维护性。

---

### P3-4. `MatchingTaskController` 的 `taskId` 参数无格式校验

**文件:** `src/AcceptanceSpecSystem.Api/Controllers/MatchingTaskController.cs`

- **问题:** 路由参数 `taskId` 为字符串，无格式约束（如 GUID 格式），任意字符串均会进入数据库查询，增加不必要的 DB 负担和日志噪音。
- **建议:** 改为 `[FromRoute] Guid taskId`，或增加正则路由约束 `{taskId:regex(^[a-f0-9]{{32}}$)}`。

---

### P3-5. 实体默认值使用属性初始化器赋 `DateTime.UtcNow`，时间可能偏移

**文件:** `src/AcceptanceSpecSystem.Data/Entities/AuthorizationEntities.cs` 等多个实体

```csharp
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
```

- **问题:** 初始化器仅在对象实例化时执行一次，若对象被复用或延迟保存，`CreatedAt` 会记录构造时间而非实际写入时间。`UtcUsageConventionTests` 检查 `DateTime.Now`，无法检测此问题。
- **建议:** 在 `SaveChanges` 拦截器或 Repository 写入方法中统一设置；或使用 `HasDefaultValueSql("UTC_TIMESTAMP(6)")` 由数据库生成，删除实体初始化器中的赋值。

---

### P3-6. `CanApplyMatchedSpec` 跳过统计语义不透明

**文件:** `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`

- **问题:** `totalSkipped` 计数器同时用于「SpecId 为空跳过」和「置信度不足跳过」两种语义，响应中无法区分两类跳过原因，问题排查困难。
- **建议:** 用具名计数器区分，并在响应统计字段中分别暴露。

---

## 测试层问题

### T1. `ReviewRegressionTests` 依赖源码文件路径扫描，CI 环境脆弱

**文件:** `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- **问题:** 测试通过硬编码相对路径遍历 `.cs` 文件进行架构检查，在 CI 中因工作目录不同可能导致路径解析失败，或因文件被重命名而产生漏报。
- **建议:** 改用 NetArchTest / ArchUnitNET 等架构测试库通过反射分析程序集，不依赖文件系统路径。

---

### T2. `UtcUsageConventionTests` 同样依赖目录遍历，CI 兼容性风险相同

**文件:** `tests/AcceptanceSpecSystem.Api.Tests/UtcUsageConventionTests.cs`

- **建议:** 改为通过 Roslyn 分析器（Analyzer）或反射扫描已编译程序集，脱离对文件系统的依赖。

---

### T3. 测试类共享 `ApiWebApplicationFactory`，全局状态可能导致隐式依赖

**文件:** `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`

- **问题:** `IClassFixture<ApiWebApplicationFactory>` 在同一测试类内共享；跨测试类时各自创建实例，但若有全局种子数据（如默认用户、角色），测试间存在隐式依赖风险。
- **建议:** 考虑使用 `ICollectionFixture` 统一管理共享资源；每个测试方法使用随机 ID 隔离数据（部分已实现，如 `ownership-{Guid.NewGuid():N}`）。

---

### T4. `ExceptionHandlingMiddlewareTests` 缺乏 HTTP 状态码端到端映射验证

**文件:** `tests/AcceptanceSpecSystem.Api.Tests/ExceptionHandlingMiddlewareTests.cs`

- **问题:** 测试主要验证异常类型与消息，缺少对实际 HTTP 状态码（如 400/401/403/500）的端到端断言，中间件映射错误时测试可能无法及时发现。
- **建议:** 补充通过 `HttpClient` 触发真实请求并断言 `response.StatusCode` 的集成测试。

---

## 前端问题

### F1. `web/src/api/matching.ts` 与后端拆分后的控制器路由同步状态未验证

**文件:** `web/src/api/matching.ts`

- **问题:** 后端控制器已从单一 `MatchingController` 拆分为四个子控制器，各路由前缀可能发生变化。前端 API 模块是否完整同步需人工确认；若存在路由遗漏，会产生运行时 404 而非编译期错误。
- **建议:** 为前端 API 模块增加集成测试或契约测试（如 Pact），确保前后端路由一致。

---

### F2. `StrictReuseDialog.vue` 权限 props 全部默认为 `false`，无开发期警告

**文件:** `web/src/views/smart-fill/components/StrictReuseDialog.vue`

- **问题:** `canPreview`、`canExecute`、`canDownload` 默认均为 `false`；父组件忘记传入时，对话框所有操作按钮不可见，用户困惑，且无任何开发期提示。
- **建议:** 增加 prop validator 或在 `onMounted` 中增加 `console.warn` 检查，开发环境下提示父组件忘记传权限 prop。

---

### F3. `ScoreDetailDialog.vue` 的 `computedDiff` 在大量条目时无性能保护

**文件:** `web/src/views/smart-fill/components/ScoreDetailDialog.vue`

- **问题:** `computedDiff` 对当前 item 所有候选项做 inline diff 计算，若候选项数量较多（如 100+），每次渲染均重新计算，会导致 UI 卡顿。`clearInlineDiffCache()` 已正确在 `onUnmounted` 和 `watch` 中调用，缓存清理无内存泄漏，但无最大条目数限制。
- **建议:** 增加 `computed` 的懒计算（只计算可见区域）或限制最多展示 N 个候选项的 diff。

---

### F4. `web/src/utils/http/index.ts` 审计 Token 在 `beforeRequestCallback` 分支下不注入

**文件:** `web/src/utils/http/index.ts`，第 96–103 行

```typescript
if (typeof config.beforeRequestCallback === "function") {
  config.beforeRequestCallback(config);
  return config; // 提前返回，未注入审计 headers
}
```

- **问题:** 当请求显式传入 `beforeRequestCallback` 时，`X-Client-Trace-Id`、`X-Client-Id`、`X-Frontend-Route` 三个审计 header 不会被注入，导致该请求在后端审计日志中缺少客户端上下文信息，影响链路追踪完整性。
- **建议:** 将审计 header 注入移到 `beforeRequestCallback` 检查之前（无论是否有回调都先注入）；或在 `beforeRequestCallback` 调用后补充注入未被覆盖的 header。

---

### F5. `web/src/store/modules/user.ts` 的 `logOut` 方法未清理本地文件缓存

**文件:** `web/src/store/modules/user.ts`

- **问题:** `logOut` 清理了 token 和 store 状态，但若前端存在本地文件引用（如上传后的 `fileId` 缓存在组件状态中）、路由守卫缓存或 Pinia 持久化状态，退出后重新登录的另一账号可能读到上一账号的残留状态。
- **建议:** `logOut` 调用时重置所有 Pinia store（`resetState()`），或使用 `router.replace` 强制清除组件实例状态。

---

## 亮点（值得肯定的设计）

1. **控制器拆分彻底:** `MatchingController` 已删除，替换为 `MatchingPreviewController`、`MatchingExecutionController`、`MatchingTaskController`、`MatchingReuseController`，每个控制器职责单一，构造函数依赖不超过 3 个，符合 SRP。

2. **Core/Data 解耦彻底:** `IAiServiceConfigProvider`、`IKeywordDataProvider`、`ISynonymDataProvider`、`ITextProcessingConfigProvider`、`IPromptTemplateProvider` 将 Core 层与 EF Core 完全隔离；`CoreProviderBoundaryTests` 以纯内存 Stub 验证，单元测试无需数据库。

3. **UTC 时间戳三层保障:** `UtcUsageConventionTests` 扫描源码禁止 `DateTime.Now`、`UtcTimestampTests` 验证实体默认值、`SpecsCreateUtcTests` 进行 API 集成验证，从根源杜绝时区 Bug。

4. **SSE 并行写入串行化:** `MatchingWorkflowService` 中 SSE 输出用 `SemaphoreSlim(1,1)` 序列化，避免并发写入响应流，技术处理正确。

5. **LLM 流式熔断机制:** `Parallel.ForEachAsync` 内通过 `circuitOpened` volatile 标志实现轻量熔断，避免单次 LLM 请求雪崩影响全批，设计思路合理。

6. **全局 Fallback 鉴权策略:** `Program.cs` 中 `options.FallbackPolicy = RequireAuthenticatedUser()`，所有未显式标注的接口默认需要认证，无安全遗漏。

7. **`ExceptionHandlingMiddleware` SSE 场景处理正确:** 检查 `context.Response.HasStarted` 后不再写入响应，避免流式场景下响应冲突。

8. **数据范围隔离完整:** `AuthDataScopeService` 支持 `All / Self / OrgNode / SubTree / CustomNodes` 多种范围类型，`MatchingTaskOwnershipTests` 覆盖跨用户隔离安全场景。

9. **Ollama 地址规范化有测试:** `OllamaNativeChatCompletionService` 增加端点规范化处理，`OllamaNativeChatCompletionServiceTests` 覆盖多种 URL 格式。

10. **仓储查询下推:** `AcceptanceSpecRepository` 通过 `AcceptanceSpecQueryOptions` 将作用域、关键字、分页下推到数据库层，修复了原来全表加载后内存过滤的问题。

---

## 综合评分

| 层级 | 评分 | 主要扣分点 |
|------|------|------------|
| API 控制器层 | 8/10 | SSE 端点缺审计日志；异常处理边界不清晰 |
| Service 层 | 6/10 | MatchingWorkflowService 职责过重；AuthDataScopeService 无缓存 |
| Core 层 | 9/10 | 依赖倒置彻底；仅 SemanticKernelFactory 有强制解引用风险 |
| Data 层 | 6/10 | 硬编码密码（P0）；PromptTemplateProvider 违规（P0）；并发问题多处 |
| 前端 | 7/10 | 审计 header 漏注入；logOut 状态清理不完整 |
| 测试层 | 7/10 | 架构测试依赖文件路径；缺少 HTTP 状态码端到端断言 |
| **综合** | **7/10** | 架构方向正确，核心安全问题需优先修复 |

---

## 行动清单

### 上线前必须处理（P0）

- [ ] 删除 `AppDbContext.DefaultConnectionString` 硬编码密码，改为启动失败（用户本轮明确排除）
- [x] 修复 CORS 在无配置时不允许 fallback 全开放
- [ ] 从 `PromptTemplateProvider` 移除 `AppDbContext`，并进一步改为“由调用方统一提交”
- [ ] `EmbeddingCache` 唯一索引加入 `ModelVersion`，并捕获并发插入异常
- [ ] `MatchingFillTask` 增加 `RowVersion` 乐观并发标记
- [ ] API Key 解密失败时升级为统一错误/错误级日志，彻底替代兼容回退

### 下一迭代（P1）

- [ ] 拆分 `MatchingWorkflowService` 为 3-4 个聚焦服务类
- [x] `SemanticKernelServiceFactory` 强制解引用改为启动期 Guard
- [x] `AuthDataScopeService` 组织树查询增加内存缓存（TTL 5 分钟）
- [x] `Program.cs` 迁移失败时让应用启动失败，不继续执行种子逻辑（当前实现已满足）
- [x] `MatchingExecutionController` SSE 端点补充审计日志
- [x] `MatchingFillTask` `CompanyId`/`CreatedByUserId` 为 null 时禁止下载的守卫
- [x] `ExceptionHandlingMiddleware` 扩大 `OperationCanceledException` 捕获范围

### 技术债登记（P2/P3）

- [x] `AcceptanceSpecQueryOptions` 增加 PageSize 上限保护
- [x] `AuthSeedOptions` 启动期非空校验
- [x] `SynonymService` 静态缓存改为 `SemaphoreSlim` double-check 或 `IMemoryCache`
- [ ] `TextProcessingConfigRepository.GetConfigAsync` 加数据库唯一约束防并发重复插入（当前仅完成进程内串行化）
- [x] `AuthAccessService` 移除直接 `AppDbContext` 注入
- [x] `EmbeddingCacheRepository` 批量删除改用 `ExecuteDeleteAsync`
- [ ] `CoreProviderAdapters` 映射代码提取为扩展方法
- [x] 前端审计 header 在 `beforeRequestCallback` 分支下补充注入
- [ ] `logOut` 进一步统一重置所有 Pinia store（当前已清理关键权限/路由缓存）
- [ ] `ReviewRegressionTests` / `UtcUsageConventionTests` 改为不依赖文件路径的架构测试
- [x] `StrictReuseDialog.vue` 权限 props 增加开发期警告
- [x] `MatchingTaskController` `taskId` 增加格式约束
