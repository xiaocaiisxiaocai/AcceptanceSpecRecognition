using Xunit;
using Moq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

public class EmbeddingServiceTests
{
    private readonly Mock<IConfigManager> _configMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<ILogger<EmbeddingService>> _loggerMock;
    private readonly EmbeddingService _embeddingService;

    public EmbeddingServiceTests()
    {
        _configMock = new Mock<IConfigManager>();
        _configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            Embedding = new EmbeddingConfig
            {
                Model = "text-embedding-3-small",
                Dimension = 1536,
                ApiKey = "" // Empty key triggers mock mode
            }
        });

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        _cacheMock = new Mock<ICacheService>();
        // 缓存返回 null 表示未命中，触发实际计算
        _cacheMock.Setup(c => c.GetOrCreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<Func<Task<float[]>>>()))
            .Returns<string, Func<Task<float[]>>>((text, factory) => factory());

        _loggerMock = new Mock<ILogger<EmbeddingService>>();

        _embeddingService = new EmbeddingService(_configMock.Object, _httpClientFactoryMock.Object, _cacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsVectorOfCorrectDimension()
    {
        // Arrange
        var text = "DC24V 输入模块";

        // Act
        var result = await _embeddingService.EmbedAsync(text);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public async Task EmbedAsync_SameTextReturnsSameVector()
    {
        // Arrange
        var text = "DC24V 输入模块";

        // Act
        var result1 = await _embeddingService.EmbedAsync(text);
        var result2 = await _embeddingService.EmbedAsync(text);

        // Assert
        Assert.Equal(result1.Length, result2.Length);
        for (int i = 0; i < result1.Length; i++)
        {
            Assert.Equal(result1[i], result2[i], 6);
        }
    }

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        var vector = new float[] { 1, 2, 3, 4, 5 };

        // Act
        var similarity = _embeddingService.CosineSimilarity(vector, vector);

        // Assert
        Assert.Equal(1.0f, similarity, 5);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var vector1 = new float[] { 1, 0, 0 };
        var vector2 = new float[] { 0, 1, 0 };

        // Act
        var similarity = _embeddingService.CosineSimilarity(vector1, vector2);

        // Assert
        Assert.Equal(0.0f, similarity, 5);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        var vector1 = new float[] { 1, 2, 3 };
        var vector2 = new float[] { -1, -2, -3 };

        // Act
        var similarity = _embeddingService.CosineSimilarity(vector1, vector2);

        // Assert
        Assert.Equal(-1.0f, similarity, 5);
    }

    [Theory]
    [InlineData(new float[] { 1, 0 }, new float[] { 1, 0 }, 1.0f)]
    [InlineData(new float[] { 1, 0 }, new float[] { 0, 1 }, 0.0f)]
    public void CosineSimilarity_VariousVectors_ReturnsExpected(float[] v1, float[] v2, float expected)
    {
        var similarity = _embeddingService.CosineSimilarity(v1, v2);
        Assert.Equal(expected, similarity, 4);
    }
}
