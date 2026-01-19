using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 操作历史查询API控制器
/// </summary>
[Route("api/history")]
public class HistoryController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public HistoryController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取操作历史列表（分页/筛选）
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<OperationHistoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<OperationHistoryDto>>>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OperationType? operationType = null,
        [FromQuery] bool? canUndo = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var all = await _unitOfWork.OperationHistories.GetAllAsync();

        if (operationType.HasValue)
            all = all.Where(h => h.OperationType == operationType.Value).ToList();

        if (canUndo.HasValue)
            all = all.Where(h => h.CanUndo == canUndo.Value).ToList();

        if (from.HasValue)
            all = all.Where(h => h.CreatedAt >= from.Value).ToList();

        if (to.HasValue)
            all = all.Where(h => h.CreatedAt <= to.Value).ToList();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            all = all.Where(h =>
                    (h.TargetFile?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (h.Details?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var total = all.Count;
        var items = all
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToList();

        return Success(new PagedData<OperationHistoryDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取操作历史详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<OperationHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<OperationHistoryDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<OperationHistoryDto>>> GetById(int id)
    {
        var entity = await _unitOfWork.OperationHistories.GetByIdAsync(id);
        if (entity == null)
            return NotFoundResult<OperationHistoryDto>("记录不存在");

        return Success(ToDto(entity));
    }

    /// <summary>
    /// 撤销操作（占位：业务撤销逻辑待Core层实现）
    /// </summary>
    [HttpPost("{id}/undo")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Undo(int id)
    {
        var entity = await _unitOfWork.OperationHistories.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "记录不存在");

        if (!entity.CanUndo)
            return Error(400, "该操作不可撤销");

        return Error(501, "撤销功能尚未实现（待业务层补充）");
    }

    private static OperationHistoryDto ToDto(OperationHistory h) => new()
    {
        Id = h.Id,
        OperationType = h.OperationType,
        TargetFile = h.TargetFile,
        Details = h.Details,
        CanUndo = h.CanUndo,
        CreatedAt = h.CreatedAt
    };
}

