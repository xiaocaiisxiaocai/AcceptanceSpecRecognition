using AcceptanceSpecSystem.Api.DTOs;
using System.Security.Claims;

namespace AcceptanceSpecSystem.Api.Services
{
    /// <summary>
    /// 匹配填充执行应用服务。
    /// </summary>
    public sealed class MatchingFillExecutionAppService
    {
        private readonly MatchingWorkflowSupportService _workflowSupportService;

        public MatchingFillExecutionAppService(MatchingWorkflowSupportService workflowSupportService)
        {
            _workflowSupportService = workflowSupportService;
        }

        public Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(
            ClaimsPrincipal user,
            BatchExecuteFillRequest request)
        {
            return _workflowSupportService.BatchExecuteFillCoreAsync(user, request);
        }
    }
}
