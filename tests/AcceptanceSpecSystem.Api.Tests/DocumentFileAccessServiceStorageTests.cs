using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class DocumentFileAccessServiceStorageTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DocumentFileAccessServiceStorageTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PersistUpdatedFileContentAsync_WhenFilePathExists_ShouldKeepDatabaseBlobEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var fileAccessService = scope.ServiceProvider.GetRequiredService<DocumentFileAccessService>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var originalContent = "original-content"u8.ToArray();
        var relativePath = await fileStorage.SaveUploadedWordAsync("storage-path.docx", originalContent);
        var wordFile = new WordFile
        {
            FileName = "storage-path.docx",
            FileType = UploadedFileType.WordDocx,
            FilePath = relativePath,
            FileContent = Array.Empty<byte>(),
            FileHash = FileStorageService.ComputeSha256(originalContent)
        };
        var updatedContent = "updated-content"u8.ToArray();

        await fileAccessService.PersistUpdatedFileContentAsync(wordFile, updatedContent);

        wordFile.FileContent.Should().BeEmpty("文件系统存储模式下不应把整份文档重新写回数据库");
        wordFile.FileHash.Should().Be(FileStorageService.ComputeSha256(updatedContent));
        File.ReadAllBytes(fileStorage.GetAbsolutePath(relativePath)).Should().Equal(updatedContent);
    }

    [Fact]
    public async Task PersistUpdatedFileContentAsync_WhenFilePathMissing_ShouldCreateFileAndKeepDatabaseBlobEmpty()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var fileAccessService = scope.ServiceProvider.GetRequiredService<DocumentFileAccessService>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var updatedContent = "content-for-new-file"u8.ToArray();
        var wordFile = new WordFile
        {
            FileName = "storage-create.docx",
            FileType = UploadedFileType.WordDocx,
            FileContent = [1, 2, 3]
        };

        await fileAccessService.PersistUpdatedFileContentAsync(wordFile, updatedContent);

        wordFile.FilePath.Should().NotBeNullOrWhiteSpace();
        wordFile.FileContent.Should().BeEmpty("落盘后应清空兼容性的数据库二进制字段");
        wordFile.FileHash.Should().Be(FileStorageService.ComputeSha256(updatedContent));
        File.ReadAllBytes(fileStorage.GetAbsolutePath(wordFile.FilePath!)).Should().Equal(updatedContent);
    }
}
