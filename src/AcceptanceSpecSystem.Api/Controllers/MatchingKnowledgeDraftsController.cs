using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配知识草稿生成接口。
/// </summary>
[Route("api/matching-knowledge/drafts")]
[Authorize]
public class MatchingKnowledgeDraftsController : BaseApiController
{
    private readonly MatchingKnowledgeDraftGenerationService _draftGenerationService;

    public MatchingKnowledgeDraftsController(MatchingKnowledgeDraftGenerationService draftGenerationService)
    {
        _draftGenerationService = draftGenerationService;
    }

    /// <summary>
    /// 生成匹配知识草稿候选。
    /// </summary>
    [HttpPost("generate")]
    [AuditOperation("generate-draft", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeDraftResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeDraftResponseDto>>> Generate([FromBody] GenerateMatchingKnowledgeDraftRequest request)
    {
        try
        {
            var result = await _draftGenerationService.GenerateAsync(User, request, HttpContext.RequestAborted);
            return Success(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error<MatchingKnowledgeDraftResponseDto>(401, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Error<MatchingKnowledgeDraftResponseDto>(400, ex.Message);
        }
    }
}
