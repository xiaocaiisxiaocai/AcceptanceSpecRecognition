using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 客户级文档结构模板管理。
/// </summary>
[Route("api/document-templates")]
[Authorize]
public sealed class DocumentTemplatesController : BaseApiController
{
    private readonly DocumentTemplateAppService _appService;

    public DocumentTemplatesController(DocumentTemplateAppService appService) => _appService = appService;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<DocumentTemplateListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<DocumentTemplateListItemDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? customerId = null,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _appService.GetPagedAsync(
            page,
            pageSize,
            customerId,
            keyword,
            cancellationToken);
        return Success(new PagedData<DocumentTemplateListItemDto>
        {
            Items = result.Items,
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<DocumentTemplateDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DocumentTemplateDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DocumentTemplateDetailDto>>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var template = await _appService.GetDetailAsync(id, cancellationToken);
        return template == null
            ? NotFoundResult<DocumentTemplateDetailDto>("结构模板不存在")
            : Success(template);
    }

    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "document-template")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _appService.DeleteAsync(id, cancellationToken);
        return deleted
            ? Success("删除结构模板成功")
            : NotFound(ApiResponse.Error(404, "结构模板不存在"));
    }
}
