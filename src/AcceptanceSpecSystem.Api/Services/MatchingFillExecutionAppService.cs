using System.Security.Claims;
using AcceptanceSpecSystem.Api.DTOs;

namespace AcceptanceSpecSystem.Api.Services
{
    public interface IMatchingFillExecutionAppService
    {
        Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(
            ClaimsPrincipal user,
            BatchExecuteFillRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 匹配填充执行应用服务。
    /// </summary>
    public sealed class MatchingFillExecutionAppService : IMatchingFillExecutionAppService
    {
        private readonly MatchingWorkflowSupportService _workflowSupportService;

        public MatchingFillExecutionAppService(MatchingWorkflowSupportService workflowSupportService)
        {
            _workflowSupportService = workflowSupportService;
        }

        public Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(
            ClaimsPrincipal user,
            BatchExecuteFillRequest request,
            CancellationToken cancellationToken = default)
        {
            return _workflowSupportService.BatchExecuteFillCoreAsync(user, request, cancellationToken);
        }
    }
}
