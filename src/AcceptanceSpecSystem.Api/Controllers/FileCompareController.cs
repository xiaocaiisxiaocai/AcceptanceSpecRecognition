using System.Security.Cryptography;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 文件对比API控制器
/// </summary>
[Route("api/file-compare")]
public class FileCompareController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly IFileCompareService _compareService;
    private readonly ILogger<FileCompareController> _logger;

    public FileCompareController(
        IUnitOfWork unitOfWork,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        IFileCompareService compareService,
        ILogger<FileCompareController> logger)
    {
        _unitOfWork = unitOfWork;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _compareService = compareService;
        _logger = logger;
    }

    /// <summary>
    /// 上传待对比文件（仅支持同类型 Word/Excel）
    /// </summary>
    [HttpPost("upload")]
    [AuditOperation("upload", "file-compare")]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileCompareUploadResponse>>> Upload(
        IFormFile fileA,
        IFormFile fileB)
    {
        if (fileA == null || fileA.Length == 0 || fileB == null || fileB.Length == 0)
        {
            return Error<FileCompareUploadResponse>(400, "请上传两份文件");
        }

        var extA = Path.GetExtension(fileA.FileName).ToLowerInvariant();
        var extB = Path.GetExtension(fileB.FileName).ToLowerInvariant();
        if (extA != extB)
        {
            return Error<FileCompareUploadResponse>(400, "仅支持同类型文件对比");
        }

        if (extA != ".docx" && extA != ".xlsx")
        {
            return Error<FileCompareUploadResponse>(400, "仅支持 .docx / .xlsx 格式");
        }
        var fileType = extA == ".docx"
            ? UploadedFileType.WordDocx
            : UploadedFileType.ExcelXlsx;

        var respA = await SaveUploadedFileAsync(fileA, fileType);
        var respB = await SaveUploadedFileAsync(fileB, fileType);

        return Success(new FileCompareUploadResponse
        {
            FileA = respA,
            FileB = respB
        }, "上传成功");
    }

    /// <summary>
    /// 获取对比预览
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<FileComparePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileComparePreviewResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileComparePreviewResponse>>> Preview([FromBody] FileComparePreviewRequest request)
    {
        if (request.FileIdA <= 0 || request.FileIdB <= 0)
            return Error<FileComparePreviewResponse>(400, "文件ID不能为空");

        var fileA = await _unitOfWork.WordFiles.GetByIdAsync(request.FileIdA);
        var fileB = await _unitOfWork.WordFiles.GetByIdAsync(request.FileIdB);
        if (fileA == null || fileB == null)
            return Error<FileComparePreviewResponse>(400, "文件不存在");

        if (fileA.FileType != fileB.FileType)
            return Error<FileComparePreviewResponse>(400, "仅支持同类型文件对比");

        var result = await _compareService.CompareAsync(fileA, fileB, HttpContext.RequestAborted);

        var response = ToPreviewResponse(result);
        return Success(response);
    }

    /// <summary>
    /// 下载对比结果（JSON）
    /// </summary>
    [HttpPost("download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Download([FromBody] FileComparePreviewRequest request)
    {
        if (request.FileIdA <= 0 || request.FileIdB <= 0)
            return BadRequest(ApiResponse.Error(400, "文件ID不能为空"));

        var fileA = await _unitOfWork.WordFiles.GetByIdAsync(request.FileIdA);
        var fileB = await _unitOfWork.WordFiles.GetByIdAsync(request.FileIdB);
        if (fileA == null || fileB == null)
            return BadRequest(ApiResponse.Error(400, "文件不存在"));

        if (fileA.FileType != fileB.FileType)
            return BadRequest(ApiResponse.Error(400, "仅支持同类型文件对比"));

        var result = await _compareService.CompareAsync(fileA, fileB, HttpContext.RequestAborted);
        var response = ToPreviewResponse(result);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        var filename = $"compare_{DateTime.Now:yyyyMMddHHmmss}.json";
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", filename);
    }

    private async Task<FileUploadResponse> SaveUploadedFileAsync(IFormFile file, UploadedFileType fileType)
    {
        byte[] fileContent;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream);
            fileContent = memoryStream.ToArray();
        }

        var fileHash = ComputeFileHash(fileContent);
        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(f => f.FileHash == fileHash);
        if (existingFile != null)
        {
            try
            {
                var needsWrite =
                    string.IsNullOrWhiteSpace(existingFile.FilePath) ||
                    !System.IO.File.Exists(_fileStorage.GetAbsolutePath(existingFile.FilePath));

                if (needsWrite)
                {
                    var newPath = existingFile.FileType == UploadedFileType.ExcelXlsx
                        ? await _fileStorage.SaveUploadedExcelAsync(existingFile.FileName, fileContent)
                        : await _fileStorage.SaveUploadedWordAsync(existingFile.FileName, fileContent);
                    existingFile.FilePath = newPath;
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重复文件落盘迁移失败: {FileId} - {FileName}", existingFile.Id, existingFile.FileName);
            }

            var tableCount = await GetTableCountAsync(fileType, fileContent);
            return new FileUploadResponse
            {
                FileId = existingFile.Id,
                FileName = existingFile.FileName,
                FileHash = existingFile.FileHash,
                IsDuplicate = true,
                TableCount = tableCount,
                FileType = existingFile.FileType
            };
        }

        var filePath = fileType == UploadedFileType.ExcelXlsx
            ? await _fileStorage.SaveUploadedExcelAsync(file.FileName, fileContent)
            : await _fileStorage.SaveUploadedWordAsync(file.FileName, fileContent);

        var wordFile = new WordFile
        {
            FileName = file.FileName,
            FileContent = Array.Empty<byte>(),
            FilePath = filePath,
            FileHash = fileHash,
            UploadedAt = DateTime.Now,
            FileType = fileType
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile);
        await _unitOfWork.SaveChangesAsync();

        var newTableCount = await GetTableCountAsync(fileType, fileContent);

        return new FileUploadResponse
        {
            FileId = wordFile.Id,
            FileName = wordFile.FileName,
            FileHash = wordFile.FileHash,
            IsDuplicate = false,
            TableCount = newTableCount,
            FileType = wordFile.FileType
        };
    }

    private async Task<int> GetTableCountAsync(UploadedFileType fileType, byte[] fileContent)
    {
        using var stream = new MemoryStream(fileContent);
        var parser = fileType == UploadedFileType.ExcelXlsx
            ? _documentServiceFactory.GetParser(DocumentType.Excel)
            : _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
            return 0;

        var tables = await parser.GetTablesAsync(stream);
        return tables.Count;
    }

    private static string ComputeFileHash(byte[] fileContent)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(fileContent);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FileComparePreviewResponse ToPreviewResponse(FileCompareResult result)
    {
        var items = result.Items.Select(i => new FileCompareDiffItemDto
        {
            DiffType = i.DiffType.ToString(),
            OriginalText = i.OriginalText,
            CurrentText = i.CurrentText,
            DisplayLocation = i.DisplayLocation,
            Location = new FileCompareLocationDto
            {
                DocumentType = i.Location.DocumentType,
                TableIndex = i.Location.TableIndex,
                SheetName = i.Location.SheetName,
                RowIndex = i.Location.RowIndex,
                ColumnIndex = i.Location.ColumnIndex,
                Address = i.Location.Address
            }
        }).ToList();
        var hunks = result.Hunks.Select(h => new FileCompareHunkDto
        {
            StartItemIndex = h.StartItemIndex,
            EndItemIndex = h.EndItemIndex,
            RangeText = h.RangeText,
            Lines = h.Lines.Select(line => new FileCompareHunkLineDto
            {
                LineType = line.LineType,
                ItemIndex = line.ItemIndex,
                ChangeGroupId = line.ChangeGroupId,
                DisplayLocation = line.DisplayLocation,
                OriginalText = line.OriginalText,
                CurrentText = line.CurrentText
            }).ToList()
        }).ToList();

        return new FileComparePreviewResponse
        {
            FileType = result.FileType,
            Items = items,
            Hunks = hunks,
            AddedCount = items.Count(i => i.DiffType == FileCompareDiffType.Added.ToString()),
            RemovedCount = items.Count(i => i.DiffType == FileCompareDiffType.Removed.ToString()),
            ModifiedCount = items.Count(i => i.DiffType == FileCompareDiffType.Modified.ToString()),
            UnchangedCount = items.Count(i => i.DiffType == FileCompareDiffType.Unchanged.ToString()),
            TotalCount = items.Count
        };
    }
}
