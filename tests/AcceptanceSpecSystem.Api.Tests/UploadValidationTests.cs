using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;
using System.Text;

namespace AcceptanceSpecSystem.Api.Tests;

public class UploadValidationTests
{
    [Fact]
    public void ValidateOfficeDocument_WhenFileExceedsLimit_ShouldThrowFriendlyError()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        IFormFile file = new FormFile(stream, 0, UploadFileValidation.MaxAllowedFileSizeBytes + 1, "file", "too-large.docx");

        var action = () => UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        action.Should().Throw<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage($"文件大小不能超过{UploadFileValidation.MaxAllowedFileSizeMegabytes}MB");
    }

    [Fact]
    public void ValidateOfficeDocument_WhenFileContentDoesNotMatchExtension_ShouldThrowFriendlyError()
    {
        var payload = "not-a-zip-file"u8.ToArray();
        using var stream = new MemoryStream(payload);
        IFormFile file = new FormFile(stream, 0, payload.Length, "file", "fake.docx");

        var action = () => UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        action.Should().Throw<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage("文件内容与扩展名不匹配或文件已损坏");
    }

    [Fact]
    public void ValidateOfficeDocument_WhenOfficeZipHasTooManyEntries_ShouldThrowFriendlyError()
    {
        var payload = CreateOfficeZip(entryCount: 1_501);
        using var stream = new MemoryStream(payload);
        IFormFile file = new FormFile(stream, 0, payload.Length, "file", "large-structure.docx");

        var action = () => UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        action.Should().Throw<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage("文件结构过大，请拆分后重新上传");
    }

    [Fact]
    public void ValidateOfficeDocument_WhenOfficeZipEntryIsTooLarge_ShouldThrowFriendlyError()
    {
        var payload = CreateOfficeZip(extraEntries: new[]
        {
            ("word/media/oversized.bin", UploadFileValidation.MaxAllowedEntrySizeBytes + 1)
        });
        using var stream = new MemoryStream(payload);
        IFormFile file = new FormFile(stream, 0, payload.Length, "file", "large-entry.docx");

        var action = () => UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        action.Should().Throw<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage("文件结构过大，请拆分后重新上传");
    }

    [Fact]
    public void ValidateOfficeDocument_WhenExcelWorksheetDimensionExceedsBudget_ShouldRejectBeforeParsing()
    {
        var payload = CreateExcelZip("A1:XFD1048576");
        using var stream = new MemoryStream(payload);
        IFormFile file = new FormFile(stream, 0, payload.Length, "file", "oversized-dimension.xlsx");

        var action = () => UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        action.Should().Throw<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage("工作表维度超过系统预算，请拆分后重新上传");
    }

    [Fact]
    public void ValidateOfficeDocument_WhenWorksheetOmitsDimensionButCellReferenceExceedsBudget_ShouldReject()
    {
        var payload = CreateExcelZipWithWorksheet(
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"100001\"><c r=\"A100001\" /></row></sheetData></worksheet>");
        using var stream = new MemoryStream(payload);
        IFormFile file = new FormFile(stream, 0, payload.Length, "file", "missing-dimension.xlsx");

        var action = () => UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        action.Should().Throw<ApplicationServiceException>()
            .Where(ex => ex.Code == 400)
            .WithMessage("工作表维度超过系统预算，请拆分后重新上传");
    }

    private static byte[] CreateOfficeZip(
        int entryCount = 2,
        IReadOnlyCollection<(string Name, long Length)>? extraEntries = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", "<Types />"u8.ToArray());
            WriteEntry(archive, "word/document.xml", "<document />"u8.ToArray());

            for (var i = 0; i < entryCount - 2; i++)
            {
                WriteEntry(archive, $"word/extra-{i}.xml", "<x />"u8.ToArray());
            }

            if (extraEntries != null)
            {
                foreach (var (name, length) in extraEntries)
                {
                    WriteEntry(archive, name, length);
                }
            }
        }

        return stream.ToArray();
    }

    private static byte[] CreateExcelZip(string dimension)
    {
        return CreateExcelZipWithWorksheet(
            $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><dimension ref=\"{dimension}\" /></worksheet>");
    }

    private static byte[] CreateExcelZipWithWorksheet(string worksheetXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", "<Types />"u8.ToArray());
            WriteEntry(archive, "xl/workbook.xml", "<workbook />"u8.ToArray());
            WriteEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                Encoding.UTF8.GetBytes(worksheetXml));
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string name, long length)
    {
        var entry = archive.CreateEntry(name);
        using var entryStream = entry.Open();
        var buffer = new byte[8192];
        var remaining = length;
        while (remaining > 0)
        {
            var writeLength = (int)Math.Min(buffer.Length, remaining);
            entryStream.Write(buffer.AsSpan(0, writeLength));
            remaining -= writeLength;
        }
    }
}
