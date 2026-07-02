using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能配置控制器
/// </summary>
[Route("api/smart-config")]
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
