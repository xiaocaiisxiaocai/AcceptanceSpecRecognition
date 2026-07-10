using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public abstract class SmartConfigRecognizeApiFactoryBase : ApiWebApplicationFactory
{
    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
            ConfigureTestAppConfiguration(configBuilder));
        builder.ConfigureServices(ConfigureTestServices);
    }

    protected virtual void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
    }

    protected abstract void ConfigureTestServices(IServiceCollection services);

    protected static void ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll(typeof(TService));
        services.AddScoped<TService, TImplementation>();
    }
}

public sealed class MissingSpecificationColumnIntelligenceApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>(services);
    }
}

public sealed class ColumnSemanticRecallCountingApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<ILlmColumnSemanticRecallService, CountingColumnSemanticRecallService>(services);
    }
}

public sealed class ColumnSemanticRecallMissingSpecificationApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, SpecificationColumnSemanticRecallService>(services);
    }
}

public sealed class ColumnSemanticRecallMissingAcceptanceApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingAcceptanceForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, AcceptanceColumnSemanticRecallService>(services);
    }
}

public sealed class ColumnSemanticRecallInvalidResultApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, InvalidColumnSemanticRecallService>(services);
    }
}

public sealed class ColumnSemanticRecallRepeatedHeaderApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, CountingColumnSemanticRecallService>(services);
    }
}

public sealed class ColumnSemanticRecallFailingRepeatedHeaderApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, FailingColumnSemanticRecallService>(services);
    }
}

public sealed class ColumnSemanticRecallTimeoutApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartConfiguration:StructureAdjudicationTimeoutSeconds"] = "3",
            ["SmartConfiguration:ColumnSemanticRecallTimeoutSeconds"] = "1",
            ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "0"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, BlockingColumnSemanticRecallService>(services);
    }
}

public sealed class LowConfidenceCompleteMappingApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "0"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, LowConfidenceCompleteMappingIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, ZeroBudgetCountingStructureAdjudicationService>(services);
    }
}

public sealed class LlmStructureTimeoutApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartConfiguration:StructureAdjudicationTimeoutSeconds"] = "1"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, BlockingStructureAdjudicationService>(services);
    }
}

public sealed class LlmStructureBudgetApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "1"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, CountingStructureAdjudicationService>(services);
    }
}

public sealed class LlmStructureCacheApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, StructureCacheCountingStructureAdjudicationService>(services);
    }
}

public sealed class LlmStructureCacheFusedRangeApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, StructureCacheFusedRangeAdjudicationService>(services);
    }
}

public sealed class LlmSharedBudgetApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartConfiguration:MaxLlmCallsPerRecognizeDocument"] = "1",
            ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "5",
            ["SmartConfiguration:MaxColumnSemanticRecallCallsPerDocument"] = "5"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, SharedBudgetCountingStructureAdjudicationService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, SharedBudgetCountingColumnSemanticRecallService>(services);
    }
}

public sealed class LlmRoutingBudgetApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestAppConfiguration(IConfigurationBuilder configBuilder)
    {
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "1"
        });
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, RoutingBudgetRecordingStructureAdjudicationService>(services);
    }
}

public sealed class MissingProjectColumnIntelligenceApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingProjectColumnIntelligenceService>(services);
    }
}

public sealed class LlmFillsMissingSpecificationApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, FillSpecificationColumnStructureAdjudicationService>(services);
    }
}

public sealed class LlmFillsMissingSpecificationWithSemanticRecallApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, FillSpecificationColumnStructureAdjudicationService>(services);
        ReplaceScoped<ILlmColumnSemanticRecallService, SpecificationColumnSemanticRecallService>(services);
    }
}

public sealed class LlmCorrectsHeaderStructureApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, LowConfidenceWrongHeaderIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, HeaderCorrectionStructureAdjudicationService>(services);
    }
}

public sealed class LlmInvalidHeaderStructureApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, LowConfidenceWrongHeaderIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, InvalidHeaderStructureAdjudicationService>(services);
    }
}

public sealed class LlmRecordingHistoryFewShotApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, RecordingStructureAdjudicationService>(services);
    }
}

public sealed class LlmOffsetHeaderRecordingApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, OffsetHeaderMissingSpecificationColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, OffsetHeaderRecordingStructureAdjudicationService>(services);
    }
}

public sealed class SpecificationOnlyIntelligenceApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, SpecificationOnlyIntelligenceService>(services);
    }
}

public sealed class LlmFillsMissingRequiredColumnsApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingAcceptanceAndRemarkColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, FillRequiredColumnsStructureAdjudicationService>(services);
    }
}

public sealed class LlmIncompleteRequiredColumnsApiFactory : SmartConfigRecognizeApiFactoryBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReplaceScoped<IDocumentIntelligenceService, MissingAcceptanceAndRemarkColumnIntelligenceService>(services);
        ReplaceScoped<ILlmDocumentStructureAdjudicationService, IncompleteRequiredColumnsStructureAdjudicationService>(services);
    }
}
