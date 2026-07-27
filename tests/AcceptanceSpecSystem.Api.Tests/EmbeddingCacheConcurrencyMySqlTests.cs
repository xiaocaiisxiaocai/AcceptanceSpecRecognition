using System.Reflection;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class EmbeddingCacheConcurrencyMySqlTests
{
    private const string EmbeddingModel = "embedding-concurrency-model";
    private const string Usage = EmbeddingCacheUsages.SemanticSearch;
    private static readonly float[] GeneratedVector = [0.25f, 0.5f, 0.75f];
    private static readonly float[] PersistedWinnerVector = [0.9f, 0.8f, 0.7f];

    [Fact]
    public async Task 模拟同键唯一冲突_应分离失败新增项并重读赢家后继续使用同一上下文()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var spec = await SeedAcceptanceSpecAsync(context);
        using var unitOfWork = new SimulatedDuplicateEmbeddingUnitOfWork(context, PersistedWinnerVector);
        await using var recoveryProvider = CreateRecoveryProvider(options);
        var service = CreateService(
            unitOfWork,
            new FixedEmbeddingService(GeneratedVector),
            recoveryProvider.GetRequiredService<IServiceScopeFactory>());

        var results = await service.GetOrCreateForSpecsAsync(
            [spec],
            Usage,
            embeddingServiceId: null,
            CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Embedding.Should().Equal(PersistedWinnerVector);
        context.ChangeTracker.Entries<EmbeddingCache>()
            .Should()
            .NotContain(entry => entry.State == EntityState.Added);

        await unitOfWork.EmbeddingCaches.AddAsync(new EmbeddingCache
        {
            SpecId = spec.Id,
            ModelName = EmbeddingModel,
            Usage = EmbeddingCacheUsages.Matching,
            TextHash = "subsequent-save",
            Vector = [1, 2, 3]
        });
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        (await context.EmbeddingCaches.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task 非当前批次条目报告同索引冲突_应原样重抛而不是进入缓存恢复()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var spec = await SeedAcceptanceSpecAsync(context);
        var foreignEntry = context.EmbeddingCaches.Add(new EmbeddingCache
        {
            SpecId = spec.Id,
            ModelName = EmbeddingModel,
            Usage = EmbeddingCacheUsages.Matching,
            TextHash = "foreign-entry",
            Vector = [1, 2, 3]
        });
        using var unitOfWork = new ForeignEntryDuplicateEmbeddingUnitOfWork(
            context,
            foreignEntry);
        await using var recoveryProvider = CreateRecoveryProvider(options);
        var service = CreateService(
            unitOfWork,
            new FixedEmbeddingService(GeneratedVector),
            recoveryProvider.GetRequiredService<IServiceScopeFactory>());

        var action = () => service.GetOrCreateForSpecsAsync(
            [spec],
            Usage,
            embeddingServiceId: null,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<DbUpdateException>();
        exception.Which.Entries.Should().ContainSingle()
            .Which.Entity.Should().BeSameAs(foreignEntry.Entity);
    }

    [Fact]
    public async Task 唯一冲突恢复_调用方无关新增和修改不应被提交且跟踪状态保持待处理()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var spec = await SeedAcceptanceSpecAsync(context);
        var originalCustomerName = spec.Customer.Name;
        spec.Customer.Name = "调用方尚未提交的修改";
        var pendingCustomer = new Customer { Name = $"调用方尚未提交的新增-{Guid.NewGuid():N}" };
        context.Customers.Add(pendingCustomer);
        using var unitOfWork = new ExternalStaleWinnerEmbeddingUnitOfWork(
            context,
            options,
            PersistedWinnerVector);
        await using var recoveryProvider = CreateRecoveryProvider(options);
        var service = CreateService(
            unitOfWork,
            new FixedEmbeddingService(GeneratedVector),
            recoveryProvider.GetRequiredService<IServiceScopeFactory>());

        var results = await service.GetOrCreateForSpecsAsync(
            [spec],
            Usage,
            embeddingServiceId: null,
            CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Embedding.Should().Equal(GeneratedVector);
        context.Entry(spec.Customer).State.Should().Be(EntityState.Modified);
        context.Entry(pendingCustomer).State.Should().Be(EntityState.Added);

        await using var verificationContext = new AppDbContext(options);
        (await verificationContext.Customers
            .AsNoTracking()
            .SingleAsync(customer => customer.Id == spec.CustomerId))
            .Name.Should().Be(originalCustomerName);
        (await verificationContext.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Name == pendingCustomer.Name))
            .Should().BeFalse();
    }

    [Fact]
    public async Task 生成完成后发生取消_当前批次缓存不应遗留为待新增()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var spec = await SeedAcceptanceSpecAsync(context);
        using var unitOfWork = new EmbeddingCacheTestUnitOfWork(context);
        using var cancellationSource = new CancellationTokenSource();
        await using var recoveryProvider = CreateRecoveryProvider(options);
        var service = CreateService(
            unitOfWork,
            new CancelAfterEmbeddingService(GeneratedVector, cancellationSource),
            recoveryProvider.GetRequiredService<IServiceScopeFactory>());

        var action = () => service.GetOrCreateForSpecsAsync(
            [spec],
            Usage,
            embeddingServiceId: null,
            cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        context.ChangeTracker.Entries<EmbeddingCache>()
            .Should()
            .NotContain(entry =>
                entry.State == EntityState.Added ||
                entry.State == EntityState.Modified);
    }

    [MySqlSmokeFact]
    public async Task 真实MySql同键并发写入_应收敛为唯一行并返回等价向量()
    {
        await using var database = await MySqlEmbeddingCacheTestDatabase.CreateAsync();
        await database.MigrateAsync();
        int specId;
        AcceptanceSpec firstSpec;
        AcceptanceSpec secondSpec;
        await using (var seedContext = database.CreateDbContext())
        {
            var seeded = await SeedAcceptanceSpecAsync(seedContext);
            specId = seeded.Id;
            firstSpec = CopySpec(seeded);
            secondSpec = CopySpec(seeded);
        }

        var arrivalGate = new TimedAsyncArrivalGate(participantCount: 2, timeout: TimeSpan.FromSeconds(15));
        var embeddingService = new BarrierEmbeddingService(arrivalGate, GeneratedVector);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        using var firstUnitOfWork = new EmbeddingCacheTestUnitOfWork(firstContext);
        using var secondUnitOfWork = new EmbeddingCacheTestUnitOfWork(secondContext);
        await using var recoveryProvider = CreateRecoveryProvider(
            database.CreateDbContextOptions());
        var scopeFactory = recoveryProvider.GetRequiredService<IServiceScopeFactory>();
        var firstService = CreateService(firstUnitOfWork, embeddingService, scopeFactory);
        var secondService = CreateService(secondUnitOfWork, embeddingService, scopeFactory);

        var results = await Task.WhenAll(
            firstService.GetOrCreateForSpecsAsync([firstSpec], Usage, null, CancellationToken.None),
            secondService.GetOrCreateForSpecsAsync([secondSpec], Usage, null, CancellationToken.None));

        results[0].Should().ContainSingle();
        results[1].Should().ContainSingle();
        results[0][0].Embedding.Should().Equal(results[1][0].Embedding);
        results[0][0].Embedding.Should().Equal(GeneratedVector);

        await using var verificationContext = database.CreateDbContext();
        (await verificationContext.EmbeddingCaches.CountAsync(cache =>
            cache.SpecId == specId &&
            cache.ModelName == EmbeddingModel &&
            cache.Usage == Usage)).Should().Be(1);

        firstContext.ChangeTracker.Entries<EmbeddingCache>()
            .Should()
            .NotContain(entry => entry.State == EntityState.Added);
        secondContext.ChangeTracker.Entries<EmbeddingCache>()
            .Should()
            .NotContain(entry => entry.State == EntityState.Added);
        (await firstUnitOfWork.SaveChangesAsync(CancellationToken.None)).Should().Be(0);
        (await secondUnitOfWork.SaveChangesAsync(CancellationToken.None)).Should().Be(0);
    }

    [MySqlSmokeFact]
    public async Task 真实MySql混合批次并发写入_共享键冲突时双方独立键仍应落库且上下文保持干净()
    {
        await using var database = await MySqlEmbeddingCacheTestDatabase.CreateAsync();
        await database.MigrateAsync();
        AcceptanceSpec sharedFirst;
        AcceptanceSpec sharedSecond;
        AcceptanceSpec firstExclusive;
        AcceptanceSpec secondExclusive;
        await using (var seedContext = database.CreateDbContext())
        {
            var shared = await SeedAcceptanceSpecAsync(seedContext, "共享");
            firstExclusive = CopySpec(await SeedAcceptanceSpecAsync(seedContext, "请求一独立"));
            secondExclusive = CopySpec(await SeedAcceptanceSpecAsync(seedContext, "请求二独立"));
            sharedFirst = CopySpec(shared);
            sharedSecond = CopySpec(shared);
        }

        var arrivalGate = new TimedAsyncArrivalGate(participantCount: 2, timeout: TimeSpan.FromSeconds(15));
        var embeddingService = new BarrierEmbeddingService(arrivalGate, GeneratedVector);
        await using var firstContext = database.CreateDbContext();
        await using var secondContext = database.CreateDbContext();
        using var firstUnitOfWork = new EmbeddingCacheTestUnitOfWork(firstContext);
        using var secondUnitOfWork = new EmbeddingCacheTestUnitOfWork(secondContext);
        await using var recoveryProvider = CreateRecoveryProvider(
            database.CreateDbContextOptions());
        var scopeFactory = recoveryProvider.GetRequiredService<IServiceScopeFactory>();
        var firstService = CreateService(firstUnitOfWork, embeddingService, scopeFactory);
        var secondService = CreateService(secondUnitOfWork, embeddingService, scopeFactory);

        var results = await Task.WhenAll(
            firstService.GetOrCreateForSpecsAsync(
                [sharedFirst, firstExclusive],
                Usage,
                null,
                CancellationToken.None),
            secondService.GetOrCreateForSpecsAsync(
                [sharedSecond, secondExclusive],
                Usage,
                null,
                CancellationToken.None));

        results.Should().OnlyContain(batch => batch.Count == 2);
        results.SelectMany(batch => batch)
            .Should().OnlyContain(result => result.Embedding.SequenceEqual(GeneratedVector));

        await using var verificationContext = database.CreateDbContext();
        var persistedSpecIds = await verificationContext.EmbeddingCaches
            .Where(cache => cache.ModelName == EmbeddingModel && cache.Usage == Usage)
            .Select(cache => cache.SpecId)
            .OrderBy(id => id)
            .ToListAsync();
        persistedSpecIds.Should().BeEquivalentTo(
            [sharedFirst.Id, firstExclusive.Id, secondExclusive.Id]);

        firstContext.ChangeTracker.Entries<EmbeddingCache>()
            .Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        secondContext.ChangeTracker.Entries<EmbeddingCache>()
            .Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        (await firstUnitOfWork.SaveChangesAsync(CancellationToken.None)).Should().Be(0);
        (await secondUnitOfWork.SaveChangesAsync(CancellationToken.None)).Should().Be(0);
    }

    private static SpecEmbeddingCacheService CreateService(
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        IServiceScopeFactory scopeFactory) =>
        new(
            unitOfWork,
            embeddingService,
            new FixedAiServiceSelector(),
            scopeFactory,
            NullLogger<SpecEmbeddingCacheService>.Instance);

    private static ServiceProvider CreateRecoveryProvider(
        DbContextOptions<AppDbContext> options)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddScoped<IUnitOfWork>(provider =>
            new EmbeddingCacheTestUnitOfWork(provider.GetRequiredService<AppDbContext>()));
        return services.BuildServiceProvider();
    }

    private static async Task<AcceptanceSpec> SeedAcceptanceSpecAsync(
        AppDbContext context,
        string scenario = "同一")
    {
        var spec = new AcceptanceSpec
        {
            Customer = new Customer { Name = $"缓存并发客户-{Guid.NewGuid():N}" },
            WordFile = new WordFile
            {
                FileName = "embedding-concurrency.docx",
                FileHash = Guid.NewGuid().ToString("N"),
                FileContent = []
            },
            Project = $"缓存并发项目-{scenario}",
            Specification = $"{scenario}规格应复用唯一向量",
            Acceptance = "并发调用返回相同结果"
        };
        context.AcceptanceSpecs.Add(spec);
        await context.SaveChangesAsync();
        return spec;
    }

    private static AcceptanceSpec CopySpec(AcceptanceSpec source) => new()
    {
        Id = source.Id,
        CustomerId = source.CustomerId,
        WordFileId = source.WordFileId,
        Project = source.Project,
        Specification = source.Specification,
        Acceptance = source.Acceptance,
        Remark = source.Remark
    };
}

internal class EmbeddingCacheTestUnitOfWork : IUnitOfWork
{
    protected readonly AppDbContext Context;

    public EmbeddingCacheTestUnitOfWork(AppDbContext context)
    {
        Context = context;
        EmbeddingCaches = new EmbeddingCacheRepository(context);
    }

    public IEmbeddingCacheRepository EmbeddingCaches { get; }
    public ICustomerRepository Customers => throw new NotSupportedException();
    public IProcessRepository Processes => throw new NotSupportedException();
    public IMachineModelRepository MachineModels => throw new NotSupportedException();
    public IAcceptanceSpecRepository AcceptanceSpecs => throw new NotSupportedException();
    public IWordFileRepository WordFiles => throw new NotSupportedException();
    public IAiServiceConfigRepository AiServiceConfigs => throw new NotSupportedException();
    public IPromptTemplateRepository PromptTemplates => throw new NotSupportedException();
    public IColumnMappingRuleRepository ColumnMappingRules => throw new NotSupportedException();
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => throw new NotSupportedException();
    public IDocumentTemplateRepository DocumentTemplates => throw new NotSupportedException();
    public ISystemUserRepository SystemUsers => throw new NotSupportedException();
    public IAuditLogRepository AuditLogs => throw new NotSupportedException();
    public IMatchingFillTaskRepository MatchingFillTasks => throw new NotSupportedException();
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => throw new NotSupportedException();
    public IOrgUnitRepository OrgUnits => throw new NotSupportedException();

    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Context.SaveChangesAsync(cancellationToken);

    public int SaveChanges() => Context.SaveChanges();
    public Task BeginTransactionAsync() => throw new NotSupportedException();
    public Task CommitTransactionAsync() => throw new NotSupportedException();
    public Task RollbackTransactionAsync() => throw new NotSupportedException();
    public void Dispose()
    {
    }
}

internal sealed class SimulatedDuplicateEmbeddingUnitOfWork : EmbeddingCacheTestUnitOfWork
{
    private const string UniqueIndex = "IX_EmbeddingCaches_SpecId_ModelName_Usage";
    private readonly float[] _winnerVector;
    private bool _conflictRaised;

    public SimulatedDuplicateEmbeddingUnitOfWork(AppDbContext context, float[] winnerVector)
        : base(context)
    {
        _winnerVector = winnerVector;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_conflictRaised)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        _conflictRaised = true;
        var failedEntry = Context.ChangeTracker.Entries<EmbeddingCache>()
            .Single(entry => entry.State == EntityState.Added);
        var failedCache = failedEntry.Entity;
        failedEntry.State = EntityState.Detached;

        Context.EmbeddingCaches.Add(new EmbeddingCache
        {
            SpecId = failedCache.SpecId,
            ModelName = failedCache.ModelName,
            Usage = failedCache.Usage,
            TextHash = failedCache.TextHash,
            Vector = SerializeVector(_winnerVector),
            CreatedAt = failedCache.CreatedAt
        });
        await Context.SaveChangesAsync(cancellationToken);

        failedEntry.State = EntityState.Added;
        throw new DbUpdateException(
            "模拟同键缓存唯一冲突",
            MySqlExceptionFactory.Create(
                MySqlErrorCode.DuplicateKeyEntry,
                $"Duplicate entry for key '{UniqueIndex}'"),
            [failedEntry]);
    }

    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

internal sealed class ForeignEntryDuplicateEmbeddingUnitOfWork : EmbeddingCacheTestUnitOfWork
{
    private const string UniqueIndex = "IX_EmbeddingCaches_SpecId_ModelName_Usage";
    private readonly EntityEntry<EmbeddingCache> _foreignEntry;
    private bool _conflictRaised;

    public ForeignEntryDuplicateEmbeddingUnitOfWork(
        AppDbContext context,
        EntityEntry<EmbeddingCache> foreignEntry)
        : base(context)
    {
        _foreignEntry = foreignEntry;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_conflictRaised)
        {
            return base.SaveChangesAsync(cancellationToken);
        }

        _conflictRaised = true;
        throw new DbUpdateException(
            "模拟非当前批次条目报告同索引冲突",
            MySqlExceptionFactory.Create(
                MySqlErrorCode.DuplicateKeyEntry,
                $"Duplicate entry for key '{UniqueIndex}'"),
            [_foreignEntry]);
    }
}

internal sealed class ExternalStaleWinnerEmbeddingUnitOfWork : EmbeddingCacheTestUnitOfWork
{
    private const string UniqueIndex = "IX_EmbeddingCaches_SpecId_ModelName_Usage";
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly float[] _winnerVector;
    private bool _conflictRaised;

    public ExternalStaleWinnerEmbeddingUnitOfWork(
        AppDbContext context,
        DbContextOptions<AppDbContext> options,
        float[] winnerVector)
        : base(context)
    {
        _options = options;
        _winnerVector = winnerVector;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_conflictRaised)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        _conflictRaised = true;
        var failedEntry = Context.ChangeTracker.Entries<EmbeddingCache>()
            .Single(entry => entry.State == EntityState.Added);
        var failedCache = failedEntry.Entity;
        await using (var winnerContext = new AppDbContext(_options))
        {
            winnerContext.EmbeddingCaches.Add(new EmbeddingCache
            {
                SpecId = failedCache.SpecId,
                ModelName = failedCache.ModelName,
                Usage = failedCache.Usage,
                TextHash = "stale-winner",
                Vector = SerializeVector(_winnerVector),
                CreatedAt = failedCache.CreatedAt
            });
            await winnerContext.SaveChangesAsync(cancellationToken);
        }

        throw new DbUpdateException(
            "模拟外部赢家造成当前批次唯一冲突",
            MySqlExceptionFactory.Create(
                MySqlErrorCode.DuplicateKeyEntry,
                $"Duplicate entry for key '{UniqueIndex}'"),
            [failedEntry]);
    }

    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

internal sealed class FixedAiServiceSelector : IAiServiceSelector
{
    public Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
        CoreAiServicePurpose purpose,
        int? preferredId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AiServiceConfigModel>>(
            [new AiServiceConfigModel
            {
                Id = 1,
                Purpose = CoreAiServicePurpose.Embedding,
                EmbeddingModel = "embedding-concurrency-model"
            }]);
}

internal class FixedEmbeddingService : IEmbeddingService
{
    private readonly float[] _vector;

    public FixedEmbeddingService(float[] vector) => _vector = vector;

    public bool IsAvailable => true;

    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        int? serviceId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_vector.ToArray());

    public virtual Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        int? serviceId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(texts.Select(_ => _vector.ToArray()).ToList());

    public double ComputeSimilarity(float[] embedding1, float[] embedding2) => 0;
}

internal sealed class BarrierEmbeddingService : FixedEmbeddingService
{
    private readonly TimedAsyncArrivalGate _arrivalGate;

    public BarrierEmbeddingService(TimedAsyncArrivalGate arrivalGate, float[] vector)
        : base(vector)
    {
        _arrivalGate = arrivalGate;
    }

    public override async Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        int? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        await _arrivalGate.ArriveAsync(cancellationToken);
        return await base.GenerateEmbeddingsAsync(texts, serviceId, cancellationToken);
    }
}

internal sealed class CancelAfterEmbeddingService : FixedEmbeddingService
{
    private readonly CancellationTokenSource _cancellationSource;

    public CancelAfterEmbeddingService(
        float[] vector,
        CancellationTokenSource cancellationSource)
        : base(vector)
    {
        _cancellationSource = cancellationSource;
    }

    public override async Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        int? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await base.GenerateEmbeddingsAsync(
            texts,
            serviceId,
            cancellationToken);
        _cancellationSource.Cancel();
        return embeddings;
    }
}

internal sealed class TimedAsyncArrivalGate
{
    private readonly int _participantCount;
    private readonly TimeSpan _timeout;
    private readonly TaskCompletionSource _allArrived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public TimedAsyncArrivalGate(int participantCount, TimeSpan timeout)
    {
        _participantCount = participantCount;
        _timeout = timeout;
    }

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrivals) == _participantCount)
        {
            _allArrived.TrySetResult();
        }

        await _allArrived.Task.WaitAsync(_timeout, cancellationToken);
    }
}

internal static class MySqlExceptionFactory
{
    public static MySqlException Create(MySqlErrorCode errorCode, string message)
    {
        var constructor = typeof(MySqlException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(MySqlErrorCode), typeof(string), typeof(string), typeof(Exception)],
            modifiers: null);
        if (constructor == null)
        {
            throw new InvalidOperationException("无法构造 MySqlConnector provider 异常");
        }

        return (MySqlException)constructor.Invoke([errorCode, "23000", message, null]);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class MySqlSmokeFactAttribute : FactAttribute
{
    public MySqlSmokeFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable(
            MySqlEmbeddingCacheTestDatabase.EnableEnvironmentVariableName)?.Trim();
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip =
                $"未设置 {MySqlEmbeddingCacheTestDatabase.EnableEnvironmentVariableName}=true，跳过真实 MySQL 并发烟测。";
            return;
        }

        var baseConnection = Environment.GetEnvironmentVariable(
            MySqlEmbeddingCacheTestDatabase.BaseConnectionEnvironmentVariableName)?.Trim();
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            Skip =
                $"未设置 {MySqlEmbeddingCacheTestDatabase.BaseConnectionEnvironmentVariableName}，跳过真实 MySQL 并发烟测。";
        }
    }
}

internal sealed class MySqlEmbeddingCacheTestDatabase : IAsyncDisposable
{
    public const string EnableEnvironmentVariableName = "ACCEPTANCE_SPEC_ENABLE_MYSQL_MIGRATION_SMOKE_TESTS";
    public const string BaseConnectionEnvironmentVariableName = "ACCEPTANCE_SPEC_MYSQL_MIGRATION_BASE_CONNECTION";

    private readonly string _adminConnectionString;
    private bool _disposed;

    private MySqlEmbeddingCacheTestDatabase(
        string adminConnectionString,
        string connectionString,
        string databaseName)
    {
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
        DatabaseName = databaseName;
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    public static async Task<MySqlEmbeddingCacheTestDatabase> CreateAsync()
    {
        var baseConnectionString =
            Environment.GetEnvironmentVariable(BaseConnectionEnvironmentVariableName)?.Trim();
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                $"未设置 {BaseConnectionEnvironmentVariableName}，无法创建真实 MySQL 测试库。");
        }

        var databaseName =
            $"acceptance_spec_test_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..45];
        var adminBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "mysql",
            Pooling = false
        };
        var databaseBuilder = new MySqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };

        await using var adminConnection = new MySqlConnection(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (var command = adminConnection.CreateCommand())
        {
            command.CommandText =
                $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await command.ExecuteNonQueryAsync();
        }

        return new MySqlEmbeddingCacheTestDatabase(
            adminBuilder.ConnectionString,
            databaseBuilder.ConnectionString,
            databaseName);
    }

    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(CreateDbContextOptions());
    }

    public DbContextOptions<AppDbContext> CreateDbContextOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString))
            .Options;
    }

    public async Task MigrateAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await using var adminConnection = new MySqlConnection(_adminConnectionString);
        await adminConnection.OpenAsync();
        await using var command = adminConnection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{DatabaseName}`;";
        await command.ExecuteNonQueryAsync();
    }
}
