using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

/// <summary>
/// WordFileRepository 单元测试
/// </summary>
public class WordFileRepositoryTests : TestBase
{
    private readonly WordFileRepository _repository;

    public WordFileRepositoryTests()
    {
        _repository = new WordFileRepository(Context);
    }

    [Fact]
    public async Task GetByHashAsync_ExistingHash_ShouldReturnFile()
    {
        // Arrange
        var file = new WordFile
        {
            FileName = "test.docx",
            FileHash = "abc123hash",
            FileType = UploadedFileType.WordDocx,
            UploadedAt = DateTime.UtcNow
        };
        Context.WordFiles.Add(file);
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByHashAsync("abc123hash");

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be("test.docx");
        result.FileHash.Should().Be("abc123hash");
    }

    [Fact]
    public async Task GetByHashAsync_NonExistingHash_ShouldReturnNull()
    {
        // Act
        var result = await _repository.GetByHashAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsByHashAsync_ExistingHash_ShouldReturnTrue()
    {
        // Arrange
        Context.WordFiles.Add(new WordFile
        {
            FileName = "test.docx",
            FileHash = "existinghash",
            FileType = UploadedFileType.WordDocx,
            UploadedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.ExistsByHashAsync("existinghash");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByHashAsync_NonExistingHash_ShouldReturnFalse()
    {
        // Act
        var result = await _repository.ExistsByHashAsync("nope");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllWithoutContentAsync_ShouldReturnFilesWithoutContent()
    {
        // Arrange
        Context.WordFiles.AddRange(
            new WordFile
            {
                FileName = "file1.docx",
                FileHash = "hash1",
                FileContent = new byte[] { 1, 2, 3 },
                FileType = UploadedFileType.WordDocx,
                UploadedAt = DateTime.UtcNow
            },
            new WordFile
            {
                FileName = "file2.xlsx",
                FileHash = "hash2",
                FileContent = new byte[] { 4, 5, 6 },
                FileType = UploadedFileType.ExcelXlsx,
                UploadedAt = DateTime.UtcNow
            });
        await Context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllWithoutContentAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(f =>
        {
            f.FileName.Should().NotBeNullOrEmpty();
            f.FileHash.Should().NotBeNullOrEmpty();
            // 投影后 FileContent 应为默认空数组
            f.FileContent.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Add_ShouldPersistWordFile()
    {
        // Arrange
        var file = new WordFile
        {
            FileName = "new-file.docx",
            FileHash = "newhash",
            FilePath = "uploads/2026-01/new-file.docx",
            FileType = UploadedFileType.WordDocx,
            CompanyId = 1,
            CreatedByUserId = 1,
            UploadedAt = DateTime.UtcNow
        };

        // Act
        await _repository.AddAsync(file);
        await Context.SaveChangesAsync();

        // Assert
        var saved = await _repository.GetByIdAsync(file.Id);
        saved.Should().NotBeNull();
        saved!.FileName.Should().Be("new-file.docx");
        saved.FilePath.Should().Be("uploads/2026-01/new-file.docx");
        saved.CompanyId.Should().Be(1);
    }
}
