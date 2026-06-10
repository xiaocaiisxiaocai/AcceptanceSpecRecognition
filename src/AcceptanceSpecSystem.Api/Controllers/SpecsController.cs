using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 验收规格管理API控制器
/// </summary>
[Route("api/specs")]
[Authorize]
public class SpecsController : BaseApiController
{
    private readonly AcceptanceSpecAppService _acceptanceSpecAppService;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly SpecSemanticSearchService _specSemanticSearchService;

    /// <summary>
    /// 创建验收规格控制器实例
    /// </summary>
    public SpecsController(
        AcceptanceSpecAppService acceptanceSpecAppService,
        IAuthDataScopeService authDataScopeService,
        SpecSemanticSearchService specSemanticSearchService)
    {
        _acceptanceSpecAppService = acceptanceSpecAppService;
        _authDataScopeService = authDataScopeService;
        _specSemanticSearchService = specSemanticSearchService;
    }

    /// <summary>
    /// 获取验收规格分组汇总（按客户 → 机型 → 制程分组，返回每组规格数量）
    /// </summary>
    [HttpGet("groups")]
    [ProducesResponseType(typeof(ApiResponse<List<SpecGroupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SpecGroupDto>>>> GetGroups(
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<List<SpecGroupDto>>(401, "会话缺少用户上下文");

        var items = await _acceptanceSpecAppService.GetGroupsAsync(scope.ToAccessContext(), cancellationToken);
        return Success(items.Select(item => item.ToDto()).ToList());
    }

    /// <summary>
    /// 获取验收规格列表（支持筛选）
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<AcceptanceSpecDto>>>> GetSpecs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] int? customerId = null,
        [FromQuery] int? processId = null,
        [FromQuery] int? machineModelId = null,
        [FromQuery] bool? processIdIsNull = null,
        [FromQuery] bool? machineModelIdIsNull = null,
        [FromQuery] DateTime? importedFrom = null,
        [FromQuery] DateTime? importedTo = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<PagedData<AcceptanceSpecDto>>(401, "会话缺少用户上下文");

        var data = await _acceptanceSpecAppService.GetPagedAsync(
            scope.ToAccessContext(),
            page,
            pageSize,
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull,
            importedFrom,
            importedTo,
            cancellationToken);

        return Success(data.ToDto());
    }

    /// <summary>
    /// 规格重复/近重复排查
    /// </summary>
    [HttpGet("duplicate-groups")]
    [ProducesResponseType(typeof(ApiResponse<SpecDuplicateDetectionResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SpecDuplicateDetectionResultDto>>> GetDuplicateGroups(
        [FromQuery] string? keyword = null,
        [FromQuery] int? customerId = null,
        [FromQuery] int? processId = null,
        [FromQuery] int? machineModelId = null,
        [FromQuery] bool? processIdIsNull = null,
        [FromQuery] bool? machineModelIdIsNull = null,
        [FromQuery] double? minSimilarity = null,
        [FromQuery] int? maxGroups = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<SpecDuplicateDetectionResultDto>(401, "会话缺少用户上下文");

        var result = await _acceptanceSpecAppService.GetDuplicateGroupsAsync(
            scope.ToAccessContext(),
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull,
            minSimilarity,
            maxGroups,
            cancellationToken);

        return Success(result.ToDto());
    }

    /// <summary>
    /// 获取验收规格详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> GetSpec(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<AcceptanceSpecDto>(401, "会话缺少用户上下文");

        try
        {
            var spec = await _acceptanceSpecAppService.GetByIdAsync(scope.ToAccessContext(), id, cancellationToken);
            if (spec == null)
                return NotFoundResult<AcceptanceSpecDto>("验收规格不存在");

            return Success(spec.ToDto());
        }
        catch (ApplicationServiceException ex)
        {
            return Error<AcceptanceSpecDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 验收规格语义搜索
    /// </summary>
    [HttpPost("semantic-search")]
    [AuditOperation("semantic-search", "spec")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<SpecSemanticSearchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SpecSemanticSearchResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SpecSemanticSearchResponse>>> SemanticSearch(
        [FromBody] SpecSemanticSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<SpecSemanticSearchResponse>(401, "会话缺少用户上下文");

        try
        {
            var result = await _specSemanticSearchService.SearchAsync(
                request,
                scope,
                cancellationToken);
            return Success(result);
        }
        catch (ArgumentException ex)
        {
            return Error<SpecSemanticSearchResponse>(400, ex.Message);
        }
        catch (AiServiceUnavailableException ex)
        {
            return Error<SpecSemanticSearchResponse>(400, $"Embedding 服务不可用: {ex.Reason}");
        }
    }

    /// <summary>
    /// 创建验收规格
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "spec")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> CreateSpec(
        [FromBody] CreateSpecRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<AcceptanceSpecDto>(401, "会话缺少用户上下文");

        try
        {
            var spec = await _acceptanceSpecAppService.CreateAsync(
                scope.ToAccessContext(),
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                request.Project,
                request.Specification,
                request.Acceptance,
                request.Remark,
                cancellationToken);
            return Success(spec.ToDto(), "创建验收规格成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<AcceptanceSpecDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 更新验收规格
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "spec")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> UpdateSpec(
        int id,
        [FromBody] UpdateSpecRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<AcceptanceSpecDto>(401, "会话缺少用户上下文");

        try
        {
            var spec = await _acceptanceSpecAppService.UpdateAsync(
                scope.ToAccessContext(),
                id,
                request.Project,
                request.Specification,
                request.Acceptance,
                request.Remark,
                cancellationToken);
            if (spec == null)
                return NotFoundResult<AcceptanceSpecDto>("验收规格不存在");

            return Success(spec.ToDto(), "更新验收规格成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<AcceptanceSpecDto>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 删除验收规格
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "spec")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteSpec(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error(401, "会话缺少用户上下文");

        try
        {
            var deleted = await _acceptanceSpecAppService.DeleteAsync(scope.ToAccessContext(), id, cancellationToken);
            if (!deleted)
                return NotFound(ApiResponse.Error(404, "验收规格不存在"));

            return Success("删除验收规格成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 批量导入验收规格
    /// </summary>
    [HttpPost("batch-import")]
    [AuditOperation("import", "spec")]
    [ProducesResponseType(typeof(ApiResponse<BatchImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchImportResult>>> BatchImport(
        [FromBody] BatchImportSpecsRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<BatchImportResult>(401, "会话缺少用户上下文");

        try
        {
            var result = await _acceptanceSpecAppService.BatchImportAsync(
                scope.ToAccessContext(),
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                request.WordFileId,
                request.Items.Select(item => item.ToInput()).ToList(),
                cancellationToken);
            return Success(result.ToDto(), $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<BatchImportResult>(ex.Code, ex.Message);
        }
    }

    /// <summary>
    /// 批量删除验收规格
    /// </summary>
    [HttpDelete("batch")]
    [AuditOperation("delete-batch", "spec")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> BatchDelete(
        [FromBody] List<int> ids,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error(401, "会话缺少用户上下文");

        try
        {
            var deletedCount = await _acceptanceSpecAppService.BatchDeleteAsync(scope.ToAccessContext(), ids ?? [], cancellationToken);
            return Success($"成功删除{deletedCount}条规格");
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }
}
