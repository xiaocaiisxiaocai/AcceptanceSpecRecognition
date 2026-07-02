using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
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
    private readonly SmartConfigurationAppService _smartConfigService;

    public SmartConfigController(SmartConfigurationAppService smartConfigService)
    {
        _smartConfigService = smartConfigService;
    }

    /// <summary>
    /// 自动识别文档配置
    /// </summary>
    /// <param name="request">请求参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>自动配置结果</returns>
    [HttpPost("auto-detect")]
    [ProducesResponseType(typeof(ApiResponse<AutoConfigResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AutoConfigResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AutoConfigResult>>> AutoDetect(
        [FromBody] AutoDetectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FileId <= 0)
        {
            return Error<AutoConfigResult>(400, "FileId 不能为空");
        }

        try
        {
            var result = await _smartConfigService.AutoConfigureAsync(
                request.FileId,
                request.CustomerId,
                cancellationToken);

            return Success(result, "自动识别完成");
        }
        catch (InvalidOperationException ex)
        {
            return Error<AutoConfigResult>(400, ex.Message);
        }
        catch (Exception ex)
        {
            return Error<AutoConfigResult>(500, $"识别失败：{ex.Message}");
        }
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
                    CustomerId = request.CustomerId
                },
                cancellationToken);

            return Success(result, "识别完成");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SmartConfigurationRecognizeResult>(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return Error<SmartConfigurationRecognizeResult>(500, $"识别失败：{ex.Message}");
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
                    LearnedColumns = request.LearnedColumns.Select(item =>
                        new SmartConfigurationLearnedColumn
                        {
                            Header = item.Header,
                            TargetField = item.TargetField
                        }).ToList()
                },
                cancellationToken);

            return Success(result, "确认成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SmartConfigurationConfirmResult>(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return Error<SmartConfigurationConfirmResult>(500, $"确认失败：{ex.Message}");
        }
    }
}

/// <summary>
/// 自动识别请求
/// </summary>
public class AutoDetectRequest
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public int FileId { get; set; }

    /// <summary>
    /// 客户ID（可选，用于模板匹配）
    /// </summary>
    public int? CustomerId { get; set; }
}
