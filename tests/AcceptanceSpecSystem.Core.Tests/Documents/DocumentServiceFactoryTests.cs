using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

/// <summary>
/// DocumentServiceFactory测试
/// </summary>
public class DocumentServiceFactoryTests
{
    private readonly DocumentServiceFactory _factory = new();

    [Fact]
    public void GetParser_ShouldReturnWordParser_ForDocxFile()
    {
        // Act
        var parser = _factory.GetParser("test.docx");

        // Assert
        parser.Should().NotBeNull();
        parser!.DocumentType.Should().Be(DocumentType.Word);
    }

    [Fact]
    public void GetParser_ShouldReturnExcelParser_ForXlsxFile()
    {
        // Act
        var parser = _factory.GetParser("test.xlsx");

        // Assert
        parser.Should().NotBeNull();
        parser!.DocumentType.Should().Be(DocumentType.Excel);
    }

    [Fact]
    public void GetWriter_ShouldReturnWordWriter_ForDocxFile()
    {
        // Act
        var writer = _factory.GetWriter("test.docx");

        // Assert
        writer.Should().NotBeNull();
        writer!.DocumentType.Should().Be(DocumentType.Word);
    }

    [Fact]
    public void GetWriter_ShouldReturnExcelWriter_ForXlsxFile()
    {
        // Act
        var writer = _factory.GetWriter("test.xlsx");

        // Assert
        writer.Should().NotBeNull();
        writer!.DocumentType.Should().Be(DocumentType.Excel);
    }

    [Fact]
    public void GetParser_ByDocumentType_ShouldReturnCorrectParser()
    {
        // Act
        var parser = _factory.GetParser(DocumentType.Word);

        // Assert
        parser.Should().NotBeNull();
        parser!.DocumentType.Should().Be(DocumentType.Word);
    }

    [Fact]
    public void GetWriter_ByDocumentType_ShouldReturnCorrectWriter()
    {
        // Act
        var writer = _factory.GetWriter(DocumentType.Word);

        // Assert
        writer.Should().NotBeNull();
        writer!.DocumentType.Should().Be(DocumentType.Word);
    }

    [Fact]
    public void GetWriter_ByExcelDocumentType_ShouldReturnExcelWriter()
    {
        // Act
        var writer = _factory.GetWriter(DocumentType.Excel);

        // Assert
        writer.Should().NotBeNull();
        writer!.DocumentType.Should().Be(DocumentType.Excel);
    }

    [Fact]
    public void GetSupportedTypes_ShouldContainWord()
    {
        // Act
        var types = _factory.GetSupportedTypes();

        // Assert
        types.Should().Contain(DocumentType.Word);
        types.Should().Contain(DocumentType.Excel);
    }

    [Fact]
    public void IsSupported_ShouldReturnTrue_ForDocxFile()
    {
        // Act
        var result = _factory.IsSupported("test.docx");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSupported_ShouldReturnFalse_ForUnsupportedFile()
    {
        // Act & Assert
        _factory.IsSupported("test.xlsx").Should().BeTrue();
        _factory.IsSupported("test.doc").Should().BeFalse();
        _factory.IsSupported("test.txt").Should().BeFalse();
    }

    [Fact]
    public void IsSupported_ShouldBeCaseInsensitive()
    {
        // Act & Assert
        _factory.IsSupported("test.DOCX").Should().BeTrue();
        _factory.IsSupported("test.Docx").Should().BeTrue();
    }
}
