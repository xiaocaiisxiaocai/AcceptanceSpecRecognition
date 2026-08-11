using System.Reflection;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecCleanupArchitectureTests
{
    [Fact]
    public void Controller_ShouldRemainAProtocolAdapterAroundApplicationPorts()
    {
        var dependencies = typeof(SpecCleanupController)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();

        dependencies.Should().BeEquivalentTo(
            [typeof(IAcceptanceSpecCleanupAppService), typeof(IAuthDataScopeService)],
            "清理控制器只应负责协议和数据范围适配，不应直接访问持久化实现");
        ReferenceEquals(
                typeof(AcceptanceSpecCleanupAppService).Assembly,
                typeof(IAcceptanceSpecCleanupAppService).Assembly)
            .Should().BeTrue("清理用例实现和端口应保留在 Application 边界内");
    }
}
