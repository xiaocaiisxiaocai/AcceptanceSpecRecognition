using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Application.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcceptanceApplicationLayer(this IServiceCollection services)
    {
        services.AddOptions<SmartConfigurationOptions>();
        services.AddOptions<ResourceBudgetOptions>();
        services.AddOptions<AcceptanceSpecSystem.Application.Options.DashboardOptions>();
        services.AddSingleton<IResourceBudgetGovernor, ResourceBudgetGovernor>();
        services.AddSingleton<IUploadedDocumentPathResolver, UploadedDocumentPathResolver>();
        services.AddScoped<IFileCompareService, FileCompareService>();
        services.AddScoped<IFileCompareAppService, FileCompareAppService>();
        services.AddScoped<IAiServiceConfigProvider, AiServiceConfigProvider>();
        services.AddScoped<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddScoped<AcceptanceSpecQueryService>();
        services.AddScoped<CustomerAppService>();
        services.AddScoped<ProcessAppService>();
        services.AddScoped<MachineModelAppService>();
        services.AddScoped<AcceptanceSpecAppService>();
        services.AddScoped<AcceptanceSpecContentVersionCoordinator>();
        services.AddScoped<IAcceptanceSpecCleanupAppService, AcceptanceSpecCleanupAppService>();
        services.AddSingleton<IAuthPasswordService, AuthPasswordService>();
        services.AddScoped<IAuthAccessService, AuthAccessService>();
        services.AddScoped<IAuthLoginAppService, AuthLoginAppService>();
        services.AddScoped<IAuthDataScopeService, AuthDataScopeService>();
        services.AddScoped<IAuthSessionValidationService, AuthSessionValidationService>();
        services.AddScoped<IAuthRefreshSessionService, AuthRefreshSessionService>();
        services.AddSingleton<ReferenceCountedKeyedLock<string>>();
        services.AddScoped<AuthPermissionQueryService>();
        services.AddScoped<IAuthRoleAppService, AuthRoleAppService>();
        services.AddScoped<IOrgUnitAppService, OrgUnitAppService>();
        services.AddScoped<ISystemUserAppService, SystemUserAppService>();
        services.AddScoped<IDashboardAppService, DashboardAppService>();
        services.AddSingleton<DatabaseBackupManager>();
        services.AddSingleton<BatchPreviewProgressTracker>();
        services.AddSingleton<AiServiceReadinessRegistry>();
        services.AddSingleton<IAiServiceRuntimeStatusReporter>(sp =>
            sp.GetRequiredService<AiServiceReadinessRegistry>());
        services.AddSingleton<IAiServiceRuntimeAvailability>(sp =>
            sp.GetRequiredService<AiServiceReadinessRegistry>());
        services.AddScoped<MatchingConfigResolver>();
        services.AddScoped<MatchingCandidateProvider>();
        services.AddScoped<MatchingWorkflowSupportService>();
        services.AddScoped<IMatchingPreviewAppService, MatchingPreviewAppService>();
        services.AddScoped<IMatchingLlmStreamAppService, MatchingLlmStreamAppService>();
        services.AddScoped<IMatchingFillExecutionAppService, MatchingFillExecutionAppService>();
        services.AddScoped<ISmartFillSpecBackfillAppService, SmartFillSpecBackfillAppService>();
        services.AddScoped<IMatchingTaskAppService, MatchingTaskAppService>();
        services.AddScoped<MatchingTaskSnapshotService>();
        services.AddSingleton<MatchingApprovalTokenService>();
        services.AddScoped<IExecutionHistoryAppService, ExecutionHistoryAppService>();
        services.AddScoped<ExecutionHistoryAppService>();
        services.AddScoped<SpecSemanticSearchService>();
        services.AddSingleton<EmbeddingCacheWarmupManager>();
        services.AddScoped<DocumentTemplateAppService>();
        services.AddScoped<IAuditTrailAppService, AuditTrailAppService>();
        services.AddScoped<IAuditLogRetentionAppService, AuditLogRetentionAppService>();
        services.AddScoped<IExecutionHistoryRetentionAppService, ExecutionHistoryRetentionAppService>();
        services.AddScoped<IEmbeddingCacheRetentionAppService, EmbeddingCacheRetentionAppService>();
        services.AddScoped<IColumnMappingRuleAppService, ColumnMappingRuleAppService>();
        services.AddScoped<ISmartStructureRoutingRuleAppService, SmartStructureRoutingRuleAppService>();
        services.AddScoped<IPromptTemplateAppService, PromptTemplateAppService>();
        services.AddScoped<IAiServiceConfigurationAppService, AiServiceConfigurationAppService>();
        services.AddScoped<IAiServiceSelectionAppService, AiServiceSelectionAppService>();
        services.AddScoped<SystemPromptTemplateInitializer>();
        services.AddScoped<ColumnMappingRuleInitializer>();
        services.AddScoped<IDocumentFileAppService, DocumentFileAppService>();
        services.AddSingleton<IWordFileDeletionCleanupAppService, WordFileDeletionCleanupAppService>();
        services.AddScoped<WordFileDeletionLeaseStore>();
        services.AddScoped<IDocumentTableQueryAppService, DocumentTableQueryAppService>();
        services.AddScoped<ImportDuplicateDetectionService>();
        services.AddScoped<ColumnMappingLearningService>();
        services.AddScoped<IDocumentImportAppService, DocumentImportAppService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(BatchReplyRetentionPolicy.Default);
        services.AddSingleton<BatchReplySessionCoordinator>();
        services.AddSingleton<BatchReplySessionService>();
        services.AddSingleton<IBatchReplyCleanupAppService, BatchReplyCleanupAppService>();
        services.AddScoped<IOrphanDatabaseReferenceQuery, OrphanDatabaseReferenceQuery>();
        services.AddSingleton<OrphanFileInspectionCoordinator>();
        services.AddScoped<IOrphanFileInspectionAppService, OrphanFileInspectionAppService>();
        services.AddScoped<IBatchReplyAppService, BatchReplyAppService>();
        services.AddScoped<SmartConfigurationLearningService>();
        services.AddScoped<ISmartConfigurationAppService, SmartConfigurationAppService>();
        return services;
    }
}
