using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class Task8QualityContractTests
{
    [Fact]
    public void DocumentImport幂等快照_不得在事务内重复普通读取源文件()
    {
        ReadSource("src/AcceptanceSpecSystem.Application/Services/DocumentImportAppService.Idempotency.cs")
            .Should().NotContain("_unitOfWork.WordFiles.GetByIdAsync(sourceFileId)");
    }

    [Fact]
    public void WordFile引用锁_必须按文件编号固定顺序获取()
    {
        ReadSource("src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs")
            .Should().Contain(".OrderBy(id => id)");
    }

    [Fact]
    public void 删除清理器_不得预先领取整批租约()
    {
        var source = ReadSource(
            "src/AcceptanceSpecSystem.Application/Services/WordFileDeletionCleanupAppService.cs");

        source.Should().NotContain("var claimed = new List<");
        source.Should().Contain("await ProcessClaimedItemAsync(id, token, cancellationToken)");
    }

    private static string ReadSource(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
            current = current.Parent;
        current.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(
            current!.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
