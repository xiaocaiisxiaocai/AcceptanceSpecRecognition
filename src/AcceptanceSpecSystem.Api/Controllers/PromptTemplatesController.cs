using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/prompt-templates")]
[Authorize]
public class PromptTemplatesController : BaseApiController
{
    private readonly IPromptTemplateAppService _appService;

    public PromptTemplatesController(IPromptTemplateAppService appService) => _appService = appService;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<PromptTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<PromptTemplateDto>>>> GetList(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _appService.GetPagedAsync(page, pageSize, keyword, cancellationToken);
        return Success(new PagedData<PromptTemplateDto>
        {
            Items = result.Items, Total = result.Total, Page = result.Page, PageSize = result.PageSize
        });
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var item = await _appService.GetByIdAsync(id, cancellationToken);
        return item == null ? NotFoundResult<PromptTemplateDto>("模板不存在") : Success(item);
    }

    [HttpPut("{id:int}")]
    [AuditOperation("update", "prompt-template")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> Update(
        int id, [FromBody] UpdatePromptTemplateRequest request, CancellationToken cancellationToken = default)
    {
        try { return Success(await _appService.UpdateAsync(id, request, cancellationToken), "更新成功"); }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404 ? NotFoundResult<PromptTemplateDto>(ex.Message) : Error<PromptTemplateDto>(ex.Code, ex.Message);
        }
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<PreviewPromptTemplateResponse>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<PreviewPromptTemplateResponse>> Preview([FromBody] PreviewPromptTemplateRequest request)
    {
        try { return Success(_appService.Preview(request)); }
        catch (ApplicationServiceException ex) { return Error<PreviewPromptTemplateResponse>(ex.Code, ex.Message); }
    }

    [HttpPost("reset-system/{scene}")]
    [AuditOperation("reset-system", "prompt-template")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> ResetSystem(
        string scene, CancellationToken cancellationToken = default)
    {
        try { return Success(await _appService.ResetSystemAsync(scene, cancellationToken), "恢复默认成功"); }
        catch (ApplicationServiceException ex) { return Error<PromptTemplateDto>(ex.Code, ex.Message); }
    }
}
