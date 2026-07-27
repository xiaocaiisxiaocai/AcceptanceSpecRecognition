using System.IO.Compression;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Application;
using Microsoft.AspNetCore.Http;
using System.Xml;

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
    public const int MaxAllowedWorksheetCount = 100;
    public const int MaxAllowedWorksheetRows = 100_000;
    public const int MaxAllowedWorksheetColumns = 512;
    public const long MaxAllowedWorksheetCells = 2_000_000;

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

        using var stream = file.OpenReadStream();
        return ValidateOfficeDocument(file.FileName, stream, allowExcel, allowWord);
    }

    public static UploadedFileType ValidateOfficeDocument(
        string fileName,
        Stream content,
        bool allowExcel,
        bool allowWord)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
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
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
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

            if (fileType.Value == UploadedFileType.ExcelXlsx)
            {
                EnsureWorksheetDimensionsWithinLimits(archive);
            }
        }
        catch (ApplicationServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or XmlException)
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

    private static void EnsureWorksheetDimensionsWithinLimits(ZipArchive archive)
    {
        var worksheets = archive.Entries
            .Where(entry => entry.FullName.Replace('\\', '/').StartsWith(
                "xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (worksheets.Count > MaxAllowedWorksheetCount)
        {
            throw WorksheetDimensionTooLarge();
        }

        foreach (var worksheet in worksheets)
        {
            using var stream = worksheet.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true
            });
            var rowElementCount = 0;
            long cellElementCount = 0;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName.Equals("dimension", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateDimensionReference(reader.GetAttribute("ref"));
                    continue;
                }

                if (reader.LocalName.Equals("row", StringComparison.OrdinalIgnoreCase))
                {
                    rowElementCount++;
                    var rowReference = reader.GetAttribute("r");
                    var rowIndex = int.TryParse(rowReference, out var parsedRow) ? parsedRow : rowElementCount;
                    if (rowIndex > MaxAllowedWorksheetRows || rowElementCount > MaxAllowedWorksheetRows)
                    {
                        throw WorksheetDimensionTooLarge();
                    }
                    continue;
                }

                if (reader.LocalName.Equals("c", StringComparison.OrdinalIgnoreCase))
                {
                    cellElementCount++;
                    if (cellElementCount > MaxAllowedWorksheetCells)
                    {
                        throw WorksheetDimensionTooLarge();
                    }
                    ValidateDimensionReference(reader.GetAttribute("r"));
                }
            }
        }
    }

    private static void ValidateDimensionReference(string? reference)
    {
        if (TryParseDimension(reference, out var rows, out var columns) &&
            (rows > MaxAllowedWorksheetRows ||
             columns > MaxAllowedWorksheetColumns ||
             (long)rows * columns > MaxAllowedWorksheetCells))
        {
            throw WorksheetDimensionTooLarge();
        }
    }

    private static bool TryParseDimension(string? reference, out int rows, out int columns)
    {
        rows = 0;
        columns = 0;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var lastCell = reference.Split(':', 2, StringSplitOptions.TrimEntries)[^1]
            .Replace("$", string.Empty, StringComparison.Ordinal);
        var letterCount = 0;
        while (letterCount < lastCell.Length && char.IsLetter(lastCell[letterCount]))
        {
            var value = char.ToUpperInvariant(lastCell[letterCount]) - 'A' + 1;
            if (value is < 1 or > 26 || columns > (int.MaxValue - value) / 26)
            {
                return false;
            }
            columns = columns * 26 + value;
            letterCount++;
        }

        return letterCount > 0 &&
               letterCount < lastCell.Length &&
               int.TryParse(lastCell.AsSpan(letterCount), out rows) &&
               rows > 0;
    }

    private static ApplicationServiceException WorksheetDimensionTooLarge()
    {
        return new ApplicationServiceException(400, "工作表维度超过系统预算，请拆分后重新上传");
    }
}
