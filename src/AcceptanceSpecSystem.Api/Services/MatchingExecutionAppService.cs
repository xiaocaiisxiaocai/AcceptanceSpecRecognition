using System.Security.Claims;
using AcceptanceSpecSystem.Api.DTOs;

namespace AcceptanceSpecSystem.Api.Services;

public interface IMatchingExecutionAppService
{
    Task LlmStreamAsync(
        ClaimsPrincipal user,
        HttpResponse response,
        MatchLlmStreamRequest request,
        CancellationToken cancellationToken);

    Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(
        ClaimsPrincipal user,
        BatchExecuteFillRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 匹配执行应用服务。
/// </summary>
public sealed class MatchingExecutionAppService : IMatchingExecutionAppService
{
    private readonly IMatchingLlmStreamAppService _matchingLlmStreamAppService;
    private readonly IMatchingFillExecutionAppService _matchingFillExecutionAppService;

    public MatchingExecutionAppService(
        IMatchingLlmStreamAppService matchingLlmStreamAppService,
        IMatchingFillExecutionAppService matchingFillExecutionAppService)
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
        BatchExecuteFillRequest request,
        CancellationToken cancellationToken = default)
    {
        return _matchingFillExecutionAppService.BatchExecuteFillAsync(user, request, cancellationToken);
    }
}
