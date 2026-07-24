using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class AcceptanceSpecIndexModelTests : TestBase
{
    [Fact]
    public void AcceptanceSpecRepository_ShouldUseStableImportedAtAndIdOrdering()
    {
        var repositoryRoot = TestPathHelper.GetRepositoryRoot();
        var repositorySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "AcceptanceSpecSystem.Data",
            "Repositories",
            "AcceptanceSpecRepository.cs"));

        repositorySource.Should().MatchRegex(
            @"OrderByDescending\(s => s\.ImportedAt\)\s*\.ThenByDescending\(s => s\.Id\)");
    }

    [Fact]
    public void AcceptanceSpec_ShouldUseGroupPagingCompositeIndex()
    {
        var entityType = Context.Model.FindEntityType(typeof(AcceptanceSpec));

        entityType.Should().NotBeNull();
        var indexPropertyNames = entityType!.GetIndexes()
            .Select(index => index.Properties.Select(property => property.Name).ToArray())
            .ToArray();

        indexPropertyNames.Should().ContainEquivalentOf(
            new[]
            {
                nameof(AcceptanceSpec.CustomerId),
                nameof(AcceptanceSpec.ProcessId),
                nameof(AcceptanceSpec.MachineModelId),
                nameof(AcceptanceSpec.ImportedAt),
                nameof(AcceptanceSpec.Id)
            });
        indexPropertyNames.Should().NotContainEquivalentOf(
            new[]
            {
                nameof(AcceptanceSpec.CustomerId),
                nameof(AcceptanceSpec.ProcessId),
                nameof(AcceptanceSpec.MachineModelId)
            });
    }
}
