using Xunit;
using Moq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

public class LLMServiceTests
{
    private readonly Mock<IConfigManager> _configMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<ILogger<LLMService>> _loggerMock;
    private readonly LLMService _llmService;

    public LLMServiceTests()
    {
        _configMock = new Mock<IConfigManager>();
        _configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            LLM = new LLMConfig
            {
                Model = "gpt-4o-mini",
                Provider = "openai",
                TimeoutSeconds = 30,
                MaxRetries = 3,
                ApiKey = "" // Empty key triggers mock mode
            },
            Matching = new MatchingConfig
            {
                EnableLLM = false // Disable LLM for testing
            }
        });

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        _cacheMock = new Mock<ICacheService>();
        // 缓存返回 null 表示未命中，触发实际计算
        _cacheMock.Setup(c => c.GetOrCreateUnifiedAnalysisAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<Task<UnifiedAnalysisResult>>>()))
            .Returns<string, string, Func<Task<UnifiedAnalysisResult>>>((q, c, factory) => factory());

        _loggerMock = new Mock<ILogger<LLMService>>();

        _llmService = new LLMService(_configMock.Object, _httpClientFactoryMock.Object, _cacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task AnalyzeUnifiedAsync_DetectsDCACConflict()
    {
        // Arrange
        var query = "DC24V 输入模块";
        var candidate = "AC220V 输入模块";

        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(query, candidate);

        // Assert
        Assert.True(result.HasConflict);
        Assert.Equal("electrical", result.ConflictType);
    }

    [Fact]
    public async Task AnalyzeUnifiedAsync_NoConflictForSameType()
    {
        // Arrange
        var query = "DC24V 输入模块";
        var candidate = "DC12V 输出模块";

        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(query, candidate);

        // Assert
        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task AnalyzeUnifiedAsync_DetectsPhaseConflict()
    {
        // Arrange
        var query = "三相电机";
        var candidate = "单相电机";

        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(query, candidate);

        // Assert
        Assert.True(result.HasConflict);
        Assert.Equal("electrical", result.ConflictType);
    }

    [Fact]
    public async Task AnalyzeUnifiedAsync_ReturnsValidResult()
    {
        // Arrange
        var query = "电气控制系统 DC24V 输入模块";
        var candidate = "电气控制系统 DC24V 输入模块 16点";

        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(query, candidate);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Reasoning);
    }

    [Fact]
    public async Task AnalyzeUnifiedAsync_EmptyCandidate_ReturnsDefault()
    {
        // Arrange
        var query = "测试项目 测试指标";
        var candidate = "";

        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(query, candidate);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.HasConflict);
    }

    [Theory]
    [InlineData("DC24V", "AC220V", true)]
    [InlineData("DC24V", "DC12V", false)]
    [InlineData("AC220V", "AC380V", false)]
    [InlineData("单相", "三相", true)]
    [InlineData("三相", "三相", false)]
    public async Task AnalyzeUnifiedAsync_VariousScenarios(string querySpec, string candidateSpec, bool expectConflict)
    {
        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(querySpec, candidateSpec);

        // Assert
        Assert.Equal(expectConflict, result.HasConflict);
    }

    [Fact]
    public async Task AnalyzeUnifiedAsync_DetectsNPNPNPConflict()
    {
        // Arrange
        var query = "NPN传感器";
        var candidate = "PNP传感器";

        // Act
        var result = await _llmService.AnalyzeUnifiedAsync(query, candidate);

        // Assert
        Assert.True(result.HasConflict);
        Assert.Equal("electrical", result.ConflictType);
    }

    [Fact]
    public void GetModelInfo_ReturnsModelInfo()
    {
        // Act
        var info = _llmService.GetModelInfo();

        // Assert
        Assert.NotNull(info);
        Assert.Equal("gpt-4o-mini", info.Name);
        Assert.Equal("openai", info.Provider);
    }
}
