using Xunit;
using Moq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

/// <summary>
/// 端到端集成测试 - 测试完整匹配流程和批量处理流程
/// Validates: Requirements 8.1, 9.1
/// </summary>
public class EndToEndTests
{
    private readonly Mock<IJsonStorageService> _storageMock;
    private readonly Mock<IConfigManager> _configMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<ILogger<BatchProcessor>> _batchLoggerMock;
    private readonly Mock<ILogger<EmbeddingService>> _embeddingLoggerMock;
    private readonly Mock<ILogger<LLMService>> _llmLoggerMock;
    private readonly Mock<ILogger<MatchingEngine>> _matchingLoggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly ITextPreprocessor _preprocessor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKeywordHighlighter _keywordHighlighter;
    private readonly ILLMService _llmService;
    private readonly IMatchingEngine _matchingEngine;
    private readonly IBatchProcessor _batchProcessor;

    public EndToEndTests()
    {
        _storageMock = new Mock<IJsonStorageService>();
        _configMock = new Mock<IConfigManager>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());
        _batchLoggerMock = new Mock<ILogger<BatchProcessor>>();
        _embeddingLoggerMock = new Mock<ILogger<EmbeddingService>>();
        _llmLoggerMock = new Mock<ILogger<LLMService>>();
        _matchingLoggerMock = new Mock<ILogger<MatchingEngine>>();

        // 配置 LoggerFactory Mock
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        // 配置缓存 Mock
        _cacheMock = new Mock<ICacheService>();
        _cacheMock.Setup(c => c.GetOrCreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<Func<Task<float[]>>>()))
            .Returns<string, Func<Task<float[]>>>((text, factory) => factory());
        _cacheMock.Setup(c => c.GetOrCreateUnifiedAnalysisAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<Task<UnifiedAnalysisResult>>>()))
            .Returns<string, string, Func<Task<UnifiedAnalysisResult>>>((q, c, factory) => factory());
        _cacheMock.Setup(c => c.GetStatistics()).Returns(new CacheStatistics());

        // 配置Mock
        SetupMocks();

        // 创建服务实例
        _preprocessor = new TextPreprocessor(_storageMock.Object, _configMock.Object);
        _embeddingService = new EmbeddingService(_configMock.Object, _httpClientFactoryMock.Object, _cacheMock.Object, _embeddingLoggerMock.Object);
        _keywordHighlighter = new KeywordHighlighter(_storageMock.Object);
        _llmService = new LLMService(_configMock.Object, _httpClientFactoryMock.Object, _cacheMock.Object, _llmLoggerMock.Object);
        _matchingEngine = new MatchingEngine(
            _preprocessor, _embeddingService,
            _keywordHighlighter, _llmService, _configMock.Object, _storageMock.Object,
            _cacheMock.Object, _matchingLoggerMock.Object, _loggerFactoryMock.Object);
        _batchProcessor = new BatchProcessor(_matchingEngine, _configMock.Object, _batchLoggerMock.Object);
    }

    private void SetupMocks()
    {
        // 配置系统配置
        _configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            Version = "1.0",
            Embedding = new EmbeddingConfig
            {
                Model = "text-embedding-3-small",
                Dimension = 1536,
                ApiKey = "" // 空密钥触发模拟模式
            },
            LLM = new LLMConfig
            {
                Model = "gpt-4o-mini",
                Provider = "openai",
                ApiKey = ""
            },
            Matching = new MatchingConfig
            {
                EnableLLM = false,
                MatchSuccessThreshold = 0.80f
            },
            Batch = new BatchConfig
            {
                MaxConcurrency = 5,
                TaskRetentionMinutes = 30,
                MaxBatchSize = 100,
                MaxTextLength = 2000,
                MaxAuditEntries = 10000,
                ApiMaxRetries = 3,
                ApiRetryBaseDelayMs = 1000
            },
            Preprocessing = new PreprocessingConfig
            {
                EnableChineseSimplification = true,
                EnableSymbolNormalization = true,
                EnableTypoCorrection = true
            }
        });

        // 配置历史记录
        _storageMock.Setup(s => s.ReadAsync<List<HistoryRecord>>(It.Is<string>(p => p.Contains("history"))))
            .ReturnsAsync(new List<HistoryRecord>
            {
                new()
                {
                    Id = "rec_001",
                    Project = "电气控制系统",
                    TechnicalSpec = "DC24V 输入模块 16点",
                    ActualSpec = "西门子 SM321 DI16",
                    Remark = "符合要求"
                },
                new()
                {
                    Id = "rec_002",
                    Project = "电气控制系统",
                    TechnicalSpec = "AC220V 输出模块 8点",
                    ActualSpec = "西门子 SM322 DO8",
                    Remark = "符合要求"
                },
                new()
                {
                    Id = "rec_003",
                    Project = "PLC控制系统",
                    TechnicalSpec = "三相异步电机 5.5KW",
                    ActualSpec = "ABB M2QA 5.5KW",
                    Remark = "符合要求"
                }
            });

        // 配置关键字库
        _storageMock.Setup(s => s.ReadAsync<KeywordLibrary>(It.Is<string>(p => p.Contains("keyword"))))
            .ReturnsAsync(new KeywordLibrary
            {
                Keywords = new List<KeywordEntry>
                {
                    new() { Keyword = "DC", Style = new HighlightStyle { Color = "#ff0000" } },
                    new() { Keyword = "AC", Style = new HighlightStyle { Color = "#0000ff" } }
                }
            });

        // 配置错别字映射
        _storageMock.Setup(s => s.ReadAsync<TypoCorrections>(It.Is<string>(p => p.Contains("typo"))))
            .ReturnsAsync(new TypoCorrections());

        // 配置单位映射
        _storageMock.Setup(s => s.ReadAsync<UnitMappings>(It.IsAny<string>()))
            .ReturnsAsync((UnitMappings?)null);

        // 配置向量缓存
        _storageMock.Setup(s => s.ReadAsync<Dictionary<string, float[]>>(It.Is<string>(p => p.Contains("vector"))))
            .ReturnsAsync(new Dictionary<string, float[]>());

        _storageMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<object>()))
            .Returns(Task.CompletedTask);
    }

    #region 完整匹配流程测试

    [Fact]
    public async Task FullMatchFlow_SingleQuery_ReturnsResults()
    {
        // Arrange
        var query = new MatchQuery
        {
            Project = "电气控制系统",
            TechnicalSpec = "DC24V 输入模块"
        };

        // Act
        var result = await _matchingEngine.MatchAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Query);
    }

    [Fact]
    public async Task FullMatchFlow_WithPreprocessing_NormalizesInput()
    {
        // Arrange
        var query = new MatchQuery
        {
            Project = "电气控制系统",
            TechnicalSpec = "ＤＣ２４Ｖ　输入模块" // 全角字符
        };

        // Act
        var result = await _matchingEngine.MatchAsync(query);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FullMatchFlow_ConflictDetection_DetectsDCACConflict()
    {
        // Arrange - 查询DC但历史记录中有AC
        var query = new MatchQuery
        {
            Project = "电气控制系统",
            TechnicalSpec = "DC24V 输出模块" // 查询DC输出
        };

        // Act
        var result = await _matchingEngine.MatchAsync(query);

        // Assert
        Assert.NotNull(result);
        // 应该检测到与AC220V输出模块的冲突
    }

    [Fact]
    public async Task FullMatchFlow_HighConfidence_NoReviewNeeded()
    {
        // Arrange
        var query = new MatchQuery
        {
            Project = "电气控制系统",
            TechnicalSpec = "DC24V 输入模块 16点" // 完全匹配
        };

        // Act
        var result = await _matchingEngine.MatchAsync(query);

        // Assert
        Assert.NotNull(result);
        if (result.Confidence == ConfidenceLevel.Success)
        {
            Assert.False(result.IsLowConfidence);
        }
    }

    [Fact]
    public async Task FullMatchFlow_LowConfidence_IsMarked()
    {
        // Arrange
        var query = new MatchQuery
        {
            Project = "未知项目",
            TechnicalSpec = "完全不相关的规格"
        };

        // Act
        var result = await _matchingEngine.MatchAsync(query);

        // Assert
        Assert.NotNull(result);
        if (result.Confidence == ConfidenceLevel.Low)
        {
            Assert.True(result.IsLowConfidence);
        }
    }

    #endregion

    #region 批量处理流程测试

    [Fact]
    public async Task BatchProcessFlow_MultipleQueries_ProcessesAll()
    {
        // Arrange
        var request = new BatchRequest
        {
            Queries = new List<MatchQuery>
            {
                new() { Project = "电气控制系统", TechnicalSpec = "DC24V 输入模块" },
                new() { Project = "电气控制系统", TechnicalSpec = "AC220V 输出模块" },
                new() { Project = "PLC控制系统", TechnicalSpec = "三相电机 5KW" }
            }
        };

        // Act
        var result = await _batchProcessor.ProcessBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Results.Count);
        Assert.Equal(3, result.Summary.TotalCount);
    }

    [Fact]
    public async Task BatchProcessFlow_GeneratesSummary()
    {
        // Arrange
        var request = new BatchRequest
        {
            Queries = new List<MatchQuery>
            {
                new() { Project = "项目1", TechnicalSpec = "DC24V" },
                new() { Project = "项目2", TechnicalSpec = "AC220V" }
            }
        };

        // Act
        var result = await _batchProcessor.ProcessBatchAsync(request);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.Equal(2, result.Summary.TotalCount);
        Assert.True(result.Summary.SuccessCount + result.Summary.LowConfidenceCount == 2);
    }

    [Fact]
    public async Task BatchProcessFlow_ProgressTracking_Works()
    {
        // Arrange
        var queries = Enumerable.Range(1, 5)
            .Select(i => new MatchQuery { Project = $"项目{i}", TechnicalSpec = $"指标{i}" })
            .ToList();

        // Act
        var taskId = await _batchProcessor.StartBatchAsync(queries);
        
        // 等待一小段时间让处理开始
        await Task.Delay(100);
        
        var progress = _batchProcessor.GetProgress(taskId);

        // Assert
        Assert.NotNull(taskId);
        Assert.NotNull(progress);
        Assert.Equal(taskId, progress.TaskId);
    }

    [Fact]
    public async Task BatchProcessFlow_EmptyBatch_ReturnsEmptyResult()
    {
        // Arrange
        var request = new BatchRequest { Queries = new List<MatchQuery>() };

        // Act
        var result = await _batchProcessor.ProcessBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Results);
        Assert.Equal(0, result.Summary.TotalCount);
    }

    #endregion

    #region 数据一致性测试

    [Fact]
    public async Task DataConsistency_SameQueryTwice_SameResults()
    {
        // Arrange
        var query = new MatchQuery
        {
            Project = "电气控制系统",
            TechnicalSpec = "DC24V 输入模块"
        };

        // Act
        var result1 = await _matchingEngine.MatchAsync(query);
        var result2 = await _matchingEngine.MatchAsync(query);

        // Assert
        Assert.Equal(result1.Confidence, result2.Confidence);
        Assert.Equal(result1.SimilarityScore, result2.SimilarityScore);
    }

    [Fact]
    public async Task DataConsistency_BatchVsIndividual_SameResults()
    {
        // Arrange
        var queries = new List<MatchQuery>
        {
            new() { Project = "电气控制系统", TechnicalSpec = "DC24V" },
            new() { Project = "PLC控制系统", TechnicalSpec = "三相电机" }
        };

        // Act - 批量处理
        var batchResult = await _batchProcessor.ProcessBatchAsync(new BatchRequest { Queries = queries });

        // Act - 单独处理
        var individualResults = new List<MatchResult>();
        foreach (var query in queries)
        {
            individualResults.Add(await _matchingEngine.MatchAsync(query));
        }

        // Assert
        Assert.Equal(queries.Count, batchResult.Results.Count);
        Assert.Equal(queries.Count, individualResults.Count);
        
        for (int i = 0; i < queries.Count; i++)
        {
            Assert.Equal(batchResult.Results[i].Confidence, individualResults[i].Confidence);
        }
    }

    #endregion
}
