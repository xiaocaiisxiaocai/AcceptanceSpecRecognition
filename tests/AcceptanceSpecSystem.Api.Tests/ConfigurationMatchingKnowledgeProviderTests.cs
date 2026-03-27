using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class ConfigurationMatchingKnowledgeProviderTests
{
    [Fact]
    public async Task GetKnowledgeAsync_ShouldMapConfiguredAliasesFactorsAndConflictPairs()
    {
        var provider = new ConfigurationMatchingKnowledgeProvider(
            Microsoft.Extensions.Options.Options.Create(new MatchingKnowledgeOptions
            {
                EntityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["panasonic"] = "松下",
                    ["松下"] = "松下"
                },
                UnitAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["厘米"] = "cm",
                    ["mm"] = "mm"
                },
                UnitFactors = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cm"] = 10m,
                    ["mm"] = 1m
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
                        Left = "正转",
                        Right = "反转"
                    }
                ]
            }));

        var knowledge = await provider.GetKnowledgeAsync();

        knowledge.EntityAliases["panasonic"].Should().Be("松下");
        knowledge.UnitAliases["厘米"].Should().Be("cm");
        knowledge.UnitFactors["cm"].Should().Be(10m);
        knowledge.FieldAliases["width"].Should().Be("宽度");
        knowledge.ConflictPairs.Should().Contain(pair => pair.Left == "正转" && pair.Right == "反转");
    }
}
