using AcceptanceSpecSystem.Api.DTOs;
using System.Security.Claims;

namespace AcceptanceSpecSystem.Api.Services;

public interface IMatchingLlmStreamAppService
{
    Task LlmStreamAsync(
        ClaimsPrincipal user,
        HttpResponse response,
        MatchLlmStreamRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// LLM 流式复核应用服务。
/// </summary>
public sealed class MatchingLlmStreamAppService : IMatchingLlmStreamAppService
{
    private readonly MatchingWorkflowSupportService _workflowSupportService;

    public MatchingLlmStreamAppService(MatchingWorkflowSupportService workflowSupportService)
    {
        _workflowSupportService = workflowSupportService;
    }

    public Task LlmStreamAsync(
        ClaimsPrincipal user,
        HttpResponse response,
        MatchLlmStreamRequest request,
        CancellationToken cancellationToken)
    {
        return _workflowSupportService.RunLlmStreamAsync(user, response, request, cancellationToken);
    }
}
