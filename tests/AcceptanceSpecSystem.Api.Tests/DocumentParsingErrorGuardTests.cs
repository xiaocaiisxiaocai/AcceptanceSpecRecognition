using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DocumentParsingErrorGuardTests
{
    private static readonly string ServiceSource = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "AcceptanceSpecSystem.Api",
            "Services",
            "DocumentTableAccessService.cs"));

    [Fact]
    public void DocumentTableAccessService_ShouldNotSwallowParsingExceptionsAsEmptyResults()
    {
        ServiceSource.Should().NotContain("catch\r\n        {\r\n            return [];");
        ServiceSource.Should().NotContain("catch\n        {\n            return [];");
    }

    [Fact]
    public void DocumentTableAccessService_SourceExtractionPaths_ShouldPreserveCancellationAndConvertParsingErrors()
    {
        var matchMethod = ExtractMethod("ExtractMatchSourceItemsAsync");
        var replyMethod = ExtractMethod("ExtractReplySourceItemsAsync");

        matchMethod.Should().Contain("catch (OperationCanceledException)");
        replyMethod.Should().Contain("catch (OperationCanceledException)");
        matchMethod.Should().Contain("throw CreateDocumentParsingException(wordFile, tableIndex, ex);");
        replyMethod.Should().Contain("throw CreateDocumentParsingException(wordFile, config.TableIndex, ex);");
        CountOccurrences(ServiceSource, "CreateDocumentParsingException(").Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void DocumentTableAccessService_ParsingErrorLog_ShouldOnlyContainSafeStructuredFields()
    {
        const string factorySignature =
            "private ApplicationServiceException CreateDocumentParsingException";
        var start = ServiceSource.IndexOf(factorySignature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = ServiceSource.IndexOf("    private ", start + factorySignature.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var exceptionFactory = ServiceSource[start..end];

        exceptionFactory.Should().Contain("FileId={FileId}");
        exceptionFactory.Should().Contain("FileType={FileType}");
        exceptionFactory.Should().Contain("TableIndex={TableIndex}");
        exceptionFactory.Should().Contain("ExceptionType={ExceptionType}");
        exceptionFactory.Should().NotContain("FileName={FileName}");
        exceptionFactory.Should().NotContain("CellValue");
        exceptionFactory.Should().NotContain("PreviewText={PreviewText}");
    }

    private static string ExtractMethod(string methodName)
    {
        var start = ServiceSource.IndexOf(methodName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var nextSummary = ServiceSource.IndexOf("/// <summary>", start + methodName.Length, StringComparison.Ordinal);
        if (nextSummary < 0)
        {
            nextSummary = ServiceSource.IndexOf("    private ", start + methodName.Length, StringComparison.Ordinal);
        }

        nextSummary.Should().BeGreaterThan(start);
        return ServiceSource[start..nextSummary];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
