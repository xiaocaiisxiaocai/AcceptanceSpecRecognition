using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api;

/// <summary>
/// Api 层服务模块化注册扩展方法
/// </summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Api 层所有业务服务
    /// </summary>
    public static IServiceCollection AddApiLayerServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── 配置选项 ──
        services.Configure<JwtAuthOptions>(
            configuration.GetSection(JwtAuthOptions.SectionName));
        services.Configure<BrowserAuthOptions>(
            configuration.GetSection(BrowserAuthOptions.SectionName));
        services.Configure<AuditLogOptions>(
            configuration.GetSection(AuditLogOptions.SectionName));
        services.Configure<ExecutionHistoryRetentionOptions>(
            configuration.GetSection(ExecutionHistoryRetentionOptions.SectionName));
        services.Configure<AuthRefreshSessionRetentionOptions>(
            configuration.GetSection(AuthRefreshSessionRetentionOptions.SectionName));
        services.Configure<EmbeddingCacheCleanupOptions>(
            configuration.GetSection(EmbeddingCacheCleanupOptions.SectionName));
        services.Configure<EmbeddingCacheWarmupOptions>(
            configuration.GetSection(EmbeddingCacheWarmupOptions.SectionName));
        services.AddOptions<ResourceBudgetOptions>()
            .Bind(configuration.GetSection(ResourceBudgetOptions.SectionName))
            .Validate(options => options.MaxConcurrentDocumentParsers > 0, "文档解析并发必须大于 0")
            .Validate(options => options.MaxConcurrentDocumentWriters > 0, "文档写回并发必须大于 0")
            .Validate(options => options.MaxConcurrentHighCostMatching > 0, "高成本匹配并发必须大于 0")
            .Validate(options => options.MaxDocumentBytes > 0, "文档字节预算必须大于 0")
            .Validate(options => options.MaxWriteOperations > 0, "写回操作预算必须大于 0")
            .Validate(options => options.MaxMatchingItems > 0, "匹配项预算必须大于 0")
            .Validate(options => options.MaxDuplicateCandidates > 0, "重复分析候选预算必须大于 0")
            .Validate(options => options.MaxDuplicatePairComparisons > 0, "重复分析比较预算必须大于 0")
            .Validate(options => options.MaxFileCompareCells > 0, "文件比较节点预算必须大于 0")
            .Validate(options => options.MaxFileCompareDiffItems > 0, "文件比较差异预算必须大于 0")
            .Validate(options => options.MaxFileCompareResultBytes > 0, "文件比较结果字节预算必须大于 0")
            .ValidateOnStart();
        services.AddOptions<FileCompareTemporaryStorageOptions>()
            .Bind(configuration.GetSection(FileCompareTemporaryStorageOptions.SectionName))
            .Validate(options => options.RetentionHours > 0, "文件比较临时文件保留时间必须大于 0")
            .Validate(options => options.CleanupIntervalMinutes > 0, "文件比较临时文件清理间隔必须大于 0")
            .Validate(options => options.HeartbeatSeconds > 0, "文件比较临时文件心跳间隔必须大于 0")
            .Validate(
                options => options.RetentionHours > 0 &&
                           options.HeartbeatSeconds <=
                           checked((long)options.RetentionHours * 60 * 60 / 4),
                "文件比较临时文件心跳间隔不能超过保留时间的四分之一")
            .ValidateOnStart();
        services.AddOptions<BatchReplyCleanupOptions>()
            .Bind(configuration.GetSection(BatchReplyCleanupOptions.SectionName))
            .Validate(options => options.InitialDelaySeconds >= 0, "InitialDelaySeconds 不能小于 0")
            .Validate(options => options.CleanupIntervalMinutes > 0, "CleanupIntervalMinutes 必须大于 0")
            .Validate(options => options.SessionRetentionMinutes > 0, "SessionRetentionMinutes 必须大于 0")
            .Validate(options => options.ArtifactRetentionMinutes > 0, "ArtifactRetentionMinutes 必须大于 0")
            .ValidateOnStart();
        services.AddOptions<OrphanFileInspectionOptions>()
            .Bind(configuration.GetSection(OrphanFileInspectionOptions.SectionName))
            .Validate(options => options.InitialDelaySeconds >= 0, "InitialDelaySeconds 不能小于 0")
            .Validate(options => options.InspectionIntervalMinutes > 0, "InspectionIntervalMinutes 必须大于 0")
            .Validate(options => options.GracePeriodHours > 0, "GracePeriodHours 必须大于 0")
            .ValidateOnStart();
        services.AddOptions<WordFileDeletionCleanupOptions>()
            .Bind(configuration.GetSection(WordFileDeletionCleanupOptions.SectionName))
            .Validate(options => options.InitialDelaySeconds >= 0, "InitialDelaySeconds 不能小于 0")
            .Validate(options => options.CleanupIntervalMinutes > 0, "CleanupIntervalMinutes 必须大于 0")
            .Validate(options => options.BatchSize is > 0 and <= 1000, "BatchSize 必须在 1 到 1000 之间")
            .ValidateOnStart();
        services.Configure<DatabaseBackupOptions>(
            configuration.GetSection(DatabaseBackupOptions.SectionName));
        services.Configure<AiServiceTestOptions>(
            configuration.GetSection(AiServiceTestOptions.SectionName));
        services.AddOptions<AiServiceReadinessOptions>()
            .Bind(configuration.GetSection(AiServiceReadinessOptions.SectionName))
            .Validate(options => options.StatusTtlSeconds > 0, "AI readiness TTL 必须大于 0")
            .Validate(options => options.ProbeTimeoutSeconds > 0, "AI readiness 探测超时必须大于 0")
            .Validate(options => options.MaxConcurrentProbes > 0, "AI readiness 并发必须大于 0")
            .ValidateOnStart();
        services.AddOptions<DashboardOptions>()
            .Bind(configuration.GetSection(DashboardOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.TimeZoneId), "Dashboard 时区不能为空")
            .Validate(options => DashboardTimeZoneResolver.TryResolveFixedOffset(options.TimeZoneId, out _),
                "Dashboard 时区必须有效，且当前业务统计窗口内 UTC 偏移必须保持稳定")
            .ValidateOnStart();
        services.Configure<SmartConfigurationOptions>(
            configuration.GetSection(SmartConfigurationOptions.SectionName));
        services.Configure<RequestTracingOptions>(
            configuration.GetSection(RequestTracingOptions.SectionName));
        services.Configure<SlowQueryOptions>(
            configuration.GetSection(SlowQueryOptions.SectionName));
        services.AddSingleton<IValidateOptions<AuthSeedOptions>, AuthSeedOptionsValidator>();
        services.AddOptions<AuthSeedOptions>()
            .Bind(configuration.GetSection(AuthSeedOptions.SectionName))
            .ValidateOnStart();
        services.Configure<SemanticKernelOptions>(
            configuration.GetSection(SemanticKernelOptions.SectionName));

        // ── 认证与授权 ──
        services.AddScoped<IAuthTokenService, AuthTokenService>();
        services.AddScoped<IBrowserAuthSecurityService, BrowserAuthSecurityService>();
        services.AddSingleton<IMatchingApprovalTokenProtector, MatchingApprovalTokenProtector>();
        services.AddSingleton<IAuthPermissionSeedCatalog, AuthPermissionSeedCatalog>();

        // ── 文件存储与文档处理 ──
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFileStorageService, FileStorageService>();
        services.AddSingleton<IFileCompareTemporaryStorage, FileCompareTemporaryStorage>();
        services.AddScoped<DocumentFileAccessService>();
        services.AddScoped<IDocumentFileAccessService>(sp => sp.GetRequiredService<DocumentFileAccessService>());
        services.AddScoped<DocumentTableAccessService>();
        services.AddScoped<IDocumentImportTableReader>(sp => sp.GetRequiredService<DocumentTableAccessService>());
        services.AddScoped<IBatchReplyDocumentTablePort>(sp => sp.GetRequiredService<DocumentTableAccessService>());
        services.AddSingleton<DocumentServiceFactory>();
        services.AddSingleton<IFileCompareDocumentParser, FileCompareDocumentParser>();
        services.AddScoped<ISmartConfigurationFileAccessService, SmartConfigurationFileAccessService>();
        services.AddScoped<MatchingResultWriteBackService>();
        services.AddScoped<IMatchingResultWriteBackPort>(sp => sp.GetRequiredService<MatchingResultWriteBackService>());
        services.AddScoped<IBatchReplyWriteBackPort>(sp => sp.GetRequiredService<MatchingResultWriteBackService>());
        services.AddScoped<IBatchReplyExecutionHistoryPort, BatchReplyExecutionHistoryAdapter>();
        services.AddSingleton<IBatchReplyCleanupStore, BatchReplyCleanupFileStore>();
        services.Replace(ServiceDescriptor.Singleton<IBatchReplyDistributedLockProvider, MySqlBatchReplyDistributedLockProvider>());
        services.AddSingleton<IOrphanFileStore, OrphanFileStore>();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BatchReplyCleanupOptions>>().Value;
            return new BatchReplyRetentionPolicy(
                TimeSpan.FromMinutes(options.SessionRetentionMinutes),
                TimeSpan.FromMinutes(options.ArtifactRetentionMinutes));
        });

        // ── 匹配与智能填充 ──

        // ── 文档导入 ──
        services.AddScoped<IRuleBasedMappingStrategy, RuleBasedMappingStrategy>();
        services.AddScoped<IDocumentIntelligenceService, DocumentIntelligenceService>();

        // ── 批量回复 ──

        // ── Embedding 缓存 ──
        services.AddSingleton<IEmbeddingCacheWarmupTrigger, EmbeddingCacheWarmupTrigger>();
        services.AddSingleton<IImportWarmupTrigger, ImportWarmupTriggerAdapter>();
        services.AddScoped<SpecEmbeddingCacheService>();
        services.AddScoped<IMatchingEmbeddingCache>(sp => sp.GetRequiredService<SpecEmbeddingCacheService>());
        services.AddScoped<ISpecSemanticEmbeddingCache>(sp => sp.GetRequiredService<SpecEmbeddingCacheService>());
        services.AddScoped<IImportEmbeddingCache>(sp => sp.GetRequiredService<SpecEmbeddingCacheService>());
        services.AddScoped<IEmbeddingCacheWarmupExecutor>(sp =>
            sp.GetRequiredService<SpecEmbeddingCacheService>());

        // ── 数据库备份 ──
        services.AddSingleton<IMySqlDumpProcessRunner, MySqlDumpProcessRunner>();
        services.AddScoped<IDatabaseBackupExecutor, MySqlDumpDatabaseBackupExecutor>();

        // ── 仪表盘与历史 ──

        // ── AI / Semantic Kernel ──
        services.AddScoped<IAiServiceSelector, AiServiceSelector>();
        services.AddSingleton<AiServiceReadinessProbeScheduler>();
        services.AddSingleton<IAiServiceReadinessProbeScheduler>(sp =>
            sp.GetRequiredService<AiServiceReadinessProbeScheduler>());
        services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<AiServiceReadinessProbeScheduler>());
        services.AddSingleton<ISafeAiHttpClientFactory, SafeAiHttpMessageHandlerFactory>();
        services.AddSingleton<ISemanticKernelServiceFactory, SemanticKernelServiceFactory>();
        services.AddScoped<IEmbeddingService, SemanticKernelEmbeddingService>();
        services.AddScoped<PromptTemplateValidationService>();
        services.AddScoped<LlmMatchingAssistService>();
        services.AddScoped<ILlmReviewService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        services.AddScoped<ILlmEquivalenceAdjudicationService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        services.AddScoped<ILlmCandidateRerankService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        services.AddScoped<ILlmDocumentStructureAdjudicationService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        services.AddScoped<ILlmColumnSemanticRecallService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());

        // 规范化器与冲突扫描器：规则初始化后只读，注册为单例。
        // 可通过 SmartFillKnowledge:RulesPath 指向外置品牌/单位 JSON。
        services.AddSingleton<ISpecCanonicalizer>(_ =>
        {
            var rulesPath = configuration.GetValue<string>("SmartFillKnowledge:RulesPath");
            return string.IsNullOrWhiteSpace(rulesPath)
                ? new SpecCanonicalizer()
                : new SpecCanonicalizer(rulesPath);
        });
        services.AddSingleton<SemanticConflictScanner>();

        services.AddScoped<IMatchingService>(sp =>
        {
            var inner = new SemanticKernelMatchingService(
                sp.GetRequiredService<IEmbeddingService>(),
                sp.GetRequiredService<ILogger<SemanticKernelMatchingService>>(),
                evidenceBuilder: new MatchEvidenceBuilder(sp.GetRequiredService<SemanticConflictScanner>()),
                llmEquivalenceAdjudicationService: sp.GetRequiredService<ILlmEquivalenceAdjudicationService>(),
                llmCandidateRerankService: sp.GetRequiredService<ILlmCandidateRerankService>(),
                canonicalizer: sp.GetRequiredService<ISpecCanonicalizer>());
            return new ResourceGovernedMatchingService(
                inner,
                sp.GetRequiredService<IResourceBudgetGovernor>());
        });

        // ── 文本处理 ──
        services.AddScoped<ITextPreprocessingPipeline, MinimalTextPreprocessingPipeline>();

        // ── 后台服务 ──
        services.AddHostedService<AuditLogCleanupService>();
        services.AddHostedService<ExecutionHistoryCleanupService>();
        services.AddHostedService<AuthRefreshSessionCleanupService>();
        services.AddHostedService<EmbeddingCacheCleanupService>();
        services.AddHostedService<EmbeddingCacheWarmupService>();
        services.AddHostedService<DatabaseBackupService>();
        services.AddHostedService<BatchReplyCleanupHostedService>();
        services.AddHostedService<MatchingFileMutationRecoveryHostedService>();
        services.AddHostedService<OrphanFileInspectionHostedService>();
        services.AddHostedService<WordFileDeletionCleanupHostedService>();
        services.AddHostedService<FileCompareTemporaryCleanupHostedService>();

        return services;
    }

    /// <summary>
    /// 注册数据层仓储服务
    /// </summary>
    public static IServiceCollection AddDataRepositories(this IServiceCollection services)
    {
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProcessRepository, ProcessRepository>();
        services.AddScoped<IMachineModelRepository, MachineModelRepository>();
        services.AddScoped<IAcceptanceSpecRepository, AcceptanceSpecRepository>();
        services.AddScoped<IEmbeddingCacheRepository, EmbeddingCacheRepository>();
        services.AddScoped<IWordFileRepository, WordFileRepository>();
        services.AddScoped<IAiServiceConfigRepository, AiServiceConfigRepository>();
        services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddScoped<IColumnMappingRuleRepository, ColumnMappingRuleRepository>();
        services.AddScoped<ISmartStructureRoutingRuleRepository, SmartStructureRoutingRuleRepository>();
        services.AddScoped<IDocumentTemplateRepository, DocumentTemplateRepository>();
        services.AddScoped<ISystemUserRepository, SystemUserRepository>();
        services.AddScoped<IAuthRoleLookupRepository, AuthRoleLookupRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IMatchingFillTaskRepository, MatchingFillTaskRepository>();
        services.AddScoped<IExecutionHistoryRecordRepository, ExecutionHistoryRecordRepository>();
        services.AddScoped<IDocumentImportExecutionRepository, DocumentImportExecutionRepository>();
        services.AddScoped<IOrgUnitRepository, OrgUnitRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
