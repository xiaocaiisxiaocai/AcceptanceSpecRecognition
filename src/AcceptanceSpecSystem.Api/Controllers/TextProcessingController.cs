using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 文本处理配置管理API控制器
/// </summary>
[Route("api/text-processing")]
public class TextProcessingController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TextProcessingController> _logger;

    public TextProcessingController(IUnitOfWork unitOfWork, ILogger<TextProcessingController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 获取文本处理配置（单例）
    /// </summary>
    [HttpGet("config")]
    [ProducesResponseType(typeof(ApiResponse<TextProcessingConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TextProcessingConfigDto>>> GetConfig()
    {
        var cfg = await _unitOfWork.TextProcessingConfigs.GetConfigAsync();
        return Success(ToDto(cfg));
    }

    /// <summary>
    /// 保存文本处理配置（单例）
    /// </summary>
    [HttpPut("config")]
    [ProducesResponseType(typeof(ApiResponse<TextProcessingConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TextProcessingConfigDto>>> SaveConfig([FromBody] UpdateTextProcessingConfigRequest request)
    {
        // 基础校验
        if (string.IsNullOrWhiteSpace(request.OkStandardFormat))
            return Error<TextProcessingConfigDto>(400, "OK标准格式不能为空");
        if (string.IsNullOrWhiteSpace(request.NgStandardFormat))
            return Error<TextProcessingConfigDto>(400, "NG标准格式不能为空");

        var cfg = new TextProcessingConfig
        {
            EnableChineseConversion = request.EnableChineseConversion,
            ConversionMode = request.ConversionMode,
            EnableSynonym = request.EnableSynonym,
            EnableOkNgConversion = request.EnableOkNgConversion,
            OkStandardFormat = request.OkStandardFormat.Trim(),
            NgStandardFormat = request.NgStandardFormat.Trim(),
            EnableKeywordHighlight = request.EnableKeywordHighlight,
            HighlightColorHex = string.IsNullOrWhiteSpace(request.HighlightColorHex) ? "#FFFF00" : request.HighlightColorHex.Trim(),
            UpdatedAt = DateTime.Now
        };

        await _unitOfWork.TextProcessingConfigs.SaveConfigAsync(cfg);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.TextProcessingConfigs.GetConfigAsync();
        _logger.LogInformation("保存文本处理配置: {Id}", saved.Id);
        return Success(ToDto(saved), "保存成功");
    }

    /// <summary>
    /// 重置为默认配置
    /// </summary>
    [HttpPost("config/reset")]
    [ProducesResponseType(typeof(ApiResponse<TextProcessingConfigDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<TextProcessingConfigDto>>> Reset()
    {
        var cfg = await _unitOfWork.TextProcessingConfigs.ResetToDefaultAsync();
        await _unitOfWork.SaveChangesAsync();
        return Success(ToDto(cfg), "已重置为默认配置");
    }

    private static TextProcessingConfigDto ToDto(TextProcessingConfig c) => new()
    {
        Id = c.Id,
        EnableChineseConversion = c.EnableChineseConversion,
        ConversionMode = c.ConversionMode,
        EnableSynonym = c.EnableSynonym,
        EnableOkNgConversion = c.EnableOkNgConversion,
        OkStandardFormat = c.OkStandardFormat,
        NgStandardFormat = c.NgStandardFormat,
        EnableKeywordHighlight = c.EnableKeywordHighlight,
        HighlightColorHex = c.HighlightColorHex,
        UpdatedAt = c.UpdatedAt
    };
}

