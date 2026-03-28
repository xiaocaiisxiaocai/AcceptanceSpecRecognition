using System.Text.Json;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        services.AddSingleton<IOptions<MatchingKnowledgeOptions>>(Microsoft.Extensions.Options.Options.Create(CreateDefaultOptions()));

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<AppDbContext>();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenConfigMissing_ShouldSeedDefaultKnowledge()
    {
        using var scope = _serviceProvider.CreateScope();
        var bootstrapper = CreateBootstrapper(scope.ServiceProvider);

        await bootstrapper.EnsureInitializedAsync();

        var entity = await _context.Set<MatchingKnowledgeConfig>().SingleAsync();
        DeserializeDictionary(entity.EntityAliasesJson)["panasonic"].Should().Be("松下");
        DeserializeDictionary(entity.UnitAliasesJson)["厘米"].Should().Be("cm");
        DeserializeDecimalDictionary(entity.UnitFactorsJson)["cm"].Should().Be(10m);
        DeserializeDictionary(entity.FieldAliasesJson)["width"].Should().Be("宽度");
        DeserializeConflictPairs(entity.ConflictPairsJson).Should().Contain(pair => pair.Left == "输入" && pair.Right == "输出");
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
            serviceProvider.GetRequiredService<IUnitOfWork>(),
            serviceProvider.GetRequiredService<IOptions<MatchingKnowledgeOptions>>());
    }

    private static MatchingKnowledgeOptions CreateDefaultOptions()
    {
        return new MatchingKnowledgeOptions
        {
            EntityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["panasonic"] = "松下",
                ["松下"] = "松下"
            },
            UnitAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cm"] = "cm",
                ["厘米"] = "cm"
            },
            UnitFactors = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["cm"] = 10m
            },
            FieldAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["width"] = "宽度",
                ["宽度"] = "宽度"
            },
            ConflictPairs =
            [
                new ConflictPairOption
                {
                    Left = "输入",
                    Right = "输出"
                }
            ]
        };
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

    private static List<ConflictPairOption> DeserializeConflictPairs(string json)
    {
        return JsonSerializer.Deserialize<List<ConflictPairOption>>(json)
            ?? [];
    }
}
