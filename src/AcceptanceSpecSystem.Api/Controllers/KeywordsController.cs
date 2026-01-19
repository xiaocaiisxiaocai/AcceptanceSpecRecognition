using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 关键字CRUD API控制器
/// </summary>
[Route("api/keywords")]
public class KeywordsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public KeywordsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取关键字列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<KeywordDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<KeywordDto>>>> GetKeywords(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var all = string.IsNullOrWhiteSpace(keyword)
            ? await _unitOfWork.Keywords.GetAllAsync()
            : await _unitOfWork.Keywords.FindAsync(k => k.Word.Contains(keyword));

        var total = all.Count;
        var items = all
            .OrderByDescending(k => k.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(k => new KeywordDto { Id = k.Id, Word = k.Word, CreatedAt = k.CreatedAt })
            .ToList();

        return Success(new PagedData<KeywordDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 新增关键字
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<KeywordDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<KeywordDto>>> Create([FromBody] CreateKeywordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Word))
            return Error<KeywordDto>(400, "关键字不能为空");

        var word = request.Word.Trim();
        var exists = await _unitOfWork.Keywords.GetByWordAsync(word);
        if (exists != null)
            return Error<KeywordDto>(400, "关键字已存在");

        var entity = new Keyword { Word = word, CreatedAt = DateTime.Now };
        await _unitOfWork.Keywords.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return Success(new KeywordDto { Id = entity.Id, Word = entity.Word, CreatedAt = entity.CreatedAt }, "创建成功");
    }

    /// <summary>
    /// 批量新增关键字（自动去重）
    /// </summary>
    [HttpPost("batch")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<int>>> BatchAdd([FromBody] BatchAddKeywordsRequest request)
    {
        var words = request.Words
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim())
            .ToList();

        var added = await _unitOfWork.Keywords.AddRangeUniqueAsync(words);
        await _unitOfWork.SaveChangesAsync();
        return Success(added, $"新增{added}个关键字");
    }

    /// <summary>
    /// 更新关键字
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Update(int id, [FromBody] UpdateKeywordRequest request)
    {
        var entity = await _unitOfWork.Keywords.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "关键字不存在");

        if (string.IsNullOrWhiteSpace(request.Word))
            return Error(400, "关键字不能为空");

        var newWord = request.Word.Trim();
        var exists = await _unitOfWork.Keywords.GetByWordAsync(newWord);
        if (exists != null && exists.Id != id)
            return Error(400, "关键字已存在");

        entity.Word = newWord;
        _unitOfWork.Keywords.Update(entity);
        await _unitOfWork.SaveChangesAsync();
        return Success("更新成功");
    }

    /// <summary>
    /// 删除关键字
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var entity = await _unitOfWork.Keywords.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "关键字不存在");

        _unitOfWork.Keywords.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
        return Success("删除成功");
    }
}

