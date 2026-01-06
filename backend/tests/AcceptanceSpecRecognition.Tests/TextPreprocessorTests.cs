using Xunit;
using Moq;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Tests;

public class TextPreprocessorTests
{
    private readonly Mock<IJsonStorageService> _storageMock;
    private readonly Mock<IConfigManager> _configMock;
    private readonly TextPreprocessor _preprocessor;

    public TextPreprocessorTests()
    {
        _storageMock = new Mock<IJsonStorageService>();
        _storageMock.Setup(s => s.ReadAsync<TypoCorrections>(It.IsAny<string>()))
            .ReturnsAsync(new TypoCorrections
            {
                Corrections = new Dictionary<string, string>
                {
                    { "电亚", "电压" },
                    { "传敢器", "传感器" }
                }
            });
        _storageMock.Setup(s => s.ReadAsync<UnitMappings>(It.IsAny<string>()))
            .ReturnsAsync((UnitMappings?)null);

        _configMock = new Mock<IConfigManager>();
        _configMock.Setup(c => c.GetAll()).Returns(new SystemConfig
        {
            Preprocessing = new PreprocessingConfig
            {
                EnableChineseSimplification = true,
                EnableSymbolNormalization = true,
                EnableTypoCorrection = true
            }
        });

        _preprocessor = new TextPreprocessor(_storageMock.Object, _configMock.Object);
    }

    [Fact]
    public void NormalizeSymbols_ConvertChinesePunctuation()
    {
        // Arrange
        var input = "DC24V\uFF0C输入模块";  // 中文逗号

        // Act
        var result = _preprocessor.NormalizeSymbols(input);

        // Assert
        Assert.Equal("DC24V,输入模块", result);
    }

    [Fact]
    public void NormalizeSymbols_ConvertFullWidthToHalfWidth()
    {
        // Arrange
        var input = "\uFF24\uFF23\uFF12\uFF14\uFF36";  // 全角DC24V

        // Act
        var result = _preprocessor.NormalizeSymbols(input);

        // Assert
        Assert.Equal("DC24V", result);
    }

    [Fact]
    public void NormalizeWhitespace_RemoveExtraSpaces()
    {
        // Arrange
        var input = "DC24V   输入   模块";

        // Act
        var result = _preprocessor.NormalizeWhitespace(input);

        // Assert
        Assert.Equal("DC24V 输入 模块", result);
    }

    [Fact]
    public void CorrectTypos_FixCommonTypos()
    {
        // Arrange
        var input = "电亚传敢器";

        // Act
        var result = _preprocessor.CorrectTypos(input);

        // Assert
        Assert.Equal("电压传感器", result);
    }

    [Fact]
    public void Preprocess_CombinesAllSteps()
    {
        // Arrange
        var input = "电亚传敢器\uFF0C  DC24V";

        // Act
        var result = _preprocessor.Preprocess(input);

        // Assert
        Assert.NotEqual(input, result.Normalized);
        Assert.True(result.Corrections.Count > 0);
    }

    [Fact]
    public void NormalizeUnits_StandardizeVoltageUnits()
    {
        // Arrange
        var input = "24伏直流";

        // Act
        var result = _preprocessor.NormalizeUnits(input);

        // Assert
        Assert.Contains("V", result.Text);
        Assert.Contains("DC", result.Text);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void NormalizeSymbols_HandlesEmptyAndNull(string? input, string? expected)
    {
        var result = _preprocessor.NormalizeSymbols(input!);
        Assert.Equal(expected, result);
    }
}
