using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
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
        services.AddOptions<MatchingKnowledgeOptions>()
            .Configure(options =>
            {
                options.EntityAliases["默认品牌"] = "默认标准品牌";
                options.UnitAliases["默认单位别名"] = "mm";
                options.UnitFactors["mm"] = 1m;
                options.FieldAliases["默认字段别名"] = "宽度";
                options.ConflictPairs.Add(new ConflictPairOption
                {
                    Left = "输入",
                    Right = "输出"
                });
            });

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
        DeserializeDictionary(entity.EntityAliasesJson).Should().Contain("默认品牌", "默认标准品牌");
        DeserializeDictionary(entity.UnitAliasesJson).Should().Contain("默认单位别名", "mm");
        DeserializeDecimalDictionary(entity.UnitFactorsJson).Should().Contain("mm", 1m);
        DeserializeDictionary(entity.FieldAliasesJson).Should().Contain("默认字段别名", "宽度");
        DeserializeConflictPairs(entity.ConflictPairsJson).Should().ContainSingle(pair => pair.Left == "输入" && pair.Right == "输出");
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
