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
    /// Prompt模板Repository
    /// </summary>
    IPromptTemplateRepository PromptTemplates { get; }

    /// <summary>
    /// Word 列映射规则 Repository
    /// </summary>
    IColumnMappingRuleRepository ColumnMappingRules { get; }

    /// <summary>
    /// 文档结构模板 Repository
    /// </summary>
    IDocumentTemplateRepository DocumentTemplates { get; }

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
    /// 执行记录Repository
    /// </summary>
    IExecutionHistoryRecordRepository ExecutionHistoryRecords { get; }

    /// <summary>
    /// 保存所有更改
    /// </summary>
    /// <returns>受影响的行数</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

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
