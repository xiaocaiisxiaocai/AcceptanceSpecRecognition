using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Tests;

public sealed class WordFileReferenceGuardTests
{
    [Theory]
    [InlineData("acceptance")]
    [InlineData("matching-add")]
    [InlineData("matching-update")]
    public async Task 旧请求已读取活动文件_删除标记提交后恢复写引用必须失败(string scenario)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"word-file-reference-race-{Guid.NewGuid():N}")
            .Options;

        int targetId;
        int otherId;
        int customerId;
        int? existingTaskId = null;
        await using (var seed = new AppDbContext(options))
        {
            var customer = new Customer { Name = $"竞态客户-{Guid.NewGuid():N}" };
            var target = CreateFile("target");
            var other = CreateFile("other");
            seed.AddRange(customer, target, other);
            await seed.SaveChangesAsync();
            targetId = target.Id;
            otherId = other.Id;
            customerId = customer.Id;

            if (scenario == "matching-update")
            {
                var task = new MatchingFillTask
                {
                    TaskId = Guid.NewGuid().ToString("N"),
                    SourceFileId = otherId,
                    PayloadJson = "{}"
                };
                seed.MatchingFillTasks.Add(task);
                await seed.SaveChangesAsync();
                existingTaskId = task.Id;
            }
        }

        await using var oldRequest = new AppDbContext(options);
        (await oldRequest.WordFiles.SingleAsync(file => file.Id == targetId))
            .DeletionStatus.Should().Be(WordFileDeletionStatus.Active);

        await using (var deleteRequest = new AppDbContext(options))
        {
            var target = await deleteRequest.WordFiles.SingleAsync(file => file.Id == targetId);
            target.DeletionStatus = WordFileDeletionStatus.PendingDeletion;
            target.DeletionRequestedAt = DateTime.UtcNow;
            await deleteRequest.SaveChangesAsync();
        }

        if (scenario == "acceptance")
        {
            oldRequest.AcceptanceSpecs.Add(new AcceptanceSpec
            {
                CustomerId = customerId,
                Project = "竞态",
                Specification = "不得落库",
                WordFileId = targetId
            });
        }
        else if (scenario == "matching-add")
        {
            oldRequest.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = Guid.NewGuid().ToString("N"),
                SourceFileId = targetId,
                PayloadJson = "{}"
            });
        }
        else
        {
            var task = await oldRequest.MatchingFillTasks.SingleAsync(item => item.Id == existingTaskId);
            task.SourceFileId = targetId;
        }

        await oldRequest.Invoking(context => context.SaveChangesAsync())
            .Should().ThrowAsync<WordFileReferenceUnavailableException>();
    }

    private static WordFile CreateFile(string prefix) => new()
    {
        FileName = $"{prefix}.docx",
        FileHash = Guid.NewGuid().ToString("N"),
        FilePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx"
    };
}
