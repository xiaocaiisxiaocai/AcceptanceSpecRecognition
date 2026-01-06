using Xunit;
using Moq;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

public class AuditLoggerTests
{
    private readonly Mock<IJsonStorageService> _storageMock;
    private readonly Mock<IConfigManager> _configMock;
    private readonly AuditLogger _auditLogger;

    public AuditLoggerTests()
    {
        _storageMock = new Mock<IJsonStorageService>();
        _storageMock.Setup(s => s.ReadAsync<AuditLogStore>(It.IsAny<string>()))
            .ReturnsAsync(new AuditLogStore { Entries = new List<AuditLogEntry>() });
        _storageMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<AuditLogStore>()))
            .Returns(Task.CompletedTask);

        _configMock = new Mock<IConfigManager>();
        _configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            Version = "1.0",
            Batch = new BatchConfig { MaxAuditEntries = 10000 }
        });

        _auditLogger = new AuditLogger(_storageMock.Object, _configMock.Object);
    }

    [Fact]
    public async Task LogQueryAsync_CreatesLogEntry()
    {
        // Arrange
        var entry = new QueryLogEntry
        {
            QueryText = "DC24V 输入模块",
            ResultCount = 5,
            TopScore = 0.95f,
            Confidence = "High"
        };

        // Act
        await _auditLogger.LogQueryAsync(entry);

        // Assert
        _storageMock.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<AuditLogStore>()), Times.Once);
    }

    [Fact]
    public async Task LogUserActionAsync_CreatesLogEntry()
    {
        // Arrange
        var entry = new UserActionLogEntry
        {
            Action = "confirm_match",
            RecordId = "rec_123",
            Details = "用户确认匹配结果"
        };

        // Act
        await _auditLogger.LogUserActionAsync(entry);

        // Assert
        _storageMock.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<AuditLogStore>()), Times.Once);
    }

    [Fact]
    public async Task LogConfigChangeAsync_CreatesLogEntry()
    {
        // Arrange
        var entry = new ConfigChangeLogEntry
        {
            ConfigSection = "matching",
            Changes = "更新高置信度阈值为0.95"
        };

        // Act
        await _auditLogger.LogConfigChangeAsync(entry);

        // Assert
        _storageMock.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<AuditLogStore>()), Times.Once);
    }

    [Fact]
    public async Task QueryLogsAsync_FiltersbyActionType()
    {
        // Arrange
        var existingEntries = new List<AuditLogEntry>
        {
            new() { Id = "1", ActionType = "query", Timestamp = DateTime.UtcNow },
            new() { Id = "2", ActionType = "confirm_match", Timestamp = DateTime.UtcNow },
            new() { Id = "3", ActionType = "query", Timestamp = DateTime.UtcNow }
        };

        _storageMock.Setup(s => s.ReadAsync<AuditLogStore>(It.IsAny<string>()))
            .ReturnsAsync(new AuditLogStore { Entries = existingEntries });

        var logger = new AuditLogger(_storageMock.Object, _configMock.Object);

        // Act
        var result = await logger.QueryLogsAsync(new AuditLogFilter { ActionType = "query" });

        // Assert
        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Equal("query", e.ActionType));
    }

    [Fact]
    public async Task QueryLogsAsync_FiltersByTimeRange()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var existingEntries = new List<AuditLogEntry>
        {
            new() { Id = "1", ActionType = "query", Timestamp = now.AddDays(-5) },
            new() { Id = "2", ActionType = "query", Timestamp = now.AddDays(-2) },
            new() { Id = "3", ActionType = "query", Timestamp = now }
        };

        _storageMock.Setup(s => s.ReadAsync<AuditLogStore>(It.IsAny<string>()))
            .ReturnsAsync(new AuditLogStore { Entries = existingEntries });

        var logger = new AuditLogger(_storageMock.Object, _configMock.Object);

        // Act
        var result = await logger.QueryLogsAsync(new AuditLogFilter
        {
            StartTime = now.AddDays(-3),
            EndTime = now.AddDays(1)
        });

        // Assert
        Assert.Equal(2, result.Entries.Count);
    }

    [Fact]
    public async Task QueryLogsAsync_SupportsPagination()
    {
        // Arrange
        var entries = Enumerable.Range(1, 100)
            .Select(i => new AuditLogEntry
            {
                Id = i.ToString(),
                ActionType = "query",
                Timestamp = DateTime.UtcNow.AddMinutes(-i)
            })
            .ToList();

        _storageMock.Setup(s => s.ReadAsync<AuditLogStore>(It.IsAny<string>()))
            .ReturnsAsync(new AuditLogStore { Entries = entries });

        var logger = new AuditLogger(_storageMock.Object, _configMock.Object);

        // Act
        var result = await logger.QueryLogsAsync(new AuditLogFilter { Page = 2, PageSize = 20 });

        // Assert
        Assert.Equal(20, result.Entries.Count);
        Assert.Equal(100, result.TotalCount);
        Assert.Equal(2, result.Page);
    }
}
