using AcceptanceSpecSystem.Data;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class OperationalDepartmentMigrationTests
{
    private const string MigrationId =
        "20260731143000_MoveOperationalDataToElectricalControlDepartment";

    [Fact]
    public void Migration_ShouldRequireControlledBackupVerifiedExecution()
    {
        DatabaseInitializer.ClassifyMigration(MigrationId)
            .Should().Be(DatabaseMigrationRisk.Destructive);
    }
}
