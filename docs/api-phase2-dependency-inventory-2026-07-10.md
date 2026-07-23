# API Phase 2 依赖清单（2026-07-10）

## 目的与判定规则

本清单记录 `Api/Controllers`、Action Filter 与 `Api/Services` 在 Phase 2 开始时的职责和持久化依赖。协议层允许保留 HTTP、SSE、下载、外部服务探测和宿主调度适配；数据库查询、事务、审计写入与跨资源编排必须迁入 Application。

## 控制器与 Filter 基线

| 模块 | 协议入口 | 基线直接依赖 | Phase 2 批次 | 目标 |
|---|---|---|---|---|
| 审计写入 | `AuditOperationFilter` | `IUnitOfWork` | 2.1 | Filter 只收集 HTTP 上下文并调用 Application 审计端口 |
| 审计查询/删除 | `AuditLogsController` | `IUnitOfWork` | 2.1 | 委派 Application 查询/删除用例 |
| 列映射配置 | `ColumnMappingRulesController` | `IUnitOfWork`、初始化器 | 2.1 | 查询、校验、写入、恢复默认值由 Application 用例拥有 |
| Prompt 配置 | `PromptTemplatesController` | `IUnitOfWork`、模板校验器 | 2.1 | 查询、校验、写入、重置由 Application 用例拥有 |
| 智能结构路由配置 | `SmartStructureRoutingRulesController` | `IUnitOfWork` | 2.1 | 查询、校验、写入由 Application 用例拥有 |
| AI 服务配置 | `AiServicesController` | `IUnitOfWork`、HTTP/AI 探测依赖 | 2.1 | 配置查询/写入迁入 Application；外部连通性与模型探测保留为 Api 适配 |
| 基础主数据 | Customers/Processes/MachineModels/Specs controllers | 已委派 Application 用例 | 2.1 | 架构测试阻止持久化依赖回流 |
| 文档 | Documents/FileCompare controllers | 文档应用服务；FileCompare 尚直接 `IUnitOfWork` | 2.2 | 文档批次处理，本批不改 |
| BatchReply | `BatchReplyController` | BatchReply 应用 façade | 2.3 | 本批不改 |
| 匹配/填充 | Matching controllers | 聚焦的匹配应用 façade | 2.4 | 本批不改 |
| 认证/RBAC | Auth 与 RBAC controllers | 认证/RBAC 应用服务；Auth 尚直接 `IUnitOfWork` | 2.5 | 本批明确禁止修改 |

## `Api/Services` 分类清单

- **2.1 配置/审计/基础管理**：`ColumnMappingRuleInitializer`、`SystemPromptTemplateInitializer`、`DashboardAppService`、配置控制器内的持久化编排、`AuditLogCleanupService`。其中初始化与配置写入应迁入 Application；hosted cleanup 的宿主循环可暂留 Api，但不得由协议入口直接持久化。
- **2.2 文档**：`DocumentFileAppService`、`DocumentImportAppService*`、`DocumentFileAccessService`、`DocumentTableAccessService`、`FileCompareService`、`UploadedDocumentPathResolver` 等。
- **2.3 BatchReply**：`BatchReplyAppService*`、`BatchReplySessionService*`、manifest/重复项处理相关服务。
- **2.4 匹配/填充**：`Matching*`、`StrictReuse*`、`SpecEmbeddingCacheService`、`SpecSemanticSearchService`、`ExecutionHistoryAppService` 等。
- **2.5 认证/RBAC/运维**：`Auth*`、`OrgUnitAppService`、`SystemUserAppService`、`DatabaseBackup*`、权限种子和会话服务。

## Phase 2.1 完成条件

1. 上述 2.1 控制器和 `AuditOperationFilter` 不出现 `AppDbContext`、`IUnitOfWork` 或 Repository 依赖。
2. 配置与审计 HTTP 路径、状态码及 JSON DTO 属性保持兼容。
3. Application 用例拥有查询、校验、事务和写入；Api 只进行协议映射和外部探测。
4. 架构测试将已迁移文件从临时白名单移除，并阻止回归。

## Phase 2.1 实施结果

- `AuditOperationFilter` 与 `AuditLogsController` 统一依赖 `IAuditTrailAppService`。
- 列映射、Prompt、智能结构路由和 AI 配置控制器分别依赖聚焦的 Application 用例；配置 DTO 迁入 `Application/Contracts`。
- AI 连接测试与远端模型探测保留在 Api，但配置读取通过只读 `AiServiceProbeConfig`，不暴露持久化实体。
- 配置/审计定向架构、JSON 契约与 API 回归 92/92 通过；全解决方案 Release warnings-as-errors 为 0/0。

## Phase 2.6 最终边界审计

### 协议层结论

- `Controllers`、Action Filter、Middleware、Authorization 源码已全量扫描，不再直接引用 `IUnitOfWork`、Repository 或 `AppDbContext`。
- 匹配控制器将 Claims 映射为 `MatchingUserContext`，LLM SSE 通过 `IMatchingEventStream` 适配；Application 不接收 `ClaimsPrincipal`、`HttpContext`、`HttpResponse` 或 `IFormFile`。
- `MatchingExecutionAppService` 迁移期聚合 façade 已删除，控制器直接依赖 `IMatchingLlmStreamAppService` 与 `IMatchingFillExecutionAppService`。
- 旧 `MatchingWorkflowService.cs` 文件名已收敛为 `MatchingWorkflowSupportService.cs`，其内容是 Application 内部协作组件，不再保留公开全能入口。
- `SpecSemanticSearchService`、`EmbeddingCacheWarmupManager`、权限种子持久化、审计/Embedding 清理决策均由 Application 拥有。

### Api/Services 最终允许分类

| 分类 | 允许实现 | 边界理由 |
|---|---|---|
| HTTP/安全协议适配 | `AuthTokenService`、`BrowserAuthSecurityService`、`HttpMatchingEventStream`、`MatchingApprovalTokenProtector`、`AuthPermissionSeedCatalog` | JWT、Cookie/CSRF、SSE、DataProtection、ASP.NET Action 元数据必须由宿主层适配 |
| Hosted 调度 | `AuditLogCleanupService`、`EmbeddingCacheCleanupService`、`EmbeddingCacheWarmupService`、`DatabaseBackupService`、`BatchReplyCleanupHostedService`、`OrphanFileInspectionHostedService` | 只负责周期、启动/停止与取消转发，业务删除/预热/备份/巡检决策委派 Application |
| 文件与持久化 adapter | `FileStorageService`、`DocumentFileAccessService`、`DocumentTableAccessService`、`MatchingResultWriteBackService`、`SmartConfigurationFileAccessService`、`SpecEmbeddingCacheService`、`BatchReplyCleanupFileStore`、`BatchReplyExecutionHistoryAdapter`、`OrphanFileStore` | 实现 Application 文件、parser/writer、缓存或持久化端口，不作为控制器业务入口 |
| 外部进程/宿主触发 | `DatabaseBackupExecutor`、`EmbeddingCacheWarmupTrigger`、`ImportWarmupTriggerAdapter` | 封装 mysqldump、进程内触发信号或宿主桥接 |
| 可观测性/基础设施 | `SlowQueryLoggingInterceptor`、`UploadFileValidation` | EF 拦截器与 HTTP 上传格式校验，无业务事务编排 |
| 纯健康检查 | `DatabaseHealthCheck`、`AiConfigHealthCheck`、`FileStorageHealthCheck` | 只探测基础设施可用性；按 Phase 2.6 范围明确保留，不伪装为 Application 用例 |

### 保留持久化依赖允许项

最终 `Api/Services` 中允许出现持久化类型的文件仅为：

- `DatabaseHealthCheck.cs`、`AiConfigHealthCheck.cs`：纯基础设施健康探针。
- `DocumentFileAccessService.cs`、`SmartConfigurationFileAccessService.cs`：Application 文件访问端口实现。
- `SpecEmbeddingCacheService.cs`：Application Embedding 缓存/语义搜索端口实现。

这些允许项不得被 Controller、Filter、Middleware 或 Authorization 直接注入；架构测试按文件名和职责建立只减不增的精确允许清单。

### Phase 2.6 验证结果

- 架构与功能定向组合：109/109 通过，覆盖最终边界守卫、匹配边界、认证/RBAC、语义搜索、Embedding 预热与失效、执行历史。
- 全解决方案 Release `TreatWarningsAsErrors=true`：0 warning / 0 error。
- `openspec validate refactor-application-boundaries-and-operational-lifecycle --strict`：通过。
- `git diff --check`：通过；未执行暂存、提交、推送或 `main` 操作。
