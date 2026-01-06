using Xunit;
using Moq;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AcceptanceSpecRecognition.Api.Controllers;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

/// <summary>
/// API集成测试 - 测试各API端点和错误处理
/// Validates: Requirements 8.1
/// </summary>
public class ApiIntegrationTests
{
    /// <summary>
    /// 创建默认的 ConfigManager mock
    /// </summary>
    private static Mock<IConfigManager> CreateConfigMock()
    {
        var configMock = new Mock<IConfigManager>();
        configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            Version = "1.0",
            Matching = new MatchingConfig { MatchSuccessThreshold = 0.80f },
            Embedding = new EmbeddingConfig { ApiKey = "" },
            LLM = new LLMConfig { ApiKey = "" },
            Batch = new BatchConfig
            {
                MaxConcurrency = 5,
                TaskRetentionMinutes = 30,
                MaxBatchSize = 100,
                MaxTextLength = 2000,
                MaxAuditEntries = 10000,
                ApiMaxRetries = 3,
                ApiRetryBaseDelayMs = 1000
            }
        });
        return configMock;
    }

    /// <summary>
    /// 创建默认的 LLMService mock
    /// </summary>
    private static Mock<ILLMService> CreateLLMServiceMock()
    {
        var llmMock = new Mock<ILLMService>();
        llmMock.Setup(l => l.AnalyzeUnifiedAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new UnifiedAnalysisResult
            {
                HasConflict = false,
                IsEquivalent = false,
                ScoreAdjustmentFactor = 1.0f,
                Confidence = 0.5f,
                Reasoning = "测试结果"
            });
        return llmMock;
    }

    /// <summary>
    /// 创建默认的 Logger mock
    /// </summary>
    private static Mock<ILogger<MatchController>> CreateLoggerMock()
    {
        return new Mock<ILogger<MatchController>>();
    }

    #region MatchController Tests

    [Fact]
    public async Task MatchController_Match_ReturnsOkResult()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var batchProcessorMock = new Mock<IBatchProcessor>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var configMock = CreateConfigMock();
        var llmMock = CreateLLMServiceMock();
        var loggerMock = CreateLoggerMock();

        matchingEngineMock.Setup(m => m.MatchAsync(It.IsAny<MatchQuery>()))
            .ReturnsAsync(new MatchResult
            {
                Query = new MatchQuery { Project = "测试", TechnicalSpec = "DC24V" },
                BestMatch = null,
                SimilarityScore = 0.0f,
                Confidence = ConfidenceLevel.Low
            });

        var controller = new MatchController(matchingEngineMock.Object, batchProcessorMock.Object, auditLoggerMock.Object, configMock.Object, llmMock.Object, loggerMock.Object);

        // Act
        var result = await controller.Match(new MatchQuery { Project = "测试", TechnicalSpec = "DC24V" });

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task MatchController_Match_InvalidQuery_ReturnsBadRequest()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var batchProcessorMock = new Mock<IBatchProcessor>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var configMock = CreateConfigMock();
        var llmMock = CreateLLMServiceMock();
        var loggerMock = CreateLoggerMock();
        var controller = new MatchController(matchingEngineMock.Object, batchProcessorMock.Object, auditLoggerMock.Object, configMock.Object, llmMock.Object, loggerMock.Object);

        // Act
        var result = await controller.Match(new MatchQuery { Project = "", TechnicalSpec = "" });

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task MatchController_MatchBatch_ReturnsOkResult()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var batchProcessorMock = new Mock<IBatchProcessor>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var configMock = CreateConfigMock();
        var llmMock = CreateLLMServiceMock();
        var loggerMock = CreateLoggerMock();

        batchProcessorMock.Setup(b => b.ProcessBatchAsync(It.IsAny<BatchRequest>()))
            .ReturnsAsync(new BatchResult
            {
                Results = new List<MatchResult>(),
                Summary = new BatchSummary { TotalCount = 2 }
            });

        var controller = new MatchController(matchingEngineMock.Object, batchProcessorMock.Object, auditLoggerMock.Object, configMock.Object, llmMock.Object, loggerMock.Object);
        var request = new BatchRequest
        {
            Queries = new List<MatchQuery>
            {
                new() { Project = "项目1", TechnicalSpec = "指标1" },
                new() { Project = "项目2", TechnicalSpec = "指标2" }
            }
        };

        // Act
        var result = await controller.MatchBatch(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task MatchController_ConfirmMatch_ReturnsOkResult()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var batchProcessorMock = new Mock<IBatchProcessor>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var configMock = CreateConfigMock();
        var llmMock = CreateLLMServiceMock();
        var loggerMock = CreateLoggerMock();
        var controller = new MatchController(matchingEngineMock.Object, batchProcessorMock.Object, auditLoggerMock.Object, configMock.Object, llmMock.Object, loggerMock.Object);

        var request = new ConfirmMatchRequest
        {
            RecordId = "rec_123",
            Accepted = true,
            Feedback = "确认匹配"
        };

        // Act
        var result = await controller.ConfirmMatch(request);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task MatchController_ConfirmMatch_EmptyRecordId_ReturnsBadRequest()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var batchProcessorMock = new Mock<IBatchProcessor>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var configMock = CreateConfigMock();
        var llmMock = CreateLLMServiceMock();
        var loggerMock = CreateLoggerMock();
        var controller = new MatchController(matchingEngineMock.Object, batchProcessorMock.Object, auditLoggerMock.Object, configMock.Object, llmMock.Object, loggerMock.Object);

        var request = new ConfirmMatchRequest { RecordId = "" };

        // Act
        var result = await controller.ConfirmMatch(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    #endregion

    #region HistoryController Tests

    [Fact]
    public async Task HistoryController_GetAll_ReturnsOkResult()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var auditLoggerMock = new Mock<IAuditLogger>();

        matchingEngineMock.Setup(m => m.GetHistoryRecordsAsync())
            .ReturnsAsync(new List<HistoryRecord>
            {
                new() { Id = "1", Project = "项目1" }
            });

        var controller = new HistoryController(matchingEngineMock.Object, auditLoggerMock.Object);

        // Act
        var result = await controller.GetAll(null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task HistoryController_Create_ReturnsCreatedResult()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var auditLoggerMock = new Mock<IAuditLogger>();

        matchingEngineMock.Setup(m => m.AddHistoryRecordAsync(It.IsAny<HistoryRecord>()))
            .ReturnsAsync((HistoryRecord r) =>
            {
                r.Id = "new_id";
                return r;
            });

        var controller = new HistoryController(matchingEngineMock.Object, auditLoggerMock.Object);
        var request = new CreateHistoryRequest
        {
            Project = "新项目",
            TechnicalSpec = "DC24V",
            ActualSpec = "西门子模块"
        };

        // Act
        var result = await controller.Create(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.NotNull(createdResult.Value);
    }

    [Fact]
    public async Task HistoryController_Update_ReturnsOkResult()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var auditLoggerMock = new Mock<IAuditLogger>();

        matchingEngineMock.Setup(m => m.GetHistoryRecordsAsync())
            .ReturnsAsync(new List<HistoryRecord>
            {
                new() { Id = "rec_123", Project = "项目1" }
            });

        var controller = new HistoryController(matchingEngineMock.Object, auditLoggerMock.Object);
        var request = new UpdateHistoryRequest
        {
            Project = "更新后的项目",
            TechnicalSpec = "AC220V"
        };

        // Act
        var result = await controller.Update("rec_123", request);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task HistoryController_Update_NotFound_ReturnsNotFound()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var auditLoggerMock = new Mock<IAuditLogger>();

        matchingEngineMock.Setup(m => m.GetHistoryRecordsAsync())
            .ReturnsAsync(new List<HistoryRecord>());

        var controller = new HistoryController(matchingEngineMock.Object, auditLoggerMock.Object);
        var request = new UpdateHistoryRequest { Project = "项目" };

        // Act
        var result = await controller.Update("nonexistent", request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    #endregion

    #region ConfigController Tests

    [Fact]
    public void ConfigController_GetConfig_ReturnsOkResult()
    {
        // Arrange
        var configMock = new Mock<IConfigManager>();
        var storageMock = new Mock<IJsonStorageService>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            Version = "1.0",
            Matching = new MatchingConfig { MatchSuccessThreshold = 0.8f },
            Embedding = new EmbeddingConfig { ApiKey = "test-key" },
            LLM = new LLMConfig { ApiKey = "test-key" }
        });
        var cacheServiceMock = new Mock<ICacheService>();

        var controller = new ConfigController(configMock.Object, storageMock.Object, auditLoggerMock.Object, httpClientFactoryMock.Object, cacheServiceMock.Object);

        // Act
        var result = controller.GetConfig();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ConfigController_UpdateConfig_ReturnsOkResult()
    {
        // Arrange
        var configMock = new Mock<IConfigManager>();
        var storageMock = new Mock<IJsonStorageService>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());
        var cacheServiceMock = new Mock<ICacheService>();

        var controller = new ConfigController(configMock.Object, storageMock.Object, auditLoggerMock.Object, httpClientFactoryMock.Object, cacheServiceMock.Object);
        var request = new UpdateConfigRequest
        {
            Matching = new MatchingConfig { MatchSuccessThreshold = 0.85f }
        };

        // Act
        var result = await controller.UpdateConfig(request);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    #endregion

    #region AuditController Tests

    [Fact]
    public async Task AuditController_Query_ReturnsOkResult()
    {
        // Arrange
        var auditMock = new Mock<IAuditLogger>();
        auditMock.Setup(a => a.QueryLogsAsync(It.IsAny<AuditLogFilter>()))
            .ReturnsAsync(new AuditQueryResult
            {
                Entries = new List<AuditLogEntry>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 50
            });

        var controller = new AuditController(auditMock.Object);

        // Act
        var result = await controller.Query(new AuditQueryParams());

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AuditController_Query_WithFilter_ReturnsFilteredResults()
    {
        // Arrange
        var auditMock = new Mock<IAuditLogger>();
        auditMock.Setup(a => a.QueryLogsAsync(It.IsAny<AuditLogFilter>()))
            .ReturnsAsync(new AuditQueryResult
            {
                Entries = new List<AuditLogEntry>
                {
                    new() { Id = "1", ActionType = "query" }
                },
                TotalCount = 1,
                Page = 1,
                PageSize = 50
            });

        var controller = new AuditController(auditMock.Object);
        var queryParams = new AuditQueryParams { ActionType = "query" };

        // Act
        var result = await controller.Query(queryParams);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var queryResult = Assert.IsType<AuditQueryResult>(okResult.Value);
        Assert.Single(queryResult.Entries);
    }

    [Fact]
    public async Task AuditController_GetStats_ReturnsOkResult()
    {
        // Arrange
        var auditMock = new Mock<IAuditLogger>();
        auditMock.Setup(a => a.QueryLogsAsync(It.IsAny<AuditLogFilter>()))
            .ReturnsAsync(new AuditQueryResult
            {
                Entries = new List<AuditLogEntry>
                {
                    new() { ActionType = "query" },
                    new() { ActionType = "confirm_match" }
                },
                TotalCount = 2
            });

        var controller = new AuditController(auditMock.Object);

        // Act
        var result = await controller.GetStats(null, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var stats = Assert.IsType<AuditStats>(okResult.Value);
        Assert.Equal(1, stats.TotalQueries);
        Assert.Equal(1, stats.TotalConfirms);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task MatchController_Match_ServiceException_ThrowsException()
    {
        // Arrange
        var matchingEngineMock = new Mock<IMatchingEngine>();
        var batchProcessorMock = new Mock<IBatchProcessor>();
        var auditLoggerMock = new Mock<IAuditLogger>();
        var configMock = CreateConfigMock();
        var llmMock = CreateLLMServiceMock();
        var loggerMock = CreateLoggerMock();

        matchingEngineMock.Setup(m => m.MatchAsync(It.IsAny<MatchQuery>()))
            .ThrowsAsync(new Exception("服务异常"));

        var controller = new MatchController(matchingEngineMock.Object, batchProcessorMock.Object, auditLoggerMock.Object, configMock.Object, llmMock.Object, loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() =>
            controller.Match(new MatchQuery { Project = "测试", TechnicalSpec = "DC24V" }));
    }

    #endregion
}
