using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 工作单元实现，管理所有Repository和事务。
/// 所有 Repository 实例通过 IServiceProvider 解析，确保生命周期与容器一致。
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private IDbContextTransaction? _transaction;

    private ICustomerRepository? _customers;
    private IProcessRepository? _processes;
    private IMachineModelRepository? _machineModels;
    private IAcceptanceSpecRepository? _acceptanceSpecs;
    private IEmbeddingCacheRepository? _embeddingCaches;
    private IWordFileRepository? _wordFiles;
    private IAiServiceConfigRepository? _aiServiceConfigs;
    private IPromptTemplateRepository? _promptTemplates;
    private IColumnMappingRuleRepository? _columnMappingRules;
    private ISystemUserRepository? _systemUsers;
    private IAuditLogRepository? _auditLogs;
    private IMatchingFillTaskRepository? _matchingFillTasks;
    private IExecutionHistoryRecordRepository? _executionHistoryRecords;

    private bool _disposed;

    /// <summary>
    /// 创建UnitOfWork实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="serviceProvider">服务提供器，用于解析带 DI 生命周期的 Repository</param>
    public UnitOfWork(AppDbContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        _serviceProvider = serviceProvider;
    }

    private TRepo GetOrCreate<TRepo>(ref TRepo? field) where TRepo : class
    {
        return field ??= _serviceProvider.GetRequiredService<TRepo>();
    }

    /// <summary>
    /// 客户数据仓储。
    /// </summary>
    public ICustomerRepository Customers => GetOrCreate(ref _customers);

    /// <summary>
    /// 制程数据仓储。
    /// </summary>
    public IProcessRepository Processes => GetOrCreate(ref _processes);

    /// <summary>
    /// 机型数据仓储。
    /// </summary>
    public IMachineModelRepository MachineModels => GetOrCreate(ref _machineModels);

    /// <summary>
    /// 验收规格数据仓储。
    /// </summary>
    public IAcceptanceSpecRepository AcceptanceSpecs => GetOrCreate(ref _acceptanceSpecs);

    /// <summary>
    /// 向量缓存数据仓储。
    /// </summary>
    public IEmbeddingCacheRepository EmbeddingCaches => GetOrCreate(ref _embeddingCaches);

    /// <summary>
    /// Word 文件数据仓储。
    /// </summary>
    public IWordFileRepository WordFiles => GetOrCreate(ref _wordFiles);

    /// <summary>
    /// AI 服务配置数据仓储。
    /// </summary>
    public IAiServiceConfigRepository AiServiceConfigs => GetOrCreate(ref _aiServiceConfigs);

    /// <summary>
    /// Prompt 模板数据仓储。
    /// </summary>
    public IPromptTemplateRepository PromptTemplates => GetOrCreate(ref _promptTemplates);

    /// <summary>
    /// Word 列映射规则仓储。
    /// </summary>
    public IColumnMappingRuleRepository ColumnMappingRules => GetOrCreate(ref _columnMappingRules);

    /// <summary>
    /// 系统用户仓储。
    /// </summary>
    public ISystemUserRepository SystemUsers => GetOrCreate(ref _systemUsers);

    /// <summary>
    /// 审计日志仓储。
    /// </summary>
    public IAuditLogRepository AuditLogs => GetOrCreate(ref _auditLogs);

    /// <summary>
    /// 智能填充任务仓储。
    /// </summary>
    public IMatchingFillTaskRepository MatchingFillTasks => GetOrCreate(ref _matchingFillTasks);

    /// <summary>
    /// 执行记录仓储。
    /// </summary>
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => GetOrCreate(ref _executionHistoryRecords);

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
        if (_transaction == null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;
        try
        {
            await transaction.CommitAsync();
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch
            {
                // 提交结果以主异常为准，释放失败不应覆盖真实错误。
            }
        }
    }

    /// <summary>
    /// 回滚事务（异步）。若当前无事务则不执行。
    /// </summary>
    public async Task RollbackTransactionAsync()
    {
        if (_transaction == null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // 回滚通常发生在主异常之后，这里不能再用次生异常覆盖原始失败原因。
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch
            {
                // 回滚后的清理失败同样属于次生异常，避免继续污染后续请求。
            }
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
