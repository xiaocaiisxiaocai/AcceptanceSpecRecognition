using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class EnumDefaultSentinelTests : TestBase
{
    [Fact]
    public void RuleSourceDefaults_ShouldUseInvalidZeroAsExplicitSentinel()
    {
        var columnSource = Context.Model.FindEntityType(typeof(ColumnMappingRule))!
            .FindProperty(nameof(ColumnMappingRule.Source))!;
        var routingSource = Context.Model.FindEntityType(typeof(SmartStructureRoutingRule))!
            .FindProperty(nameof(SmartStructureRoutingRule.Source))!;

        columnSource.GetDefaultValue().Should().Be(ColumnMappingRuleSource.Manual);
        columnSource.Sentinel.Should().Be((ColumnMappingRuleSource)0);
        routingSource.GetDefaultValue().Should().Be(SmartStructureRoutingRuleSource.Manual);
        routingSource.Sentinel.Should().Be((SmartStructureRoutingRuleSource)0);
    }
}
