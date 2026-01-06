using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

public class ConfigManagerTests
{
    private readonly Mock<IJsonStorageService> _storageMock;
    private readonly Mock<ILogger<ConfigManager>> _loggerMock;
    private readonly ConfigManager _configManager;

    public ConfigManagerTests()
    {
        _storageMock = new Mock<IJsonStorageService>();
        _storageMock.Setup(s => s.ReadAsync<SystemConfig>(It.IsAny<string>()))
            .ReturnsAsync(new SystemConfig
            {
                Version = "1.0",
                Embedding = new EmbeddingConfig { Model = "text-embedding-3-small" },
                LLM = new LLMConfig { Model = "gpt-4o-mini" },
                Matching = new MatchingConfig
                {
                    MatchSuccessThreshold = 0.80f
                }
            });
        _storageMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<SystemConfig>()))
            .Returns(Task.CompletedTask);

        _loggerMock = new Mock<ILogger<ConfigManager>>();

        // 使用工厂方法创建 ConfigManager
        _configManager = ConfigManager.Create(_storageMock.Object, _loggerMock.Object);

        // 等待异步初始化完成
        Thread.Sleep(100);
    }

    [Fact]
    public void GetAll_ReturnsConfig()
    {
        // Act
        var config = _configManager.GetAll();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("1.0", config.Version);
        Assert.Equal(0.80f, config.Matching.MatchSuccessThreshold);
    }

    [Fact]
    public void Get_ReturnsSpecificSection()
    {
        // Act
        var matching = _configManager.Get<MatchingConfig>("Matching");

        // Assert
        Assert.NotNull(matching);
        Assert.Equal(0.80f, matching.MatchSuccessThreshold);
    }

    [Fact]
    public void Set_UpdatesConfig()
    {
        // Arrange
        var newMatching = new MatchingConfig
        {
            MatchSuccessThreshold = 0.85f
        };

        // Act
        _configManager.Set("Matching", newMatching);
        var updated = _configManager.Get<MatchingConfig>("Matching");

        // Assert
        Assert.Equal(0.85f, updated.MatchSuccessThreshold);
    }

    [Fact]
    public async Task UpdateMatchingConfigAsync_UpdatesAndSaves()
    {
        // Arrange
        var newConfig = new MatchingConfig
        {
            MatchSuccessThreshold = 0.90f
        };

        // Act
        await _configManager.UpdateMatchingConfigAsync(newConfig);

        // Assert
        _storageMock.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<SystemConfig>()), Times.Once);
    }

    [Fact]
    public void GetHistory_ReturnsChangeHistory()
    {
        // Arrange
        _configManager.Set("Matching", new MatchingConfig { MatchSuccessThreshold = 0.8f });
        _configManager.Set("Matching", new MatchingConfig { MatchSuccessThreshold = 0.85f });

        // Act
        var history = _configManager.GetHistory();

        // Assert
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public void Get_InvalidKey_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _configManager.Get<object>("InvalidKey"));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.8f)]
    [InlineData(0.95f)]
    public void MatchingConfig_ThresholdIsValid(float threshold)
    {
        // Arrange
        var config = new MatchingConfig
        {
            MatchSuccessThreshold = threshold
        };

        // Assert
        Assert.True(config.MatchSuccessThreshold > 0 && config.MatchSuccessThreshold <= 1);
    }
}
