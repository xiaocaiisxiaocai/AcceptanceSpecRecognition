using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;

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
        services.Configure<AuditLogOptions>(
            configuration.GetSection(AuditLogOptions.SectionName));
        services.Configure<EmbeddingCacheCleanupOptions>(
            configuration.GetSection(EmbeddingCacheCleanupOptions.SectionName));
        services.Configure<EmbeddingCacheWarmupOptions>(
            configuration.GetSection(EmbeddingCacheWarmupOptions.SectionName));
        services.Configure<DatabaseBackupOptions>(
            configuration.GetSection(DatabaseBackupOptions.SectionName));
        services.Configure<AiServiceTestOptions>(
            configuration.GetSection(AiServiceTestOptions.SectionName));
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
        services.AddSingleton<IAuthTokenService, AuthTokenService>();
        services.AddSingleton<IAuthPasswordService, AuthPasswordService>();
        services.AddScoped<IAuthAccessService, AuthAccessService>();
        services.AddScoped<IAuthDataScopeService, AuthDataScopeService>();
        services.AddScoped<IAuthSessionValidationService, AuthSessionValidationService>();
        services.AddScoped<AuthPermissionQueryService>();
        services.AddScoped<IAuthRoleAppService, AuthRoleAppService>();
        services.AddScoped<IOrgUnitAppService, OrgUnitAppService>();
        services.AddScoped<ISystemUserAppService, SystemUserAppService>();

        // ── 文件存储与文档处理 ──
        services.AddSingleton<IFileStorageService, FileStorageService>();
        services.AddSingleton<DocumentServiceFactory>();
        services.AddScoped<IFileCompareService, FileCompareService>();
        services.AddScoped<DocumentFileAccessService>();
        services.AddScoped<DocumentTableAccessService>();
        services.AddScoped<MatchingResultWriteBackService>();

        // ── 匹配与智能填充 ──
        services.AddSingleton<BatchPreviewProgressTracker>();
        services.AddScoped<MatchingConfigResolver>();
        services.AddScoped<MatchingCandidateProvider>();
        services.AddScoped<MatchingWorkflowSupportService>();
        services.AddScoped<IMatchingPreviewAppService, MatchingPreviewAppService>();
        services.AddScoped<IMatchingLlmStreamAppService, MatchingLlmStreamAppService>();
        services.AddScoped<IMatchingFillExecutionAppService, MatchingFillExecutionAppService>();
        services.AddScoped<IMatchingExecutionAppService, MatchingExecutionAppService>();
        services.AddScoped<ISmartFillSpecBackfillAppService, SmartFillSpecBackfillAppService>();
        services.AddScoped<IMatchingTaskAppService, MatchingTaskAppService>();
        services.AddScoped<MatchingTaskSnapshotService>();
        services.AddSingleton<MatchingApprovalTokenService>();

        // ── 文档导入 ──
        services.AddScoped<IDocumentFileAppService, DocumentFileAppService>();
        services.AddScoped<IDocumentImportAppService, DocumentImportAppService>();
        services.AddScoped<ImportDuplicateDetectionService>();
        services.AddScoped<IRuleBasedMappingStrategy, RuleBasedMappingStrategy>();
        services.AddScoped<IDocumentIntelligenceService, DocumentIntelligenceService>();

        // ── 批量回复 ──
        services.AddSingleton<BatchReplySessionService>();
        services.AddScoped<IBatchReplyAppService, BatchReplyAppService>();

        // ── Embedding 缓存 ──
        services.AddScoped<SpecSemanticSearchService>();
        services.AddSingleton<EmbeddingCacheWarmupManager>();
        services.AddScoped<SpecEmbeddingCacheService>();
        services.AddScoped<IEmbeddingCacheWarmupExecutor>(sp =>
            sp.GetRequiredService<SpecEmbeddingCacheService>());

        // ── 数据库备份 ──
        services.AddSingleton<DatabaseBackupManager>();
        services.AddScoped<IDatabaseBackupExecutor, MySqlDumpDatabaseBackupExecutor>();

        // ── 仪表盘与历史 ──
        services.AddScoped<IDashboardAppService, DashboardAppService>();
        services.AddScoped<IExecutionHistoryAppService, ExecutionHistoryAppService>();
        services.AddScoped<ExecutionHistoryAppService>();

        // ── 系统初始化 ──
        services.AddScoped<SystemPromptTemplateInitializer>();

        // ── AI / Semantic Kernel ──
        services.AddScoped<IAiServiceSelector, AiServiceSelector>();
        services.AddSingleton<ISemanticKernelServiceFactory, SemanticKernelServiceFactory>();
        services.AddScoped<IEmbeddingService, SemanticKernelEmbeddingService>();
        services.AddScoped<PromptTemplateValidationService>();
        services.AddScoped<LlmMatchingAssistService>();
        services.AddScoped<ILlmReviewService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        services.AddScoped<ILlmEquivalenceAdjudicationService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());
        services.AddScoped<ILlmCandidateRerankService>(sp => sp.GetRequiredService<LlmMatchingAssistService>());

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

        services.AddScoped<IMatchingService>(sp => new SemanticKernelMatchingService(
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<ILogger<SemanticKernelMatchingService>>(),
            evidenceBuilder: new MatchEvidenceBuilder(sp.GetRequiredService<SemanticConflictScanner>()),
            llmEquivalenceAdjudicationService: sp.GetRequiredService<ILlmEquivalenceAdjudicationService>(),
            llmCandidateRerankService: sp.GetRequiredService<ILlmCandidateRerankService>(),
            canonicalizer: sp.GetRequiredService<ISpecCanonicalizer>()));

        // ── 文本处理 ──
        services.AddScoped<ITextPreprocessingPipeline, MinimalTextPreprocessingPipeline>();

        // ── 后台服务 ──
        services.AddHostedService<AuditLogCleanupService>();
        services.AddHostedService<EmbeddingCacheCleanupService>();
        services.AddHostedService<EmbeddingCacheWarmupService>();
        services.AddHostedService<DatabaseBackupService>();

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
        services.AddScoped<IDocumentTemplateRepository, DocumentTemplateRepository>();
        services.AddScoped<ISystemUserRepository, SystemUserRepository>();
        services.AddScoped<IAuthRoleLookupRepository, AuthRoleLookupRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IMatchingFillTaskRepository, MatchingFillTaskRepository>();
        services.AddScoped<IExecutionHistoryRecordRepository, ExecutionHistoryRecordRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
