using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

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
}
