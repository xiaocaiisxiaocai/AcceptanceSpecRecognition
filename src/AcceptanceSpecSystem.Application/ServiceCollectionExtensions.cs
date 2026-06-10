using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAcceptanceApplicationLayer(this IServiceCollection services)
    {
        services.AddScoped<IAiServiceConfigProvider, AiServiceConfigProvider>();
        services.AddScoped<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddScoped<AcceptanceSpecQueryService>();
        services.AddScoped<CustomerAppService>();
        services.AddScoped<ProcessAppService>();
        services.AddScoped<MachineModelAppService>();
        services.AddScoped<AcceptanceSpecAppService>();
        return services;
    }
}
