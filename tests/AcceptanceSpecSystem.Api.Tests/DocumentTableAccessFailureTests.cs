using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DocumentTableAccessFailureTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DocumentTableAccessFailureTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExtractMatchSourceItemsAsync_WhenExcelContentIsCorrupted_ShouldThrowBusinessError()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DocumentTableAccessService>();
        var wordFile = new WordFile
        {
            Id = 91001,
            FileType = UploadedFileType.ExcelXlsx,
            FileContent = [0x01, 0x02, 0x03, 0x04]
        };

        var action = () => service.ExtractMatchSourceItemsAsync(wordFile, 0, 0, 1);

        await action.Should().ThrowAsync<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage("文档解析失败，请确认文件完整且未被占用");
    }

    [Fact]
    public async Task ExtractMatchSourceItemsAsync_WhenExcelTableIsMissingOrEmpty_ShouldReturnEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DocumentTableAccessService>();
        var wordFile = new WordFile
        {
            Id = 91002,
            FileType = UploadedFileType.ExcelXlsx,
            FileContent = CreateEmptyWorkbookBytes()
        };

        var missingTableItems = await service.ExtractMatchSourceItemsAsync(wordFile, 99, 0, 1);
        var emptySheetItems = await service.ExtractMatchSourceItemsAsync(wordFile, 0, 0, 1);

        missingTableItems.Should().BeEmpty();
        emptySheetItems.Should().BeEmpty();
    }

    private static byte[] CreateEmptyWorkbookBytes()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("Empty");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
