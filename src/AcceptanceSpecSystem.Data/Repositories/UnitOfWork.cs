using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 工作单元实现，管理所有Repository和事务
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    private ICustomerRepository? _customers;
    private IProcessRepository? _processes;
    private IMachineModelRepository? _machineModels;
    private IAcceptanceSpecRepository? _acceptanceSpecs;
    private IEmbeddingCacheRepository? _embeddingCaches;
    private IWordFileRepository? _wordFiles;
    private IAiServiceConfigRepository? _aiServiceConfigs;
    private ISynonymRepository? _synonyms;
    private IKeywordRepository? _keywords;
    private ITextProcessingConfigRepository? _textProcessingConfigs;
    private IPromptTemplateRepository? _promptTemplates;
    private IColumnMappingRuleRepository? _columnMappingRules;
    private ISystemUserRepository? _systemUsers;
    private IAuditLogRepository? _auditLogs;
    private IMatchingFillTaskRepository? _matchingFillTasks;

    private bool _disposed;

    /// <summary>
    /// 创建UnitOfWork实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 客户数据仓储。
    /// </summary>
    public ICustomerRepository Customers =>
        _customers ??= new CustomerRepository(_context);

    /// <summary>
    /// 制程数据仓储。
    /// </summary>
    public IProcessRepository Processes =>
        _processes ??= new ProcessRepository(_context);

    /// <summary>
    /// 机型数据仓储。
    /// </summary>
    public IMachineModelRepository MachineModels =>
        _machineModels ??= new MachineModelRepository(_context);

    /// <summary>
    /// 验收规格数据仓储。
    /// </summary>
    public IAcceptanceSpecRepository AcceptanceSpecs =>
        _acceptanceSpecs ??= new AcceptanceSpecRepository(_context);

    /// <summary>
    /// 向量缓存数据仓储。
    /// </summary>
    public IEmbeddingCacheRepository EmbeddingCaches =>
        _embeddingCaches ??= new EmbeddingCacheRepository(_context);

    /// <summary>
    /// Word 文件数据仓储。
    /// </summary>
    public IWordFileRepository WordFiles =>
        _wordFiles ??= new WordFileRepository(_context);

    /// <summary>
    /// AI 服务配置数据仓储。
    /// </summary>
    public IAiServiceConfigRepository AiServiceConfigs =>
        _aiServiceConfigs ??= new AiServiceConfigRepository(_context);

    /// <summary>
    /// 同义词数据仓储。
    /// </summary>
    public ISynonymRepository Synonyms =>
        _synonyms ??= new SynonymRepository(_context);

    /// <summary>
    /// 关键字数据仓储。
    /// </summary>
    public IKeywordRepository Keywords =>
        _keywords ??= new KeywordRepository(_context);

    /// <summary>
    /// 文本处理配置数据仓储。
    /// </summary>
    public ITextProcessingConfigRepository TextProcessingConfigs =>
        _textProcessingConfigs ??= new TextProcessingConfigRepository(_context);

    /// <summary>
    /// Prompt 模板数据仓储。
    /// </summary>
    public IPromptTemplateRepository PromptTemplates =>
        _promptTemplates ??= new PromptTemplateRepository(_context);

    /// <summary>
    /// 导入列映射规则数据仓储（全局）。
    /// </summary>
    public IColumnMappingRuleRepository ColumnMappingRules =>
        _columnMappingRules ??= new ColumnMappingRuleRepository(_context);

    /// <summary>
    /// 系统用户仓储。
    /// </summary>
    public ISystemUserRepository SystemUsers =>
        _systemUsers ??= new SystemUserRepository(_context);

    /// <summary>
    /// 审计日志仓储。
    /// </summary>
    public IAuditLogRepository AuditLogs =>
        _auditLogs ??= new AuditLogRepository(_context);

    /// <summary>
    /// 智能填充任务仓储。
    /// </summary>
    public IMatchingFillTaskRepository MatchingFillTasks =>
        _matchingFillTasks ??= new MatchingFillTaskRepository(_context);

    /// <summary>
    /// 保存所有更改（异步）。
    /// </summary>
    /// <returns>受影响的行数</returns>
    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 保存所有更改（同步）。
    /// </summary>
    /// <returns>受影响的行数</returns>
    public int SaveChanges()
    {
        return _context.SaveChanges();
    }

    /// <summary>
    /// 开始数据库事务（异步）。
    /// </summary>
    public async Task BeginTransactionAsync()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    /// <summary>
    /// 提交事务（异步）。若当前无事务则不执行。
    /// </summary>
    public async Task CommitTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    /// <summary>
    /// 回滚事务（异步）。若当前无事务则不执行。
    /// </summary>
    public async Task RollbackTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    /// <param name="disposing">是否正在释放</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _transaction?.Dispose();
            _context.Dispose();
        }
        _disposed = true;
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
