namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 工作单元接口，用于管理事务
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// 客户Repository
    /// </summary>
    ICustomerRepository Customers { get; }

    /// <summary>
    /// 制程Repository
    /// </summary>
    IProcessRepository Processes { get; }

    /// <summary>
    /// 机型Repository
    /// </summary>
    IMachineModelRepository MachineModels { get; }

    /// <summary>
    /// 验收规格Repository
    /// </summary>
    IAcceptanceSpecRepository AcceptanceSpecs { get; }

    /// <summary>
    /// 向量缓存Repository
    /// </summary>
    IEmbeddingCacheRepository EmbeddingCaches { get; }

    /// <summary>
    /// Word文件Repository
    /// </summary>
    IWordFileRepository WordFiles { get; }

    /// <summary>
    /// AI服务配置Repository
    /// </summary>
    IAiServiceConfigRepository AiServiceConfigs { get; }

    /// <summary>
    /// 同义词Repository
    /// </summary>
    ISynonymRepository Synonyms { get; }

    /// <summary>
    /// 关键字Repository
    /// </summary>
    IKeywordRepository Keywords { get; }

    /// <summary>
    /// 文本处理配置Repository
    /// </summary>
    ITextProcessingConfigRepository TextProcessingConfigs { get; }

    /// <summary>
    /// Prompt模板Repository
    /// </summary>
    IPromptTemplateRepository PromptTemplates { get; }

    /// <summary>
    /// 导入列映射规则Repository（全局）
    /// </summary>
    IColumnMappingRuleRepository ColumnMappingRules { get; }

    /// <summary>
    /// 系统用户Repository
    /// </summary>
    ISystemUserRepository SystemUsers { get; }

    /// <summary>
    /// 审计日志Repository
    /// </summary>
    IAuditLogRepository AuditLogs { get; }

    /// <summary>
    /// 智能填充任务Repository
    /// </summary>
    IMatchingFillTaskRepository MatchingFillTasks { get; }

    /// <summary>
    /// 保存所有更改
    /// </summary>
    /// <returns>受影响的行数</returns>
    Task<int> SaveChangesAsync();

    /// <summary>
    /// 保存所有更改（同步版本）
    /// </summary>
    /// <returns>受影响的行数</returns>
    int SaveChanges();

    /// <summary>
    /// 开始事务
    /// </summary>
    Task BeginTransactionAsync();

    /// <summary>
    /// 提交事务
    /// </summary>
    Task CommitTransactionAsync();

    /// <summary>
    /// 回滚事务
    /// </summary>
    Task RollbackTransactionAsync();
}
