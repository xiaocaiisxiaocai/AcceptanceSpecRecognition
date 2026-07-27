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
        var service = CreateService(unitOfWork, new FixedEmbeddingService(GeneratedVector));

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
        var firstService = CreateService(firstUnitOfWork, embeddingService);
        var secondService = CreateService(secondUnitOfWork, embeddingService);

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

    private static SpecEmbeddingCacheService CreateService(
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService) =>
        new(
            unitOfWork,
            embeddingService,
            new FixedAiServiceSelector(),
            NullLogger<SpecEmbeddingCacheService>.Instance);

    private static async Task<AcceptanceSpec> SeedAcceptanceSpecAsync(AppDbContext context)
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
            Project = "缓存并发项目",
            Specification = "同一规格应复用唯一向量",
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
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString))
            .Options;
        return new AppDbContext(options);
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
