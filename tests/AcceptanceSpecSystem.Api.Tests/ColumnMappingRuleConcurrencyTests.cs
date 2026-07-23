using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ColumnMappingRuleConcurrencyTests
{
    [Fact]
    public async Task StartupRepair_ShouldCollapseLegacySqliteUnicodeCaseDuplicates()
    {
        await using var database = await ConcurrentRuleDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            await seedContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO ColumnMappingRules " +
                "(TargetField, MatchMode, Pattern, ScopeKey, NormalizedPattern, GlobalNormalizedPatternKey, Priority, Enabled, Source, CustomerId, CreatedAt) VALUES " +
                "(1, 2, 'α', 'global', 'α', 'α', 100, 1, 3, NULL, CURRENT_TIMESTAMP), " +
                "(2, 2, 'Α', 'global', 'Α', 'Α', 10, 1, 2, NULL, CURRENT_TIMESTAMP);");
        }

        await using var context = database.CreateContext();
        using var unitOfWork = new BarrierColumnMappingRuleUnitOfWork(
            context,
            new AsyncArrivalGate(participantCount: 1));
        var service = new ColumnMappingRuleAppService(unitOfWork);

        await service.RestoreDefaultsAsync(targetField: null);

        await using var verificationContext = database.CreateContext();
        var unicodeRows = await verificationContext.ColumnMappingRules
            .Where(rule => rule.Pattern == "α" || rule.Pattern == "Α")
            .ToListAsync();
        unicodeRows.Should().ContainSingle();
        unicodeRows[0].NormalizedPattern.Should().Be("Α");
        unicodeRows[0].GlobalNormalizedPatternKey.Should().Be("Α");
    }

    [Fact]
    public async Task ManagementCreate_WhenTwoWritersRace_ShouldReturnPersistedWinnerToBoth()
    {
        await using var database = await ConcurrentRuleDatabase.CreateAsync();
        var gate = new AsyncArrivalGate(participantCount: 2);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var firstUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(firstContext, gate);
        using var secondUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(secondContext, gate);
        var firstService = new ColumnMappingRuleAppService(firstUnitOfWork);
        var secondService = new ColumnMappingRuleAppService(secondUnitOfWork);
        var request = new CreateColumnMappingRuleRequest
        {
            TargetField = ColumnMappingTargetField.Project,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = "并发管理创建词",
            Priority = 10,
            Enabled = true,
            Source = ColumnMappingRuleSource.Manual
        };

        var results = await Task.WhenAll(
            firstService.CreateAsync(request),
            secondService.CreateAsync(request));

        results.Select(result => result.Id).Distinct().Should().ContainSingle();
        await using var verificationContext = database.CreateContext();
        var rows = await verificationContext.ColumnMappingRules
            .Where(rule =>
                rule.ScopeKey == ColumnMappingRule.GlobalScopeKey &&
                rule.TargetField == ColumnMappingTargetField.Project &&
                rule.NormalizedPattern == ColumnMappingRule.NormalizePattern(request.Pattern))
            .ToListAsync();
        rows.Should().ContainSingle();
    }

    [Fact]
    public async Task SmartLearning_WhenTwoWritersRaceForSameCustomerRule_ShouldKeepOneRule()
    {
        await using var database = await ConcurrentRuleDatabase.CreateAsync();
        var gate = new AsyncArrivalGate(participantCount: 2);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var firstUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(firstContext, gate);
        using var secondUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(secondContext, gate);
        var options = Microsoft.Extensions.Options.Options.Create(new SmartConfigurationOptions
        {
            GlobalRulePromotionCustomerThreshold = 2
        });
        var firstService = new SmartConfigurationLearningService(firstUnitOfWork, options);
        var secondService = new SmartConfigurationLearningService(secondUnitOfWork, options);
        var columns = new[]
        {
            new SmartConfigurationLearnedColumn
            {
                Header = "并发学习表头",
                TargetField = ColumnMappingTargetField.Specification
            }
        };

        var results = await Task.WhenAll(
            firstService.ApplyLearningAsync(9001, null, null, null, columns, CancellationToken.None),
            secondService.ApplyLearningAsync(9001, null, null, null, columns, CancellationToken.None));

        results.Sum(result => result.LearnedRuleCount).Should().Be(1);
        await using var verificationContext = database.CreateContext();
        var rows = await verificationContext.ColumnMappingRules
            .Where(rule =>
                rule.ScopeKey == ColumnMappingRule.BuildScopeKey(9001) &&
                rule.TargetField == ColumnMappingTargetField.Specification &&
                rule.NormalizedPattern == ColumnMappingRule.NormalizePattern("并发学习表头"))
            .ToListAsync();
        rows.Should().ContainSingle();
    }

    [Fact]
    public async Task SmartLearning_WhenDifferentTargetsRaceForSameGlobalPattern_ShouldPromoteOnlyOneTarget()
    {
        await using var database = await ConcurrentRuleDatabase.CreateAsync();
        const string header = "全局冲突学习表头";
        await using (var seedContext = database.CreateContext())
        {
            seedContext.ColumnMappingRules.AddRange(
                CreateLearnedCustomerRule(9101, header, ColumnMappingTargetField.Project),
                CreateLearnedCustomerRule(9102, header, ColumnMappingTargetField.Specification));
            await seedContext.SaveChangesAsync();
        }

        var gate = new AsyncArrivalGate(participantCount: 2);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var firstUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(firstContext, gate, barrierSaveOrdinal: 2);
        using var secondUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(secondContext, gate, barrierSaveOrdinal: 2);
        var options = Microsoft.Extensions.Options.Options.Create(new SmartConfigurationOptions
        {
            GlobalRulePromotionCustomerThreshold = 1
        });
        var firstService = new SmartConfigurationLearningService(firstUnitOfWork, options);
        var secondService = new SmartConfigurationLearningService(secondUnitOfWork, options);

        var results = await Task.WhenAll(
            firstService.ApplyLearningAsync(9101, null, null, null,
                [new SmartConfigurationLearnedColumn { Header = header, TargetField = ColumnMappingTargetField.Project }],
                CancellationToken.None),
            secondService.ApplyLearningAsync(9102, null, null, null,
                [new SmartConfigurationLearnedColumn { Header = header, TargetField = ColumnMappingTargetField.Specification }],
                CancellationToken.None));

        results.Sum(result => result.PromotedGlobalRuleCount).Should().Be(1);
        await using var verificationContext = database.CreateContext();
        var globalRows = await verificationContext.ColumnMappingRules
            .Where(rule =>
                rule.CustomerId == null &&
                rule.GlobalNormalizedPatternKey == ColumnMappingRule.NormalizePattern(header))
            .ToListAsync();
        globalRows.Should().ContainSingle();
    }

    [Fact]
    public async Task ManagementUpdate_WhenTwoWritersRaceToSameIdentity_ShouldReturnStableConflict()
    {
        await using var database = await ConcurrentRuleDatabase.CreateAsync();
        int firstId;
        int secondId;
        await using (var seedContext = database.CreateContext())
        {
            var first = CreateLearnedCustomerRule(9201, "更新前-A", ColumnMappingTargetField.Project);
            var second = CreateLearnedCustomerRule(9201, "更新前-B", ColumnMappingTargetField.Project);
            seedContext.ColumnMappingRules.AddRange(first, second);
            await seedContext.SaveChangesAsync();
            firstId = first.Id;
            secondId = second.Id;
        }

        var gate = new AsyncArrivalGate(participantCount: 2);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        using var firstUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(firstContext, gate);
        using var secondUnitOfWork = new BarrierColumnMappingRuleUnitOfWork(secondContext, gate);
        var firstService = new ColumnMappingRuleAppService(firstUnitOfWork);
        var secondService = new ColumnMappingRuleAppService(secondUnitOfWork);
        var request = new UpdateColumnMappingRuleRequest
        {
            CustomerId = 9201,
            TargetField = ColumnMappingTargetField.Project,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = "并发更新目标",
            Priority = 10,
            Enabled = true,
            Source = ColumnMappingRuleSource.Manual
        };

        static async Task<ApplicationServiceException?> ExecuteAsync(
            ColumnMappingRuleAppService service,
            int id,
            UpdateColumnMappingRuleRequest request)
        {
            try
            {
                await service.UpdateAsync(id, request);
                return null;
            }
            catch (ApplicationServiceException exception)
            {
                return exception;
            }
        }

        var outcomes = await Task.WhenAll(
            ExecuteAsync(firstService, firstId, request),
            ExecuteAsync(secondService, secondId, request));

        outcomes.Count(exception => exception is null).Should().Be(1);
        outcomes.Single(exception => exception is not null)!.Code.Should().Be(409);
    }

    private static ColumnMappingRule CreateLearnedCustomerRule(
        int customerId,
        string pattern,
        ColumnMappingTargetField targetField) => new()
    {
        CustomerId = customerId,
        TargetField = targetField,
        MatchMode = ColumnMappingMatchMode.Equals,
        Pattern = pattern,
        Priority = 100,
        Enabled = true,
        Source = ColumnMappingRuleSource.Learned,
        CreatedAt = DateTime.UtcNow
    };
}

internal sealed class ConcurrentRuleDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _anchor;
    private readonly string _connectionString;

    private ConcurrentRuleDatabase(SqliteConnection anchor, string connectionString)
    {
        _anchor = anchor;
        _connectionString = connectionString;
    }

    public static async Task<ConcurrentRuleDatabase> CreateAsync()
    {
        var databaseName = $"ColumnMappingRuleConcurrency-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Default Timeout=30";
        var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var database = new ConcurrentRuleDatabase(anchor, connectionString);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new AppDbContext(options);
    }

    public ValueTask DisposeAsync() => _anchor.DisposeAsync();
}

internal sealed class AsyncArrivalGate
{
    private readonly int _participantCount;
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public AsyncArrivalGate(int participantCount) => _participantCount = participantCount;

    public async Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrivals) == _participantCount)
            _allArrived.TrySetResult();

        await _allArrived.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class BarrierColumnMappingRuleUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly AsyncArrivalGate _gate;
    private readonly int _barrierSaveOrdinal;
    private int _saveCount;

    public BarrierColumnMappingRuleUnitOfWork(
        AppDbContext context,
        AsyncArrivalGate gate,
        int barrierSaveOrdinal = 1)
    {
        _context = context;
        _gate = gate;
        _barrierSaveOrdinal = barrierSaveOrdinal;
        ColumnMappingRules = new ColumnMappingRuleRepository(context);
    }

    public IColumnMappingRuleRepository ColumnMappingRules { get; }
    public ICustomerRepository Customers => throw new NotSupportedException();
    public IProcessRepository Processes => throw new NotSupportedException();
    public IMachineModelRepository MachineModels => throw new NotSupportedException();
    public IAcceptanceSpecRepository AcceptanceSpecs => throw new NotSupportedException();
    public IEmbeddingCacheRepository EmbeddingCaches => throw new NotSupportedException();
    public IWordFileRepository WordFiles => throw new NotSupportedException();
    public IAiServiceConfigRepository AiServiceConfigs => throw new NotSupportedException();
    public IPromptTemplateRepository PromptTemplates => throw new NotSupportedException();
    public ISmartStructureRoutingRuleRepository SmartStructureRoutingRules => throw new NotSupportedException();
    public IDocumentTemplateRepository DocumentTemplates => throw new NotSupportedException();
    public ISystemUserRepository SystemUsers => throw new NotSupportedException();
    public IAuditLogRepository AuditLogs => throw new NotSupportedException();
    public IMatchingFillTaskRepository MatchingFillTasks => throw new NotSupportedException();
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => throw new NotSupportedException();

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _saveCount) == _barrierSaveOrdinal)
            await _gate.ArriveAsync(cancellationToken);

        return await _context.SaveChangesAsync(cancellationToken);
    }

    public int SaveChanges() => _context.SaveChanges();
    public Task BeginTransactionAsync() => throw new NotSupportedException();
    public Task CommitTransactionAsync() => throw new NotSupportedException();
    public Task RollbackTransactionAsync() => throw new NotSupportedException();
    public void Dispose() { }
}
