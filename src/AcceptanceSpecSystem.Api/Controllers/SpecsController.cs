using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 验收规格管理API控制器
/// </summary>
[Route("api/specs")]
public class SpecsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SpecsController> _logger;

    /// <summary>
    /// 创建验收规格控制器实例
    /// </summary>
    public SpecsController(IUnitOfWork unitOfWork, ILogger<SpecsController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
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
        [FromQuery] int? processId = null)
    {
        // 需要在列表中展示客户/制程名称，因此必须 eager load 导航属性
        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetAllWithCustomerAndProcessAsync();

        // 按制程筛选
        if (processId.HasValue)
        {
            allSpecs = allSpecs.Where(s => s.ProcessId == processId.Value).ToList();
        }
        // 按客户筛选（直接通过 AcceptanceSpec.CustomerId）
        if (customerId.HasValue)
        {
            allSpecs = allSpecs.Where(s => s.CustomerId == customerId.Value).ToList();
        }

        // 按关键字筛选
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            allSpecs = allSpecs.Where(s =>
                s.Project.Contains(keyword) ||
                s.Specification.Contains(keyword) ||
                (s.Acceptance != null && s.Acceptance.Contains(keyword)) ||
                (s.Remark != null && s.Remark.Contains(keyword))).ToList();
        }

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
                ProcessName = s.Process?.Name ?? "",
                CustomerName = s.Customer?.Name ?? "",
                Project = s.Project,
                Specification = s.Specification,
                Acceptance = s.Acceptance,
                Remark = s.Remark,
                ImportedAt = s.ImportedAt
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
    /// 获取验收规格详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> GetSpec(int id)
    {
        // 详情页需要展示客户/制程名称
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdWithCustomerAndProcessAsync(id);

        if (spec == null)
        {
            return NotFoundResult<AcceptanceSpecDto>("验收规格不存在");
        }

        var dto = new AcceptanceSpecDto
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            ProcessName = spec.Process?.Name ?? "",
            CustomerName = spec.Customer?.Name ?? "",
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt
        };

        return Success(dto);
    }

    /// <summary>
    /// 创建验收规格
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> CreateSpec([FromBody] CreateSpecRequest request)
    {
        // 检查客户是否存在
        var customer = await _unitOfWork.Customers.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            return Error<AcceptanceSpecDto>(400, "所选客户不存在");
        }

        // 检查制程是否存在
        var process = await _unitOfWork.Processes.GetByIdAsync(request.ProcessId);
        if (process == null)
        {
            return Error<AcceptanceSpecDto>(400, "所选制程不存在");
        }

        // 获取或创建一个默认的WordFile记录（手动创建的规格）
        var wordFile = await GetOrCreateManualWordFile();

        var spec = new AcceptanceSpec
        {
            CustomerId = request.CustomerId,
            ProcessId = request.ProcessId,
            Project = request.Project,
            Specification = request.Specification,
            Acceptance = request.Acceptance,
            Remark = request.Remark,
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
            ProcessName = process.Name,
            CustomerName = customer.Name,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt
        };

        return Success(dto, "创建验收规格成功");
    }

    /// <summary>
    /// 更新验收规格
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AcceptanceSpecDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AcceptanceSpecDto>>> UpdateSpec(int id, [FromBody] UpdateSpecRequest request)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id);
        if (spec == null)
        {
            return NotFoundResult<AcceptanceSpecDto>("验收规格不存在");
        }

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
            ProcessName = spec.Process?.Name ?? "",
            CustomerName = spec.Customer?.Name ?? "",
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt
        };

        return Success(dto, "更新验收规格成功");
    }

    /// <summary>
    /// 删除验收规格
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteSpec(int id)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id);
        if (spec == null)
        {
            return NotFound(ApiResponse.Error(404, "验收规格不存在"));
        }

        _unitOfWork.AcceptanceSpecs.Remove(spec);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除验收规格成功: {SpecId} - {Project}", spec.Id, spec.Project);

        return Success("删除验收规格成功");
    }

    /// <summary>
    /// 批量导入验收规格
    /// </summary>
    [HttpPost("batch-import")]
    [ProducesResponseType(typeof(ApiResponse<BatchImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchImportResult>>> BatchImport([FromBody] BatchImportSpecsRequest request)
    {
        // 检查客户是否存在
        var customer = await _unitOfWork.Customers.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            return Error<BatchImportResult>(400, "所选客户不存在");
        }

        // 检查制程是否存在
        var process = await _unitOfWork.Processes.GetByIdAsync(request.ProcessId);
        if (process == null)
        {
            return Error<BatchImportResult>(400, "所选制程不存在");
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
                    Project = item.Project.Trim(),
                    Specification = item.Specification.Trim(),
                    Acceptance = item.Acceptance?.Trim(),
                    Remark = item.Remark?.Trim(),
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
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> BatchDelete([FromBody] List<int> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            return Error(400, "请选择要删除的规格");
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => ids.Contains(s.Id));
        if (specs.Count == 0)
        {
            return Error(400, "未找到要删除的规格");
        }

        _unitOfWork.AcceptanceSpecs.RemoveRange(specs);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("批量删除验收规格成功: {Count}条", specs.Count);

        return Success($"成功删除{specs.Count}条规格");
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
