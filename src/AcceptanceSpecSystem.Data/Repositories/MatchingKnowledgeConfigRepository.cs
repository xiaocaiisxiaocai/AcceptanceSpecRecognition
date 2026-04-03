using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 匹配知识配置仓储实现。
/// </summary>
public class MatchingKnowledgeConfigRepository : IMatchingKnowledgeConfigRepository
{
    private readonly AppDbContext _context;

    /// <summary>
    /// 初始化匹配知识配置仓储。
    /// </summary>
    /// <param name="context">数据库上下文。</param>
    public MatchingKnowledgeConfigRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取当前匹配知识配置。
    /// </summary>
    public async Task<MatchingKnowledgeConfig?> GetConfigAsync()
    {
        return await _context.MatchingKnowledgeConfigs.FirstOrDefaultAsync();
    }

    /// <summary>
    /// 保存匹配知识配置，始终维持单例语义。
    /// </summary>
    /// <param name="config">待保存配置。</param>
    public async Task SaveConfigAsync(MatchingKnowledgeConfig config)
    {
        var existing = await _context.MatchingKnowledgeConfigs.FirstOrDefaultAsync();

        if (existing == null)
        {
            config.UpdatedAt = DateTime.UtcNow;
            await _context.MatchingKnowledgeConfigs.AddAsync(config);
            return;
        }

        existing.EntityAliasesJson = config.EntityAliasesJson;
        existing.UnitAliasesJson = config.UnitAliasesJson;
        existing.UnitFactorsJson = config.UnitFactorsJson;
        existing.FieldAliasesJson = config.FieldAliasesJson;
        existing.ConflictPairsJson = config.ConflictPairsJson;
        existing.UpdatedAt = DateTime.UtcNow;
    }
}
