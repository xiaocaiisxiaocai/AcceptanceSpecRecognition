using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 文本处理配置Repository实现
/// </summary>
public class TextProcessingConfigRepository : ITextProcessingConfigRepository
{
    private readonly AppDbContext _context;

    /// <summary>
    /// 创建TextProcessingConfigRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public TextProcessingConfigRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取文本处理配置；若不存在则创建默认配置并保存。
    /// </summary>
    /// <returns>文本处理配置</returns>
    public async Task<TextProcessingConfig> GetConfigAsync()
    {
        var config = await _context.TextProcessingConfigs.FirstOrDefaultAsync();

        if (config == null)
        {
            config = CreateDefaultConfig();
            await _context.TextProcessingConfigs.AddAsync(config);
            await _context.SaveChangesAsync();
        }

        return config;
    }

    /// <summary>
    /// 保存文本处理配置（若不存在则新增，存在则更新）。
    /// 注意：该方法只修改 DbContext 跟踪实体，不主动调用 SaveChanges。
    /// </summary>
    /// <param name="config">配置</param>
    public async Task SaveConfigAsync(TextProcessingConfig config)
    {
        var existing = await _context.TextProcessingConfigs.FirstOrDefaultAsync();

        if (existing == null)
        {
            await _context.TextProcessingConfigs.AddAsync(config);
        }
        else
        {
            existing.EnableChineseConversion = config.EnableChineseConversion;
            existing.ConversionMode = config.ConversionMode;
            existing.EnableSynonym = config.EnableSynonym;
            existing.EnableOkNgConversion = config.EnableOkNgConversion;
            existing.OkStandardFormat = config.OkStandardFormat;
            existing.NgStandardFormat = config.NgStandardFormat;
            existing.EnableKeywordHighlight = config.EnableKeywordHighlight;
            existing.HighlightColorHex = config.HighlightColorHex;
            existing.UpdatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// 重置为默认配置（删除旧配置并创建默认配置）。
    /// 注意：该方法只修改 DbContext 跟踪实体，不主动调用 SaveChanges。
    /// </summary>
    /// <returns>默认配置</returns>
    public async Task<TextProcessingConfig> ResetToDefaultAsync()
    {
        var existing = await _context.TextProcessingConfigs.FirstOrDefaultAsync();

        if (existing != null)
        {
            _context.TextProcessingConfigs.Remove(existing);
        }

        var defaultConfig = CreateDefaultConfig();
        await _context.TextProcessingConfigs.AddAsync(defaultConfig);

        return defaultConfig;
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    private static TextProcessingConfig CreateDefaultConfig()
    {
        return new TextProcessingConfig
        {
            EnableChineseConversion = false,
            ConversionMode = ChineseConversionMode.None,
            EnableSynonym = false,
            EnableOkNgConversion = false,
            OkStandardFormat = "OK",
            NgStandardFormat = "NG",
            EnableKeywordHighlight = false,
            HighlightColorHex = "#FFFF00",
            UpdatedAt = DateTime.Now
        };
    }
}
