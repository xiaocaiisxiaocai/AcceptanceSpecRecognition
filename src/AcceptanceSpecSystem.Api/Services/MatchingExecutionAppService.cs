using AcceptanceSpecSystem.Api.DTOs;
using System.Security.Claims;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配执行应用服务。
/// </summary>
public sealed class MatchingExecutionAppService
{
    private readonly MatchingLlmStreamAppService _matchingLlmStreamAppService;
    private readonly MatchingFillExecutionAppService _matchingFillExecutionAppService;

    public MatchingExecutionAppService(
        MatchingLlmStreamAppService matchingLlmStreamAppService,
        MatchingFillExecutionAppService matchingFillExecutionAppService)
    {
        _matchingLlmStreamAppService = matchingLlmStreamAppService;
        _matchingFillExecutionAppService = matchingFillExecutionAppService;
    }

    public Task LlmStreamAsync(
        ClaimsPrincipal user,
        HttpResponse response,
        MatchLlmStreamRequest request,
        CancellationToken cancellationToken)
    {
        return _matchingLlmStreamAppService.LlmStreamAsync(user, response, request, cancellationToken);
    }

    public Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(
        ClaimsPrincipal user,
        BatchExecuteFillRequest request)
    {
        return _matchingFillExecutionAppService.BatchExecuteFillAsync(user, request);
    }
}
