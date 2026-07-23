using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 文档模板Repository实现
/// </summary>
public class DocumentTemplateRepository : Repository<DocumentTemplate>, IDocumentTemplateRepository
{
    /// <summary>
    /// 创建DocumentTemplateRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public DocumentTemplateRepository(AppDbContext context) : base(context)
    {
    }
}
