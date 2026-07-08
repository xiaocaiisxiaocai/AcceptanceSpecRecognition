using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// 列映射规则 Repository 测试。
/// </summary>
public class ColumnMappingRuleRepositoryTests : TestBase
{
    [Fact]
    public async Task GetEnabledOrderedAsync_ShouldFilterDisabledRulesAndOrderByTargetPriorityAndId()
    {
        // Arrange
        var repository = new ColumnMappingRuleRepository(Context);
        Context.ColumnMappingRules.AddRange(
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "规格低优先级",
                Priority = 10
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "项目",
                Priority = 1
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Specification,
                MatchMode = ColumnMappingMatchMode.Regex,
                Pattern = "规格高优先级",
                Priority = 20
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Acceptance,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "已禁用",
                Priority = 100,
                Enabled = false
            });
        await Context.SaveChangesAsync();

        // Act
        var result = await repository.GetEnabledOrderedAsync();

        // Assert
        result.Select(rule => rule.Pattern)
            .Should()
            .Equal("项目", "规格高优先级", "规格低优先级");
    }

    [Fact]
    public async Task GetEnabledOrderedAsync_WhenDuplicateGlobalRulesExist_ShouldReturnOneRulePerTargetPattern()
    {
        var repository = new ColumnMappingRuleRepository(Context);
        Context.ColumnMappingRules.AddRange(
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "项目",
                Priority = 0,
                Source = ColumnMappingRuleSource.Builtin,
                CustomerId = null,
                Enabled = true
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "项目",
                Priority = 0,
                Source = ColumnMappingRuleSource.Manual,
                CustomerId = null,
                Enabled = true
            });
        await Context.SaveChangesAsync();

        var result = await repository.GetEnabledOrderedAsync();

        result.Where(rule => rule.TargetField == ColumnMappingTargetField.Project && rule.Pattern == "项目")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task GetEffectiveForCustomerAsync_WhenCustomerRuleDuplicatesGlobalRule_ShouldPreferCustomerRule()
    {
        var repository = new ColumnMappingRuleRepository(Context);
        Context.ColumnMappingRules.AddRange(
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Contains,
                Pattern = "项目",
                Priority = 0,
                Source = ColumnMappingRuleSource.Builtin,
                CustomerId = null,
                Enabled = true
            },
            new ColumnMappingRule
            {
                TargetField = ColumnMappingTargetField.Project,
                MatchMode = ColumnMappingMatchMode.Equals,
                Pattern = "项目",
                Priority = 100,
                Source = ColumnMappingRuleSource.Learned,
                CustomerId = 3,
                Enabled = true
            });
        await Context.SaveChangesAsync();

        var result = await repository.GetEffectiveForCustomerAsync(3);

        var projectRules = result.Where(rule => rule.TargetField == ColumnMappingTargetField.Project && rule.Pattern == "项目").ToList();
        projectRules.Should().ContainSingle();
        projectRules[0].CustomerId.Should().Be(3);
        projectRules[0].Source.Should().Be(ColumnMappingRuleSource.Learned);
    }
}
