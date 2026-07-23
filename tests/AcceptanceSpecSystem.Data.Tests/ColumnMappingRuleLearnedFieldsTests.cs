using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 列映射规则学习字段测试。
/// </summary>
public class ColumnMappingRuleLearnedFieldsTests : TestBase
{
    [Fact]
    public async Task SaveAndQuery_ShouldRoundTripSourceAndCustomerId()
    {
        Context.ColumnMappingRules.Add(new ColumnMappingRule
        {
            TargetField = ColumnMappingTargetField.Project,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = "管控要点",
            Priority = 100,
            Source = ColumnMappingRuleSource.Learned,
            CustomerId = 7,
            Enabled = true
        });
        await Context.SaveChangesAsync();

        var found = Context.ColumnMappingRules.Single(rule => rule.Pattern == "管控要点");

        found.Source.Should().Be(ColumnMappingRuleSource.Learned);
        found.CustomerId.Should().Be(7);
    }

    [Fact]
    public async Task GetEffectiveForCustomerAsync_ShouldReturnCustomerRulesBeforeGlobalRules()
    {
        var repository = new ColumnMappingRuleRepository(Context);
        Context.ColumnMappingRules.AddRange(
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "全局项目",
                Priority = 100,
                Source = ColumnMappingRuleSource.Manual,
                CustomerId = null,
                Enabled = true
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "客户项目",
                Priority = 1,
                Source = ColumnMappingRuleSource.Learned,
                CustomerId = 3,
                Enabled = true
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "其他客户项目",
                Priority = 1000,
                Source = ColumnMappingRuleSource.Learned,
                CustomerId = 4,
                Enabled = true
            });
        await Context.SaveChangesAsync();

        var result = await repository.GetEffectiveForCustomerAsync(3);

        result.Select(rule => rule.Pattern).Should().Equal("客户项目", "全局项目");
    }
}
