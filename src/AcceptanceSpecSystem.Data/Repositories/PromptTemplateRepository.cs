using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// Prompt模板Repository实现
/// </summary>
public class PromptTemplateRepository : Repository<PromptTemplate>, IPromptTemplateRepository
{
    /// <summary>
    /// 创建PromptTemplateRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public PromptTemplateRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据名称获取 Prompt 模板。
    /// </summary>
    /// <param name="name">模板名称</param>
    /// <returns>模板或 null</returns>
    public async Task<PromptTemplate?> GetByNameAsync(string name)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.Name == name);
    }

    /// <summary>
    /// 获取默认 Prompt 模板（IsDefault=true）。
    /// </summary>
    /// <returns>默认模板或 null</returns>
    public async Task<PromptTemplate?> GetDefaultAsync()
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.IsDefault);
    }

    /// <summary>
    /// 将指定模板设置为默认模板，并取消当前默认模板（若存在）。
    /// </summary>
    /// <param name="id">模板ID</param>
    public async Task SetDefaultAsync(int id)
    {
        // 先取消当前默认
        var currentDefault = await _dbSet.FirstOrDefaultAsync(p => p.IsDefault);
        if (currentDefault != null)
        {
            currentDefault.IsDefault = false;
        }

        // 设置新默认
        var newDefault = await _dbSet.FindAsync(id);
        if (newDefault != null)
        {
            newDefault.IsDefault = true;
            newDefault.UpdatedAt = DateTime.Now;
        }
    }

    /// <summary>
    /// 获取默认模板；若不存在则创建默认模板并返回（不负责保存上下文以外的额外更改）。
    /// </summary>
    /// <param name="defaultContent">默认模板内容</param>
    /// <returns>默认模板</returns>
    public async Task<PromptTemplate> GetOrCreateDefaultAsync(string defaultContent)
    {
        var template = await GetDefaultAsync();

        if (template == null)
        {
            template = new PromptTemplate
            {
                Name = "default",
                Content = defaultContent,
                IsDefault = true,
                CreatedAt = DateTime.Now
            };

            await _dbSet.AddAsync(template);
        }

        return template;
    }
}
