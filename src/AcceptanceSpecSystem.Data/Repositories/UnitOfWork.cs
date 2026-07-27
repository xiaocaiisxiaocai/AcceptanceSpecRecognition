using AcceptanceSpecSystem.Data.Context;
using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 工作单元实现，管理所有Repository和事务。
/// 所有 Repository 实例通过 IServiceProvider 解析，确保生命周期与容器一致。
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private static readonly ConcurrentDictionary<string, LocalLockEntry> LocalOperationLocks = new(StringComparer.Ordinal);
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
    private ISmartStructureRoutingRuleRepository? _smartStructureRoutingRules;
    private IDocumentTemplateRepository? _documentTemplates;
    private ISystemUserRepository? _systemUsers;
    private IAuditLogRepository? _auditLogs;
    private IMatchingFillTaskRepository? _matchingFillTasks;
    private IExecutionHistoryRecordRepository? _executionHistoryRecords;
    private IOrgUnitRepository? _orgUnits;

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

    public async Task<IAsyncDisposable> AcquireOperationLockAsync(
        string operationKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
        {
            throw new ArgumentException("操作锁键不能为空", nameof(operationKey));
        }

        var normalizedKey = BuildBoundedLockKey(operationKey);
        if (_context.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere)
            {
                await _context.Database.OpenConnectionAsync(cancellationToken);
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT GET_LOCK(@operationLockName, 30);";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@operationLockName";
                parameter.Value = normalizedKey;
                command.Parameters.Add(parameter);
                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (Convert.ToInt32(result) != 1)
                {
                    throw new TimeoutException("等待数据库操作锁超时，请稍后重试");
                }

                return new MySqlOperationLockLease(_context, normalizedKey, openedHere);
            }
            catch
            {
                if (openedHere)
                {
                    await _context.Database.CloseConnectionAsync();
                }
                throw;
            }
        }

        var entry = RentLocalEntry(normalizedKey);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new LocalOperationLockLease(normalizedKey, entry);
        }
        catch
        {
            ReleaseLocalReference(normalizedKey, entry, releaseSemaphore: false);
            throw;
        }
    }

    private static string BuildBoundedLockKey(string operationKey)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operationKey.Trim())));
        return $"ass:{digest[..60]}";
    }

    private static LocalLockEntry RentLocalEntry(string key)
    {
        while (true)
        {
            var candidate = new LocalLockEntry();
            var entry = LocalOperationLocks.GetOrAdd(key, candidate);
            if (!ReferenceEquals(candidate, entry))
            {
                candidate.Semaphore.Dispose();
            }

            lock (entry)
            {
                if (entry.Retired)
                {
                    continue;
                }

                entry.ReferenceCount++;
                return entry;
            }
        }
    }

    private static void ReleaseLocalReference(string key, LocalLockEntry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        var shouldRemove = false;
        lock (entry)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                entry.Retired = true;
                shouldRemove = true;
            }
        }

        if (shouldRemove && LocalOperationLocks.TryRemove(new KeyValuePair<string, LocalLockEntry>(key, entry)))
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class LocalLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount;

        public bool Retired;
    }

    private sealed class LocalOperationLockLease : IAsyncDisposable
    {
        private readonly string _key;
        private LocalLockEntry? _entry;

        public LocalOperationLockLease(string key, LocalLockEntry entry)
        {
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry != null)
            {
                ReleaseLocalReference(_key, entry, releaseSemaphore: true);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MySqlOperationLockLease : IAsyncDisposable
    {
        private readonly AppDbContext _context;
        private readonly string _key;
        private readonly bool _closeConnection;
        private int _disposed;

        public MySqlOperationLockLease(AppDbContext context, string key, bool closeConnection)
        {
            _context = context;
            _key = key;
            _closeConnection = closeConnection;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT RELEASE_LOCK(@operationLockName);";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@operationLockName";
                    parameter.Value = _key;
                    command.Parameters.Add(parameter);
                    await command.ExecuteScalarAsync();
                }
            }
            finally
            {
                if (_closeConnection)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }
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
    /// 智能结构识别表格路由规则仓储。
    /// </summary>
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => GetOrCreate(ref _smartStructureRoutingRules);

    /// <summary>
    /// 文档结构模板仓储。
    /// </summary>
    public IDocumentTemplateRepository DocumentTemplates => GetOrCreate(ref _documentTemplates);

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
    /// 组织节点仓储。
    /// </summary>
    public IOrgUnitRepository OrgUnits => GetOrCreate(ref _orgUnits);


    /// <summary>
    /// 保存所有更改（异步）。
    /// </summary>
    /// <returns>受影响的行数</returns>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
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
    public Task BeginTransactionAsync() => BeginTransactionAsync(CancellationToken.None);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        _transaction = await _context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    /// <summary>
    /// 提交事务（异步）。若当前无事务则不执行。
    /// </summary>
    public Task CommitTransactionAsync() => CommitTransactionAsync(CancellationToken.None);

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction == null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;
        try
        {
            await transaction.CommitAsync(cancellationToken);
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
    public Task RollbackTransactionAsync() => RollbackTransactionAsync(CancellationToken.None);

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction == null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;
        try
        {
            await transaction.RollbackAsync(cancellationToken);
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
