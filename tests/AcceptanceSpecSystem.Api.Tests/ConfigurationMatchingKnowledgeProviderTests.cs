using System.Text.Json;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class ConfigurationMatchingKnowledgeProviderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MatchingKnowledgeConfigRepository _repository;

    public ConfigurationMatchingKnowledgeProviderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new MatchingKnowledgeConfigRepository(_context);
    }

    [Fact]
    public async Task GetKnowledgeAsync_WhenDatabaseConfigExists_ShouldReturnDatabaseConfigOnly()
    {
        await _repository.SaveConfigAsync(new MatchingKnowledgeConfig
        {
            EntityAliasesJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["Panasonic品牌"] = "松下",
                ["默认品牌"] = "默认标准品牌"
            }),
            UnitAliasesJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["公分"] = "cm",
                ["默认单位别名"] = "mm"
            }),
            UnitFactorsJson = JsonSerializer.Serialize(new Dictionary<string, decimal>
            {
                ["cm"] = 10m,
                ["mm"] = 1m
            }),
            FieldAliasesJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["宽尺寸"] = "宽度",
                ["默认字段别名"] = "宽度"
            }),
            ConflictPairsJson = JsonSerializer.Serialize(new[]
            {
                new ConflictPairOption
                {
                    Left = "输入",
                    Right = "输出"
                },
                new ConflictPairOption
                {
                    Left = "正转",
                    Right = "反转"
                }
            })
        });
        await _context.SaveChangesAsync();

        var provider = new ConfigurationMatchingKnowledgeProvider(_repository);

        var knowledge = await provider.GetKnowledgeAsync();

        knowledge.EntityAliases["Panasonic品牌"].Should().Be("松下");
        knowledge.EntityAliases["默认品牌"].Should().Be("默认标准品牌");
        knowledge.UnitAliases["公分"].Should().Be("cm");
        knowledge.UnitAliases["默认单位别名"].Should().Be("mm");
        knowledge.UnitFactors["cm"].Should().Be(10m);
        knowledge.UnitFactors["mm"].Should().Be(1m);
        knowledge.FieldAliases["宽尺寸"].Should().Be("宽度");
        knowledge.FieldAliases["默认字段别名"].Should().Be("宽度");
        knowledge.ConflictPairs.Should().Contain(pair => pair.Left == "输入" && pair.Right == "输出");
        knowledge.ConflictPairs.Should().Contain(pair => pair.Left == "正转" && pair.Right == "反转");
    }

    [Fact]
    public async Task GetKnowledgeAsync_WhenDatabaseConfigMissing_ShouldReturnEmptyKnowledge()
    {
        var provider = new ConfigurationMatchingKnowledgeProvider(_repository);

        var knowledge = await provider.GetKnowledgeAsync();

        knowledge.EntityAliases.Should().BeEmpty();
        knowledge.UnitAliases.Should().BeEmpty();
        knowledge.UnitFactors.Should().BeEmpty();
        knowledge.FieldAliases.Should().BeEmpty();
        knowledge.ConflictPairs.Should().BeEmpty();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
