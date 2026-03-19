using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 验收规格管理API控制器
/// </summary>
[Route("api/specs")]
[Authorize]
public class SpecsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly ILogger<SpecsController> _logger;

    /// <summary>
    /// 创建验收规格控制器实例
    /// </summary>
    public SpecsController(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService,
        ILogger<SpecsController> logger)
    {
        _unitOfWork = unitOfWork;
        _authDataScopeService = authDataScopeService;
        _logger = logger;
    }

    /// <summary>
    /// 获取验收规格分组汇总（按客户 → 机型 → 制程分组，返回每组规格数量）
    /// </summary>
    [HttpGet("groups")]
    [ProducesResponseType(typeof(ApiResponse<List<SpecGroupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SpecGroupDto>>>> GetGroups()
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<List<SpecGroupDto>>(401, "会话缺少用户上下文");

        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetAllWithCustomerAndProcessAsync();
        allSpecs = ApplySpecScope(allSpecs, scope);

        var dtos = allSpecs
            .GroupBy(s => new { s.CustomerId, s.MachineModelId, s.ProcessId })
            .Select(g =>
            {
                var first = g.First();
                return new SpecGroupDto
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = first.Customer?.Name ?? string.Empty,
                    MachineModelId = g.Key.MachineModelId,
                    MachineModelName = first.MachineModel?.Name,
                    ProcessId = g.Key.ProcessId,
                    ProcessName = first.Process?.Name,
                    SpecCount = g.Count()
                };
            })
            .OrderBy(x => x.CustomerName)
            .ThenBy(x => x.MachineModelName)
            .ThenBy(x => x.ProcessName)
            .ToList();

        return Success(dtos);
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
        [FromQuery] bool? machineModelIdIsNull = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<PagedData<AcceptanceSpecDto>>(401, "会话缺少用户上下文");

        // 需要在列表中展示客户/制程名称，因此必须 eager load 导航属性
        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetAllWithCustomerAndProcessAsync();
        allSpecs = ApplySpecScope(allSpecs, scope);
        allSpecs = ApplySpecFilters(
            allSpecs,
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull);

        var total = allSpecs.Count;
        var items = allSpecs
            .OrderByDescending(s => s.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new AcceptanceSpecDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                ProcessId = s.ProcessId,
                MachineModelId = s.MachineModelId,
                ProcessName = s.Process?.Name ?? "",
                MachineModelName = s.MachineModel?.Name ?? "",
                CustomerName = s.Customer?.Name ?? "",
                Project = s.Project,
                Specification = s.Specification,
                Acceptance = s.Acceptance,
                Remark = s.Remark,
                ImportedAt = s.ImportedAt,
                OwnerOrgUnitId = s.OwnerOrgUnitId,
                CreatedByUserId = s.CreatedByUserId
            })
            .ToList();

        var pagedData = new PagedData<AcceptanceSpecDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(pagedData);
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
        [FromQuery] int? maxGroups = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<SpecDuplicateDetectionResultDto>(401, "会话缺少用户上下文");

        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetAllWithCustomerAndProcessAsync();
        allSpecs = ApplySpecScope(allSpecs, scope);
        allSpecs = ApplySpecFilters(
            allSpecs,
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull);

        var result = SpecDuplicateDetectionService.Detect(allSpecs, minSimilarity, maxGroups);
        return Success(result);
    }

    /// <summary>
    /// 获取验收规格详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> GetSpec(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<AcceptanceSpecDto>(401, "会话缺少用户上下文");

        // 详情页需要展示客户/制程名称
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdWithCustomerAndProcessAsync(id);

        if (spec == null)
        {
            return NotFoundResult<AcceptanceSpecDto>("验收规格不存在");
        }

        if (!CanAccessSpec(spec, scope))
            return Error<AcceptanceSpecDto>(403, "无权访问该规格");

        var dto = new AcceptanceSpecDto
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            MachineModelId = spec.MachineModelId,
            ProcessName = spec.Process?.Name ?? "",
            MachineModelName = spec.MachineModel?.Name ?? "",
            CustomerName = spec.Customer?.Name ?? "",
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt,
            OwnerOrgUnitId = spec.OwnerOrgUnitId,
            CreatedByUserId = spec.CreatedByUserId
        };

        return Success(dto);
    }

    /// <summary>
    /// 创建验收规格
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "spec")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> CreateSpec([FromBody] CreateSpecRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<AcceptanceSpecDto>(401, "会话缺少用户上下文");

        // 检查客户是否存在
        var customer = await _unitOfWork.Customers.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            return Error<AcceptanceSpecDto>(400, "所选客户不存在");
        }

        // 检查制程是否存在
        Process? process = null;
        if (request.ProcessId.HasValue)
        {
            process = await _unitOfWork.Processes.GetByIdAsync(request.ProcessId.Value);
            if (process == null)
            {
                return Error<AcceptanceSpecDto>(400, "所选制程不存在");
            }
        }

        // 检查机型是否存在
        MachineModel? machineModel = null;
        if (request.MachineModelId.HasValue)
        {
            machineModel = await _unitOfWork.MachineModels.GetByIdAsync(request.MachineModelId.Value);
            if (machineModel == null)
            {
                return Error<AcceptanceSpecDto>(400, "所选机型不存在");
            }
        }

        // 获取或创建一个默认的WordFile记录（手动创建的规格）
        var wordFile = await GetOrCreateManualWordFile();

        var spec = new AcceptanceSpec
        {
            CustomerId = request.CustomerId,
            ProcessId = request.ProcessId,
            MachineModelId = request.MachineModelId,
            Project = request.Project,
            Specification = request.Specification,
            Acceptance = request.Acceptance,
            Remark = request.Remark,
            OwnerOrgUnitId = scope.PrimaryOrgUnitId,
            CreatedByUserId = scope.UserId,
            WordFileId = wordFile.Id,
            ImportedAt = DateTime.Now
        };

        await _unitOfWork.AcceptanceSpecs.AddAsync(spec);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建验收规格成功: {SpecId} - {Project}", spec.Id, spec.Project);

        var dto = new AcceptanceSpecDto
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            MachineModelId = spec.MachineModelId,
            ProcessName = process?.Name ?? "",
            MachineModelName = machineModel?.Name ?? "",
            CustomerName = customer.Name,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt,
            OwnerOrgUnitId = spec.OwnerOrgUnitId,
            CreatedByUserId = spec.CreatedByUserId
        };

        return Success(dto, "创建验收规格成功");
    }

    /// <summary>
    /// 更新验收规格
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "spec")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> UpdateSpec(int id, [FromBody] UpdateSpecRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<AcceptanceSpecDto>(401, "会话缺少用户上下文");

        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id);
        if (spec == null)
        {
            return NotFoundResult<AcceptanceSpecDto>("验收规格不存在");
        }

        if (!CanAccessSpec(spec, scope))
            return Error<AcceptanceSpecDto>(403, "无权操作该规格");

        spec.Project = request.Project;
        spec.Specification = request.Specification;
        spec.Acceptance = request.Acceptance;
        spec.Remark = request.Remark;

        _unitOfWork.AcceptanceSpecs.Update(spec);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("更新验收规格成功: {SpecId} - {Project}", spec.Id, spec.Project);

        var dto = new AcceptanceSpecDto
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            MachineModelId = spec.MachineModelId,
            ProcessName = spec.Process?.Name ?? "",
            MachineModelName = spec.MachineModel?.Name ?? "",
            CustomerName = spec.Customer?.Name ?? "",
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt,
            OwnerOrgUnitId = spec.OwnerOrgUnitId,
            CreatedByUserId = spec.CreatedByUserId
        };

        return Success(dto, "更新验收规格成功");
    }

    /// <summary>
    /// 删除验收规格
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "spec")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteSpec(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error(401, "会话缺少用户上下文");

        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id);
        if (spec == null)
        {
            return NotFound(ApiResponse.Error(404, "验收规格不存在"));
        }

        if (!CanAccessSpec(spec, scope))
            return Error(403, "无权操作该规格");

        _unitOfWork.AcceptanceSpecs.Remove(spec);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除验收规格成功: {SpecId} - {Project}", spec.Id, spec.Project);

        return Success("删除验收规格成功");
    }

    /// <summary>
    /// 批量导入验收规格
    /// </summary>
    [HttpPost("batch-import")]
    [AuditOperation("import", "spec")]
    [ProducesResponseType(typeof(ApiResponse<BatchImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchImportResult>>> BatchImport([FromBody] BatchImportSpecsRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<BatchImportResult>(401, "会话缺少用户上下文");

        // 检查客户是否存在
        var customer = await _unitOfWork.Customers.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            return Error<BatchImportResult>(400, "所选客户不存在");
        }

        // 检查制程是否存在
        Process? process = null;
        if (request.ProcessId.HasValue)
        {
            process = await _unitOfWork.Processes.GetByIdAsync(request.ProcessId.Value);
            if (process == null)
            {
                return Error<BatchImportResult>(400, "所选制程不存在");
            }
        }

        // 检查机型是否存在
        if (request.MachineModelId.HasValue)
        {
            var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(request.MachineModelId.Value);
            if (machineModel == null)
            {
                return Error<BatchImportResult>(400, "所选机型不存在");
            }
        }

        // 检查WordFile是否存在
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(request.WordFileId);
        if (wordFile == null)
        {
            return Error<BatchImportResult>(400, "Word文件不存在");
        }

        var successCount = 0;
        var failedCount = 0;

        foreach (var item in request.Items)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.Project) || string.IsNullOrWhiteSpace(item.Specification))
                {
                    failedCount++;
                    continue;
                }

                var spec = new AcceptanceSpec
                {
                    CustomerId = request.CustomerId,
                    ProcessId = request.ProcessId,
                    MachineModelId = request.MachineModelId,
                    Project = item.Project.Trim(),
                    Specification = item.Specification.Trim(),
                    Acceptance = item.Acceptance?.Trim(),
                    Remark = item.Remark?.Trim(),
                    OwnerOrgUnitId = scope.PrimaryOrgUnitId,
                    CreatedByUserId = scope.UserId,
                    WordFileId = request.WordFileId,
                    ImportedAt = DateTime.Now
                };

                await _unitOfWork.AcceptanceSpecs.AddAsync(spec);
                successCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("批量导入验收规格完成: 成功{Success}, 失败{Failed}", successCount, failedCount);

        var result = new BatchImportResult
        {
            SuccessCount = successCount,
            FailedCount = failedCount,
            TotalCount = request.Items.Count
        };

        return Success(result, $"导入完成：成功{successCount}条，失败{failedCount}条");
    }

    /// <summary>
    /// 批量删除验收规格
    /// </summary>
    [HttpDelete("batch")]
    [AuditOperation("delete-batch", "spec")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> BatchDelete([FromBody] List<int> ids)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error(401, "会话缺少用户上下文");

        if (ids == null || ids.Count == 0)
        {
            return Error(400, "请选择要删除的规格");
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => ids.Contains(s.Id));
        var allowedSpecs = specs.Where(s => CanAccessSpec(s, scope)).ToList();

        if (allowedSpecs.Count == 0)
        {
            return Error(403, "未找到可删除的规格或无权限");
        }

        _unitOfWork.AcceptanceSpecs.RemoveRange(allowedSpecs);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("批量删除验收规格成功: {Count}条", allowedSpecs.Count);

        return Success($"成功删除{allowedSpecs.Count}条规格");
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }

    private static IReadOnlyList<AcceptanceSpec> ApplySpecScope(
        IEnumerable<AcceptanceSpec> specs,
        DataScopeResult scope)
    {
        return SpecDataScopeHelper.ApplyScope(specs, scope);
    }

    private static bool CanAccessSpec(AcceptanceSpec spec, DataScopeResult scope)
    {
        return SpecDataScopeHelper.CanAccess(spec, scope);
    }

    private static IReadOnlyList<AcceptanceSpec> ApplySpecFilters(
        IEnumerable<AcceptanceSpec> specs,
        string? keyword,
        int? customerId,
        int? processId,
        int? machineModelId,
        bool? processIdIsNull,
        bool? machineModelIdIsNull)
    {
        var query = specs;

        if (processId.HasValue)
        {
            query = query.Where(spec => spec.ProcessId == processId.Value);
        }
        else if (processIdIsNull == true)
        {
            query = query.Where(spec => spec.ProcessId == null);
        }

        if (machineModelId.HasValue)
        {
            query = query.Where(spec => spec.MachineModelId == machineModelId.Value);
        }
        else if (machineModelIdIsNull == true)
        {
            query = query.Where(spec => spec.MachineModelId == null);
        }

        if (customerId.HasValue)
        {
            query = query.Where(spec => spec.CustomerId == customerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(spec =>
                spec.Project.Contains(keyword) ||
                spec.Specification.Contains(keyword) ||
                (spec.Acceptance != null && spec.Acceptance.Contains(keyword)) ||
                (spec.Remark != null && spec.Remark.Contains(keyword)));
        }

        return query.ToList();
    }

    /// <summary>
    /// 获取或创建手动录入的WordFile记录
    /// </summary>
    private async Task<WordFile> GetOrCreateManualWordFile()
    {
        const string manualFileName = "__MANUAL_ENTRY__";
        const string manualFileHash = "manual_entry_placeholder";

        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(
            w => w.FileName == manualFileName);

        if (existingFile != null)
        {
            return existingFile;
        }

        var wordFile = new WordFile
        {
            FileName = manualFileName,
            FileContent = Array.Empty<byte>(),
            FileHash = manualFileHash,
            UploadedAt = DateTime.Now
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile);
        await _unitOfWork.SaveChangesAsync();

        return wordFile;
    }
}
