using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 使用当前登录上下文裁决智能结构配置可访问的文件和共享基础数据。
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
        if (scope == null)
        {
            return false;
        }

        // 客户是共享基础数据，不应通过历史验收规格的数据范围反向限制访问。
        // 规格和上传文件仍分别在各自查询入口执行数据范围校验。
        return await _unitOfWork.Customers.Query()
            .AnyAsync(item => item.Id == customerId, cancellationToken);
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

        return await _authDataScopeService.GetScopeAsync(
            userId.Value,
            companyId.Value,
            "spec",
            cancellationToken);
    }
}
