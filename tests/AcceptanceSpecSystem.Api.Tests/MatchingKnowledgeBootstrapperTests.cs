using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingKnowledgeBootstrapperTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ServiceProvider _serviceProvider;
    private readonly SqliteConnection _connection;

    public MatchingKnowledgeBootstrapperTests()
    {
        var services = new ServiceCollection();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(_connection));
        services.AddScoped<IMatchingKnowledgeConfigRepository, MatchingKnowledgeConfigRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<AppDbContext>();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenConfigMissing_ShouldSeedEmptyCustomKnowledge()
    {
        using var scope = _serviceProvider.CreateScope();
        var bootstrapper = CreateBootstrapper(scope.ServiceProvider);

        await bootstrapper.EnsureInitializedAsync();

        var entity = await _context.Set<MatchingKnowledgeConfig>().SingleAsync();
        DeserializeDictionary(entity.EntityAliasesJson).Should().BeEmpty();
        DeserializeDictionary(entity.UnitAliasesJson).Should().BeEmpty();
        DeserializeDecimalDictionary(entity.UnitFactorsJson).Should().BeEmpty();
        DeserializeDictionary(entity.FieldAliasesJson).Should().BeEmpty();
        DeserializeConflictPairs(entity.ConflictPairsJson).Should().BeEmpty();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    private MatchingKnowledgeBootstrapper CreateBootstrapper(IServiceProvider serviceProvider)
    {
        return new MatchingKnowledgeBootstrapper(
            serviceProvider.GetRequiredService<IUnitOfWork>());
    }
    private static Dictionary<string, string> DeserializeDictionary(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? [];
    }

    private static Dictionary<string, decimal> DeserializeDecimalDictionary(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)
            ?? [];
    }

    private static List<ConflictPairDto> DeserializeConflictPairs(string json)
    {
        return JsonSerializer.Deserialize<List<ConflictPairDto>>(json)
            ?? [];
    }
}
