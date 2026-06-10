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
    /// 根据场景获取 Prompt 模板。
    /// </summary>
    /// <param name="scene">模板场景</param>
    /// <returns>模板或 null</returns>
    public async Task<PromptTemplate?> GetBySceneAsync(PromptTemplateScene scene)
    {
        return await _dbSet
            .FirstOrDefaultAsync(p => p.Scene == scene && p.IsSystem);
    }

    /// <summary>
    /// 获取系统模板列表。
    /// </summary>
    /// <returns>系统模板</returns>
    public async Task<List<PromptTemplate>> GetSystemTemplatesAsync()
    {
        return await _dbSet
            .Where(p => p.IsSystem)
            .OrderBy(p => p.Scene)
            .ToListAsync();
    }

    /// <summary>
    /// 获取或创建系统模板。
    /// </summary>
    /// <param name="scene">场景</param>
    /// <param name="name">系统键</param>
    /// <param name="displayName">展示名称</param>
    /// <param name="defaultContent">默认内容</param>
    /// <returns>模板</returns>
    public async Task<PromptTemplate> GetOrCreateSystemAsync(
        PromptTemplateScene scene,
        string name,
        string displayName,
        string defaultContent)
    {
        var template = await _dbSet.FirstOrDefaultAsync(p =>
            (p.IsSystem && p.Scene == scene) || p.Name == name);

        if (template == null)
        {
            template = new PromptTemplate
            {
                Name = name,
                DisplayName = displayName,
                Content = defaultContent,
                Scene = scene,
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            };

            await _dbSet.AddAsync(template);
            return template;
        }

        var changed = false;
        if (template.Scene != scene)
        {
            template.Scene = scene;
            changed = true;
        }

        if (!string.Equals(template.Name, name, StringComparison.Ordinal))
        {
            template.Name = name;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(template.DisplayName))
        {
            template.DisplayName = displayName;
            changed = true;
        }

        if (!template.IsSystem)
        {
            template.IsSystem = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(template.Content))
        {
            template.Content = defaultContent;
            changed = true;
        }

        if (changed)
        {
            template.UpdatedAt = DateTime.UtcNow;
        }

        return template;
    }

}
