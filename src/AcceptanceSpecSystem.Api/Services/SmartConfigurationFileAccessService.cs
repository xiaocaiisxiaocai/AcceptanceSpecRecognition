using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 使用当前登录上下文和规格数据范围裁决智能结构配置可访问的文件。
/// </summary>
public sealed class SmartConfigurationFileAccessService : ISmartConfigurationFileAccessService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly IUnitOfWork _unitOfWork;

    public SmartConfigurationFileAccessService(
        IHttpContextAccessor httpContextAccessor,
        IAuthDataScopeService authDataScopeService,
        DocumentFileAccessService documentFileAccessService,
        IUnitOfWork unitOfWork)
    {
        _httpContextAccessor = httpContextAccessor;
        _authDataScopeService = authDataScopeService;
        _documentFileAccessService = documentFileAccessService;
        _unitOfWork = unitOfWork;
    }

    public async Task<WordFile?> GetAccessibleFileAsync(
        int fileId,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope == null)
        {
            return null;
        }

        return await _documentFileAccessService.GetAccessibleWordFileAsync(
            fileId,
            scope,
            includeScopedSpecs: false,
            cancellationToken: cancellationToken);
    }

    public async Task<bool> CanAccessCustomerAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope == null ||
            !await _unitOfWork.Customers.Query().AnyAsync(item => item.Id == customerId, cancellationToken))
        {
            return false;
        }

        if (scope.IsAll)
        {
            return true;
        }

        var customerSpecs = _unitOfWork.AcceptanceSpecs.Query()
            .Where(item => item.CustomerId == customerId);
        // Customer 没有独立归属字段，非全范围用户只能通过已有规格的数据范围证明访问权；
        // 空客户无法证明归属时必须 fail closed，首次配置仅允许全范围用户执行。
        return await SpecDataScopeHelper.ApplyScopeToQuery(
                customerSpecs,
                scope)
            .AnyAsync(cancellationToken);
    }

    private async Task<DataScopeResult?> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user == null ? null : AuthClaimHelper.GetUserId(user);
        var companyId = user == null ? null : AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            return null;
        }

        var scope = await _authDataScopeService.GetScopeAsync(userId.Value, companyId.Value, "spec");
        cancellationToken.ThrowIfCancellationRequested();
        return scope;
    }
}
