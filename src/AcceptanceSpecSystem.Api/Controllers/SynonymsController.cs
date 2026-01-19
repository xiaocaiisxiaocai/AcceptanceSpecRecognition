using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 同义词CRUD API控制器（按同义词组管理）
/// </summary>
[Route("api/synonyms")]
public class SynonymsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public SynonymsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取同义词组列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<SynonymGroupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<SynonymGroupDto>>>> GetGroups(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var all = await _unitOfWork.Synonyms.GetAllGroupsAsync();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            all = all.Where(g =>
                    g.Words.Any(w => w.Word.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var total = all.Count;
        var items = all
            .OrderByDescending(g => g.UpdatedAt ?? g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToList();

        return Success(new PagedData<SynonymGroupDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取同义词组详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<SynonymGroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SynonymGroupDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SynonymGroupDto>>> GetGroup(int id)
    {
        var group = await _unitOfWork.Synonyms.GetGroupByIdAsync(id);
        if (group == null)
            return NotFoundResult<SynonymGroupDto>("同义词组不存在");

        return Success(ToDto(group));
    }

    /// <summary>
    /// 新增同义词组（第一个词为标准词）
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SynonymGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SynonymGroupDto>>> Create([FromBody] UpsertSynonymGroupRequest request)
    {
        var words = NormalizeWords(request.Words);
        if (words.Count < 2)
            return Error<SynonymGroupDto>(400, "至少需要2个词");

        var group = await _unitOfWork.Synonyms.AddGroupAsync(words);
        await _unitOfWork.SaveChangesAsync();
        return Success(ToDto(group), "创建成功");
    }

    /// <summary>
    /// 更新同义词组（第一个词为标准词）
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Update(int id, [FromBody] UpsertSynonymGroupRequest request)
    {
        var words = NormalizeWords(request.Words);
        if (words.Count < 2)
            return Error(400, "至少需要2个词");

        var group = await _unitOfWork.Synonyms.GetGroupByIdAsync(id);
        if (group == null)
            return Error(400, "同义词组不存在");

        await _unitOfWork.Synonyms.UpdateGroupAsync(id, words);
        await _unitOfWork.SaveChangesAsync();
        return Success("更新成功");
    }

    /// <summary>
    /// 删除同义词组
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var group = await _unitOfWork.Synonyms.GetGroupByIdAsync(id);
        if (group == null)
            return Error(400, "同义词组不存在");

        await _unitOfWork.Synonyms.DeleteGroupAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return Success("删除成功");
    }

    private static SynonymGroupDto ToDto(Data.Entities.SynonymGroup g)
    {
        var ordered = g.Words
            .OrderByDescending(w => w.IsStandard)
            .ThenBy(w => w.Word)
            .Select(w => w.Word)
            .ToList();

        return new SynonymGroupDto
        {
            Id = g.Id,
            Words = ordered,
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt
        };
    }

    private static List<string> NormalizeWords(IEnumerable<string> input)
    {
        return input
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

