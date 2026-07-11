using System.IO.Compression;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Application;
using Microsoft.AspNetCore.Http;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// Office 文档上传校验。
/// </summary>
public static class UploadFileValidation
{
    public const int MaxAllowedFileSizeMegabytes = 50;
    public const long MaxAllowedFileSizeBytes = MaxAllowedFileSizeMegabytes * 1024L * 1024L;
    public const int MaxAllowedZipEntryCount = 1_500;
    public const long MaxAllowedUncompressedSizeBytes = 200L * 1024L * 1024L;
    public const long MaxAllowedEntrySizeBytes = 100L * 1024L * 1024L;

    public static UploadedFileType ValidateOfficeDocument(
        IFormFile file,
        bool allowExcel,
        bool allowWord)
    {
        if (file == null || file.Length == 0)
        {
            throw new ApplicationServiceException(400, "请选择要上传的文件");
        }

        if (file.Length > MaxAllowedFileSizeBytes)
        {
            throw new ApplicationServiceException(400, $"文件大小不能超过{MaxAllowedFileSizeMegabytes}MB");
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        UploadedFileType? fileType = extension switch
        {
            ".docx" when allowWord => UploadedFileType.WordDocx,
            ".xlsx" when allowExcel => UploadedFileType.ExcelXlsx,
            _ => null
        };

        if (!fileType.HasValue)
        {
            var message = allowExcel && allowWord
                ? "仅支持 .docx / .xlsx 格式"
                : allowWord
                    ? "仅支持 .docx 格式"
                    : "仅支持 .xlsx 格式";
            throw new ApplicationServiceException(400, message);
        }

        try
        {
            using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            EnsureOfficeZipStructureWithinLimits(archive);
            var entries = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var isValid = fileType.Value switch
            {
                UploadedFileType.WordDocx => entries.Contains("[Content_Types].xml") && entries.Contains("word/document.xml"),
                UploadedFileType.ExcelXlsx => entries.Contains("[Content_Types].xml") && entries.Contains("xl/workbook.xml"),
                _ => false
            };

            if (!isValid)
            {
                throw new ApplicationServiceException(400, "文件内容与扩展名不匹配或文件已损坏");
            }
        }
        catch (ApplicationServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
        {
            throw new ApplicationServiceException(400, "文件内容与扩展名不匹配或文件已损坏");
        }

        return fileType.Value;
    }

    private static void EnsureOfficeZipStructureWithinLimits(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxAllowedZipEntryCount)
        {
            throw OfficeZipTooLarge();
        }

        var totalUncompressedSize = 0L;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxAllowedEntrySizeBytes)
            {
                throw OfficeZipTooLarge();
            }

            if (totalUncompressedSize > MaxAllowedUncompressedSizeBytes - entry.Length)
            {
                throw OfficeZipTooLarge();
            }

            totalUncompressedSize += entry.Length;
            if (totalUncompressedSize > MaxAllowedUncompressedSizeBytes)
            {
                throw OfficeZipTooLarge();
            }

        }
    }

    private static ApplicationServiceException OfficeZipTooLarge()
    {
        return new ApplicationServiceException(400, "文件结构过大，请拆分后重新上传");
    }
}
