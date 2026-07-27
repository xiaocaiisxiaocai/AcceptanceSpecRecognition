using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// MatchingFillTaskRepository 单元测试
/// </summary>
public class MatchingFillTaskRepositoryTests : TestBase
{
    private readonly MatchingFillTaskRepository _repository;

    public MatchingFillTaskRepositoryTests()
    {
        _repository = new MatchingFillTaskRepository(Context);
    }

    [Fact]
    public async Task GetByTaskIdAsync_ExistingTask_ShouldReturnTask()
    {
        // Arrange
        await SeedSourceFileAsync(1);
        var task = new MatchingFillTask
        {
            TaskId = "task-001",
            SourceFileId = 1,
            PayloadJson = "{}",
            CreatedAt = DateTime.UtcNow
        };
        Context.MatchingFillTasks.Add(task);
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByTaskIdAsync("task-001");

        // Assert
        result.Should().NotBeNull();
        result!.TaskId.Should().Be("task-001");
        result.SourceFileId.Should().Be(1);
    }

    [Fact]
    public async Task GetByTaskIdAsync_NonExistingTask_ShouldReturnNull()
    {
        // Act
        var result = await _repository.GetByTaskIdAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact(Skip = "ExecuteDeleteAsync 不被 InMemory provider 支持，需要 SQLite 或真实数据库")]
    public async Task DeleteBeforeAsync_ShouldDeleteOldTasks()
    {
        // Arrange
        var now = DateTime.UtcNow;
        Context.MatchingFillTasks.AddRange(
            new MatchingFillTask
            {
                TaskId = "old-task-1",
                SourceFileId = 1,
                PayloadJson = "{}",
                CreatedAt = now.AddDays(-10)
            },
            new MatchingFillTask
            {
                TaskId = "old-task-2",
                SourceFileId = 1,
                PayloadJson = "{}",
                CreatedAt = now.AddDays(-5)
            },
            new MatchingFillTask
            {
                TaskId = "recent-task",
                SourceFileId = 1,
                PayloadJson = "{}",
                CreatedAt = now.AddDays(-1)
            });
        await Context.SaveChangesAsync();

        // Act
        var deletedCount = await _repository.DeleteBeforeAsync(now.AddDays(-3));

        // Assert
        deletedCount.Should().Be(2);
        var remaining = await _repository.GetAllAsync();
        remaining.Should().HaveCount(1);
        remaining[0].TaskId.Should().Be("recent-task");
    }

    [Fact(Skip = "ExecuteDeleteAsync 不被 InMemory provider 支持，需要 SQLite 或真实数据库")]
    public async Task DeleteBeforeAsync_NoOldTasks_ShouldReturnZero()
    {
        // Arrange
        Context.MatchingFillTasks.Add(new MatchingFillTask
        {
            TaskId = "recent",
            SourceFileId = 1,
            PayloadJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        // Act
        var deletedCount = await _repository.DeleteBeforeAsync(DateTime.UtcNow.AddDays(-30));

        // Assert
        deletedCount.Should().Be(0);
    }

    [Fact]
    public async Task Add_ShouldPersistTask()
    {
        // Arrange
        await SeedSourceFileAsync(42);
        var task = new MatchingFillTask
        {
            TaskId = "new-task",
            SourceFileId = 42,
            CreatedByUserId = 1,
            CompanyId = 1,
            PayloadJson = "{\"tables\":[]}",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _repository.AddAsync(task);
        await Context.SaveChangesAsync();

        // Assert
        var saved = await _repository.GetByTaskIdAsync("new-task");
        saved.Should().NotBeNull();
        saved!.SourceFileId.Should().Be(42);
        saved.PayloadJson.Should().Contain("tables");
    }

    private async Task SeedSourceFileAsync(int id)
    {
        Context.WordFiles.Add(new WordFile
        {
            Id = id,
            FileName = $"source-{id}.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FilePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx"
        });
        await Context.SaveChangesAsync();
    }
}
