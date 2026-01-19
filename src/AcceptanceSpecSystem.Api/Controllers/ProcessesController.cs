using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 制程管理API控制器
/// </summary>
public class ProcessesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProcessesController> _logger;

    /// <summary>
    /// 创建制程控制器实例
    /// </summary>
    public ProcessesController(IUnitOfWork unitOfWork, ILogger<ProcessesController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 获取制程列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<ProcessDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<ProcessDto>>>> GetProcesses(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var allProcesses = await _unitOfWork.Processes.GetAllAsync();

        // 按关键字筛选
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            allProcesses = allProcesses.Where(p => p.Name.Contains(keyword)).ToList();
        }

        var total = allProcesses.Count;
        var items = allProcesses
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProcessDto
            {
                Id = p.Id,
                Name = p.Name,
                CreatedAt = p.CreatedAt,
                SpecCount = p.AcceptanceSpecs?.Count ?? 0
            })
            .ToList();

        var pagedData = new PagedData<ProcessDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(pagedData);
    }

    /// <summary>
    /// 获取制程详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> GetProcess(int id)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id);

        if (process == null)
        {
            return NotFoundResult<ProcessDto>("制程不存在");
        }

        var dto = new ProcessDto
        {
            Id = process.Id,
            Name = process.Name,
            CreatedAt = process.CreatedAt,
            SpecCount = process.AcceptanceSpecs?.Count ?? 0
        };

        return Success(dto);
    }

    /// <summary>
    /// 创建制程
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> CreateProcess([FromBody] CreateProcessRequest request)
    {
        var process = new Process
        {
            Name = request.Name,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.Processes.AddAsync(process);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);

        var dto = new ProcessDto
        {
            Id = process.Id,
            Name = process.Name,
            CreatedAt = process.CreatedAt,
            SpecCount = 0
        };

        return Success(dto, "创建制程成功");
    }

    /// <summary>
    /// 更新制程
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ProcessDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ProcessDto>>> UpdateProcess(int id, [FromBody] UpdateProcessRequest request)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
        if (process == null)
        {
            return NotFoundResult<ProcessDto>("制程不存在");
        }

        // 检查同一客户下制程名称是否与其他制程重复
        process.Name = request.Name;

        _unitOfWork.Processes.Update(process);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("更新制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);

        var dto = new ProcessDto
        {
            Id = process.Id,
            Name = process.Name,
            CreatedAt = process.CreatedAt,
            SpecCount = process.AcceptanceSpecs?.Count ?? 0
        };

        return Success(dto, "更新制程成功");
    }

    /// <summary>
    /// 删除制程
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteProcess(int id)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
        if (process == null)
        {
            return NotFound(ApiResponse.Error(404, "制程不存在"));
        }

        _unitOfWork.Processes.Remove(process);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);

        return Success("删除制程成功");
    }

    /// <summary>
    /// 获取制程的验收规格列表
    /// </summary>
    [HttpGet("{id}/specs")]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AcceptanceSpecDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedData<AcceptanceSpecDto>>>> GetProcessSpecs(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
        if (process == null)
        {
            return NotFoundResult<PagedData<AcceptanceSpecDto>>("制程不存在");
        }

        var allSpecs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.ProcessId == id);

        // 按关键字筛选
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            allSpecs = allSpecs.Where(s =>
                s.Project.Contains(keyword) ||
                s.Specification.Contains(keyword)).ToList();
        }

        var total = allSpecs.Count;
        var items = allSpecs
            .OrderByDescending(s => s.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new AcceptanceSpecDto
            {
                Id = s.Id,
                ProcessId = s.ProcessId,
                ProcessName = process.Name,
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
}
