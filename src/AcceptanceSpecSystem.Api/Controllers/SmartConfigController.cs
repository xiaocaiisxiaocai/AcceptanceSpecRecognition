using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能配置控制器
/// </summary>
[Route("api/smart-config")]
[Authorize]
public class SmartConfigController : BaseApiController
{
    private readonly ISmartConfigurationAppService _smartConfigService;

    public SmartConfigController(ISmartConfigurationAppService smartConfigService)
    {
        _smartConfigService = smartConfigService;
    }

    [HttpPost("recognize")]
    [ProducesResponseType(typeof(ApiResponse<SmartConfigurationRecognizeResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SmartConfigurationRecognizeResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SmartConfigurationRecognizeResult>>> Recognize(
        [FromBody] SmartConfigRecognizeRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _smartConfigService.RecognizeAsync(
                new SmartConfigurationRecognizeCommand
                {
                    FileId = request.FileId,
                    CustomerId = request.CustomerId,
                    EnableLlmAssistance = request.EnableLlmAssistance,
                    LlmServiceId = request.LlmServiceId
                },
                cancellationToken);

            return Success(result, "识别完成");
        }
        catch (ApplicationServiceException ex) when (ex.Code == 404)
        {
            return NotFoundResult<SmartConfigurationRecognizeResult>(ex.Message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SmartConfigurationRecognizeResult>(ex.Code, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    [HttpPost("confirm")]
    [ProducesResponseType(typeof(ApiResponse<SmartConfigurationConfirmResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SmartConfigurationConfirmResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SmartConfigurationConfirmResult>>> Confirm(
        [FromBody] SmartConfigConfirmRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _smartConfigService.ConfirmAsync(
                new SmartConfigurationConfirmCommand
                {
                    FileId = request.FileId,
                    TableIndex = request.TableIndex,
                    CustomerId = request.CustomerId,
                    TemplateName = request.TemplateName,
                    Headers = request.Headers,
                    ProjectColumnIndex = request.ProjectColumnIndex,
                    SpecificationColumnIndex = request.SpecificationColumnIndex,
                    AcceptanceColumnIndex = request.AcceptanceColumnIndex,
                    RemarkColumnIndex = request.RemarkColumnIndex,
                    HeaderRowIndex = request.HeaderRowIndex,
                    HeaderRowCount = request.HeaderRowCount,
                    DataStartRowIndex = request.DataStartRowIndex,
                    DataEndRowIndex = request.DataEndRowIndex,
                    IsSpecificationOnly = request.IsSpecificationOnly,
                    TableKind = request.TableKind,
                    Recommendation = request.Recommendation,
                    UserModifiedStructure = request.UserModifiedStructure,
                    LearnedColumns = request.LearnedColumns.Select(item =>
                        new SmartConfigurationLearnedColumn
                        {
                            Header = item.Header,
                            TargetField = item.TargetField
                        }).ToList(),
                    Regions = request.Regions.Select(region => new SmartConfigurationConfirmRegion
                    {
                        RegionId = region.RegionId,
                        RegionIndex = region.RegionIndex,
                        Headers = region.Headers,
                        ProjectColumnIndex = region.ProjectColumnIndex,
                        SpecificationColumnIndex = region.SpecificationColumnIndex,
                        AcceptanceColumnIndex = region.AcceptanceColumnIndex,
                        RemarkColumnIndex = region.RemarkColumnIndex,
                        HeaderRowIndex = region.HeaderRowIndex,
                        HeaderRowCount = region.HeaderRowCount,
                        DataStartRowIndex = region.DataStartRowIndex,
                        DataEndRowIndex = region.DataEndRowIndex,
                        IsSpecificationOnly = region.IsSpecificationOnly
                    }).ToList()
                },
                cancellationToken);

            return Success(result, "确认成功");
        }
        catch (ApplicationServiceException ex) when (ex.Code == 404)
        {
            return NotFoundResult<SmartConfigurationConfirmResult>(ex.Message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SmartConfigurationConfirmResult>(ex.Code, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }
}
