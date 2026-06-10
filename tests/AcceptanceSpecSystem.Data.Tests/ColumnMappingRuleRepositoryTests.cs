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
}
