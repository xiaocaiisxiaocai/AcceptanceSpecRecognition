using System.Text;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 文档处理API控制器
/// </summary>
[Route("api/documents")]
public class DocumentsController : BaseApiController
{
    private const string MatchTypeExact = "exact";
    private const string MatchTypeConflict = "conflict";
    private const string MatchTypeSemantic = "semantic";

    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly ImportDuplicateDetectionService _importDuplicateDetectionService;
    private readonly ILogger<DocumentsController> _logger;

    /// <summary>
    /// 创建文档控制器实例
    /// </summary>
    public DocumentsController(
        IUnitOfWork unitOfWork,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        IAuthDataScopeService authDataScopeService,
        ImportDuplicateDetectionService importDuplicateDetectionService,
        ILogger<DocumentsController> logger)
    {
        _unitOfWork = unitOfWork;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _authDataScopeService = authDataScopeService;
        _importDuplicateDetectionService = importDuplicateDetectionService;
        _logger = logger;
    }

    /// <summary>
    /// 获取已上传的文件列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<WordFileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<WordFileDto>>>> GetFiles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<PagedData<WordFileDto>>(401, "会话缺少用户上下文");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.WordFiles.Query()
            .Where(f => f.FileName != "__MANUAL_ENTRY__");
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(f => f.FileName.Contains(key));
        }

        query = ApplyWordFileScopeToQuery(query, scope);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(f => f.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id,
                f.FileName,
                f.FileType,
                f.FileHash,
                f.UploadedAt
            })
            .ToListAsync();

        var fileIds = rows.Select(f => f.Id).ToList();
        var specCountByFile = fileIds.Count == 0
            ? new Dictionary<int, int>()
            : await BuildSpecCountByFileAsync(fileIds, scope);

        var items = rows.Select(f => new WordFileDto
        {
            Id = f.Id,
            FileName = f.FileName,
            FileType = f.FileType,
            FileHash = f.FileHash,
            UploadedAt = f.UploadedAt,
            SpecCount = specCountByFile.TryGetValue(f.Id, out var count) ? count : 0
        }).ToList();

        var pagedData = new PagedData<WordFileDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(pagedData);
    }

    /// <summary>
    /// 上传文件（Word/Excel）
    /// 文件仅做临时保存，不做哈希去重，处理完后自动清理
    /// </summary>
    [HttpPost("upload")]
    [AuditOperation("upload", "document")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileUploadResponse>>> UploadFile(IFormFile file)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<FileUploadResponse>(401, "会话缺少用户上下文");
        }

        if (file == null || file.Length == 0)
        {
            return Error<FileUploadResponse>(400, "请选择要上传的文件");
        }

        // 检查文件类型
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".docx" && extension != ".xlsx")
        {
            return Error<FileUploadResponse>(400, "仅支持 .docx / .xlsx 格式");
        }

        var fileType = extension == ".xlsx" ? UploadedFileType.ExcelXlsx : UploadedFileType.WordDocx;
        var cancellationToken = HttpContext.RequestAborted;

        byte[] fileContent;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream, cancellationToken);
            fileContent = memoryStream.ToArray();
        }

        var fileHash = FileStorageService.ComputeSha256(fileContent);

        // 保存为临时文件（不做哈希去重）
        var filePath = fileType == UploadedFileType.ExcelXlsx
            ? await _fileStorage.SaveUploadedExcelAsync(file.FileName, fileContent, cancellationToken)
            : await _fileStorage.SaveUploadedWordAsync(file.FileName, fileContent, cancellationToken);

        var wordFile = new WordFile
        {
            CompanyId = scope.CompanyId,
            CreatedByUserId = scope.UserId,
            OwnerOrgUnitId = scope.OrgUnitId,
            FileName = file.FileName,
            FileContent = Array.Empty<byte>(),
            FilePath = filePath,
            FileHash = fileHash,
            UploadedAt = DateTime.UtcNow,
            FileType = fileType
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile);
        await _unitOfWork.SaveChangesAsync();

        // 获取表格数量
        var tableCount = 0;
        using (var stream = new MemoryStream(fileContent))
        {
            var parser = fileType == UploadedFileType.ExcelXlsx
                ? _documentServiceFactory.GetParser(DocumentType.Excel)
                : _documentServiceFactory.GetParser(DocumentType.Word);
            if (parser != null)
            {
                var tables = await parser.GetTablesAsync(stream);
                tableCount = tables.Count;
            }
        }

        _logger.LogInformation("文件临时上传成功: {FileId} - {FileName}", wordFile.Id, wordFile.FileName);

        return Success(new FileUploadResponse
        {
            FileId = wordFile.Id,
            FileName = wordFile.FileName,
            FileHash = wordFile.FileHash,
            IsDuplicate = false,
            TableCount = tableCount,
            FileType = wordFile.FileType
        }, "文件上传成功");
    }

    /// <summary>
    /// 获取文件中的表格列表
    /// </summary>
    [HttpGet("{id}/tables")]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTables(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<List<TableInfoDto>>(401, "会话缺少用户上下文");
        }

        var wordFile = await GetAccessibleWordFileAsync(id, scope);
        if (wordFile == null)
        {
            return NotFoundResult<List<TableInfoDto>>("文件不存在");
        }

        var parser = wordFile.FileType == UploadedFileType.ExcelXlsx
            ? _documentServiceFactory.GetParser(DocumentType.Excel)
            : _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return Error<List<TableInfoDto>>(500, "文档解析器不可用");
        }

        using var stream = OpenWordFileReadStream(wordFile);
        var tables = await parser.GetTablesAsync(stream);

        var result = tables.Select(t => new TableInfoDto
        {
            Index = t.Index,
            Name = t.Name,
            RowCount = t.RowCount,
            ColumnCount = t.ColumnCount,
            IsNested = t.IsNested,
            PreviewText = t.PreviewText,
            Headers = t.Headers?.ToList() ?? [],
            HasMergedCells = t.HasMergedCells,
            UsedRangeStartRow = t.UsedRangeStartRow,
            UsedRangeStartColumn = t.UsedRangeStartColumn
        }).ToList();

        return Success(result);
    }

    /// <summary>
    /// 获取表格数据预览
    /// </summary>
    [HttpGet("{id}/tables/{tableIndex}/preview")]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TableDataDto>>> GetTablePreview(
        int id,
        int tableIndex,
        [FromQuery] int previewRows = 0,
        [FromQuery] int headerRowIndex = 0,
        [FromQuery] int headerRowCount = 1,
        [FromQuery] int dataStartRowIndex = 1)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<TableDataDto>(401, "会话缺少用户上下文");
        }

        var wordFile = await GetAccessibleWordFileAsync(id, scope);
        if (wordFile == null)
        {
            return NotFoundResult<TableDataDto>("文件不存在");
        }

        var parser = wordFile.FileType == UploadedFileType.ExcelXlsx
            ? _documentServiceFactory.GetParser(DocumentType.Excel)
            : _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return Error<TableDataDto>(500, "文档解析器不可用");
        }

        var mapping = new ColumnMapping
        {
            HeaderRowIndex = headerRowIndex,
            HeaderRowCount = headerRowCount,
            DataStartRowIndex = dataStartRowIndex
        };

        using var stream = OpenWordFileReadStream(wordFile);
        TableData tableData;
        try
        {
            tableData = await parser.ExtractTableDataAsync(stream, tableIndex, mapping);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error<TableDataDto>(400, "表格索引超出范围");
        }

        // 转换为DTO：previewRows <= 0 时返回全部行，否则只返回指定预览行数
        var rowSource = previewRows <= 0 ? tableData.Rows : tableData.Rows.Take(previewRows);

        var rows = rowSource
            .Select(r => r.Cells.Select(FormatPreviewCellText).ToList())
            .ToList();

        var structuredRows = rowSource
            .Select(r => r.Cells.Select(c => MapStructuredCellValue(c.StructuredValue)).ToList())
            .ToList();

        var result = new TableDataDto
        {
            TableIndex = tableData.TableIndex,
            Headers = tableData.Headers.ToList(),
            Rows = rows,
            StructuredRows = structuredRows,
            TotalRows = tableData.Rows.Count,
            ColumnCount = tableData.ColumnCount
        };

        return Success(result);
    }

    private static StructuredCellValueDto MapStructuredCellValue(StructuredCellValue? value)
    {
        var dto = new StructuredCellValueDto();
        if (value?.Parts == null || value.Parts.Count == 0)
            return dto;

        dto.Parts = value.Parts.Select(MapStructuredPart).ToList();
        return dto;
    }

    private static StructuredCellPartDto MapStructuredPart(StructuredCellPart part)
    {
        return new StructuredCellPartDto
        {
            Type = part.Type,
            Text = part.Text,
            Table = part.Table == null ? null : MapStructuredTable(part.Table)
        };
    }

    private static StructuredTableValueDto MapStructuredTable(StructuredTableValue table)
    {
        return new StructuredTableValueDto
        {
            RowCount = table.RowCount,
            ColumnCount = table.ColumnCount,
            Rows = table.Rows.Select(r => r.Select(MapStructuredCellValue).ToList()).ToList()
        };
    }

    private static readonly System.Text.RegularExpressions.Regex ListPrefixRegex =
        new(@"^(?<indent>\s*)(?<num>\d+)\s*(?<sep>[、:：])(?<space>\s*)(?<rest>.*)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string FormatPreviewCellText(CellData cell)
    {
        var structuredText = ExtractStructuredText(cell.StructuredValue);
        var rawText = string.IsNullOrWhiteSpace(structuredText)
            ? cell.Value ?? string.Empty
            : structuredText;
        return AlignListPrefixes(rawText);
    }

    private static string ExtractStructuredText(StructuredCellValue? value)
    {
        if (value?.Parts == null || value.Parts.Count == 0)
            return string.Empty;

        var texts = value.Parts
            .Where(p => p.Type == "text" && !string.IsNullOrWhiteSpace(p.Text))
            .Select(p => p.Text!.TrimEnd());

        return string.Join("\n", texts);
    }

    private static string AlignListPrefixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');
        if (lines.Length < 2)
            return normalized;

        var items = new List<(bool HasPrefix, string Original, string Indent, string Num, string Sep, string Space, string Tail)>();
        foreach (var line in lines)
        {
            var match = ListPrefixRegex.Match(line);
            if (match.Success)
            {
                items.Add((
                    true,
                    line,
                    match.Groups["indent"].Value,
                    match.Groups["num"].Value,
                    match.Groups["sep"].Value,
                    match.Groups["space"].Value,
                    match.Groups["rest"].Value
                ));
            }
            else
            {
                items.Add((false, line, "", "", "", "", ""));
            }
        }

        var listItems = items.Where(i => i.HasPrefix).ToList();
        if (listItems.Count < 2)
            return normalized;

        var maxDigits = listItems.Max(i => i.Num.Length);
        var formatted = items.Select(i =>
        {
            if (!i.HasPrefix)
                return i.Original;

            var paddedNum = i.Num.PadLeft(maxDigits);
            return $"{i.Indent}{paddedNum}{i.Sep}{i.Space}{i.Tail}";
        });

        return string.Join("\n", formatted);
    }

    /// <summary>
    /// 导入表格数据到验收规格
    /// </summary>
    [HttpPost("import")]
    [AuditOperation("import", "document")]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResult>>> ImportData([FromBody] ImportDataRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<ImportResult>(401, "会话缺少用户上下文");

        var cancellationToken = HttpContext.RequestAborted;

        // 验证文件
        var wordFile = await GetAccessibleWordFileAsync(request.FileId, scope);
        if (wordFile == null)
        {
            return Error<ImportResult>(400, "文件不存在");
        }

        if (wordFile.FileType == UploadedFileType.ExcelXlsx)
        {
            return Error<ImportResult>(400, "该文件为 Excel，请使用 Excel 导入接口");
        }

        // 验证客户
        var customer = await _unitOfWork.Customers.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            return Error<ImportResult>(400, "客户不存在");
        }

        // 验证制程
        if (request.ProcessId.HasValue)
        {
            var process = await _unitOfWork.Processes.GetByIdAsync(request.ProcessId.Value);
            if (process == null)
            {
                return Error<ImportResult>(400, "制程不存在");
            }
        }

        // 验证机型
        if (request.MachineModelId.HasValue)
        {
            var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(request.MachineModelId.Value);
            if (machineModel == null)
            {
                return Error<ImportResult>(400, "机型不存在");
            }
        }

        // 验证列映射
        if (!request.Mapping.ProjectColumn.HasValue ||
            !request.Mapping.SpecificationColumn.HasValue ||
            !request.Mapping.AcceptanceColumn.HasValue ||
            !request.Mapping.RemarkColumn.HasValue)
        {
            return Error<ImportResult>(400, "项目列、规格列、验收标准列、备注列为必填");
        }

        var parser = _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return Error<ImportResult>(500, "文档解析器不可用");
        }

        // 提取表格数据
        var mapping = new ColumnMapping
        {
            ProjectColumn = request.Mapping.ProjectColumn,
            SpecificationColumn = request.Mapping.SpecificationColumn,
            AcceptanceColumn = request.Mapping.AcceptanceColumn,
            RemarkColumn = request.Mapping.RemarkColumn,
            HeaderRowIndex = request.Mapping.HeaderRowIndex,
            DataStartRowIndex = request.Mapping.DataStartRowIndex
        };

        TableData tableData;
        using (var stream = OpenWordFileReadStream(wordFile))
        {
            try
            {
                tableData = await parser.ExtractTableDataAsync(stream, request.TableIndex, mapping);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Error<ImportResult>(400, "表格索引超出范围");
            }
        }

        var result = new ImportResult
        {
            TotalCount = tableData.Rows.Count
        };
        var excludedRowIndexes = (request.ExcludedRowIndexes ?? [])
            .Where(index => index >= 0)
            .ToHashSet();
        if (excludedRowIndexes.Count > 0)
        {
            result.TotalCount = Math.Max(0, tableData.Rows.Count - tableData.Rows.Count(row => excludedRowIndexes.Contains(row.Index)));
        }

        try
        {
            var existingSpecsInScope = await LoadExistingSpecsForImportAsync(
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                scope,
                cancellationToken);
            var duplicateSession = await CreateDuplicateDetectionSessionAsync(
                existingSpecsInScope,
                request.ConfirmedDifferenceKeys,
                request.PartiallyConfirmedDifferenceKeys,
                request.SkippedDifferenceKeys,
                request.DuplicateCheckOptions,
                cancellationToken);
            var executionContext = CreateImportExecutionContext(
                result,
                existingSpecsInScope,
                request.ConfirmedDifferenceKeys,
                request.PartiallyConfirmedDifferenceKeys,
                request.SkippedDifferenceKeys,
                duplicateSession,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                request.FileId,
                scope.UserId,
                scope.OrgUnitId,
                request.PreviewSkippedRows);

            foreach (var row in tableData.Rows)
            {
                if (excludedRowIndexes.Contains(row.Index))
                {
                    continue;
                }

                try
                {
                    await ProcessImportRowAsync(
                        executionContext,
                        request.TableIndex,
                        new ImportRowPayload(
                            row.Index,
                            GetRowValues(row),
                            GetCellValue(row, request.Mapping.ProjectColumn!.Value),
                            GetCellValue(row, request.Mapping.SpecificationColumn!.Value),
                            request.Mapping.AcceptanceColumn.HasValue ? GetCellValue(row, request.Mapping.AcceptanceColumn.Value) : null,
                            request.Mapping.RemarkColumn.HasValue ? GetCellValue(row, request.Mapping.RemarkColumn.Value) : null),
                        cancellationToken);
                }
                catch (AiServiceUnavailableException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add(new ImportError
                    {
                        RowIndex = row.Index,
                        Message = ex.Message
                    });
                }
            }

            if (result.PendingCount > 0)
            {
                return Success(result, $"检测到{result.PendingCount}条重复或疑似重复数据，请逐条确认后再导入");
            }

            if (executionContext.SpecsToInsert.Count > 0 || executionContext.OverwriteCount > 0)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    if (executionContext.SpecsToInsert.Count > 0)
                    {
                        await _unitOfWork.AcceptanceSpecs.AddRangeAsync(executionContext.SpecsToInsert);
                    }

                    await _unitOfWork.SaveChangesAsync();
                    await _unitOfWork.CommitTransactionAsync();
                    result.SuccessCount = executionContext.SpecsToInsert.Count + executionContext.OverwriteCount;
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }

            // 导入完成后按需清理源文件（多表格分批导入时仅最后一次清理）
            if (request.CleanupSourceFile)
            {
                try
                {
                    await _fileStorage.DeleteIfExistsAsync(wordFile.FilePath);
                    wordFile.FilePath = null;
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "导入后清理源文件失败: fileId={FileId}", request.FileId);
                }
            }

            _logger.LogInformation(
                "导入完成: 文件{FileId}, 表格{TableIndex}, 客户{CustomerId}, 制程{ProcessId}, 机型{MachineModelId}, 成功{Success}, 失败{Failed}, 跳过{Skipped}",
                request.FileId, request.TableIndex, request.CustomerId, request.ProcessId, request.MachineModelId, result.SuccessCount, result.FailedCount, result.SkippedCount);

            return Success(result, $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条");
        }
        catch (AiServiceUnavailableException ex)
        {
            return Error<ImportResult>(400, BuildAiImportUnavailableMessage(request.DuplicateCheckOptions, ex));
        }
    }

    /// <summary>
    /// 删除已上传的文件
    /// </summary>
    /// <summary>
    /// Excel 导入：按列序号配置导入（列号/行号均为 1-based）
    /// </summary>
    [HttpPost("excel/import")]
    [AuditOperation("import", "excel-document")]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResult>>> ImportExcelData([FromBody] ExcelImportDataRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<ImportResult>(401, "会话缺少用户上下文");

        var cancellationToken = HttpContext.RequestAborted;

        var file = await GetAccessibleWordFileAsync(request.FileId, scope);
        if (file == null)
        {
            return Error<ImportResult>(400, "文件不存在");
        }

        if (file.FileType != UploadedFileType.ExcelXlsx)
        {
            return Error<ImportResult>(400, "该文件不是 Excel（.xlsx）");
        }

        var customer = await _unitOfWork.Customers.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            return Error<ImportResult>(400, "客户不存在");
        }

        if (request.ProcessId.HasValue)
        {
            var process = await _unitOfWork.Processes.GetByIdAsync(request.ProcessId.Value);
            if (process == null)
            {
                return Error<ImportResult>(400, "制程不存在");
            }
        }

        if (request.MachineModelId.HasValue)
        {
            var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(request.MachineModelId.Value);
            if (machineModel == null)
            {
                return Error<ImportResult>(400, "机型不存在");
            }
        }

        if (request.ProjectColumn <= 0 || request.SpecificationColumn <= 0)
        {
            return Error<ImportResult>(400, "项目列与规格内容列为必填，且列号必须 >= 1");
        }

        if (request.HeaderRowStart < 1 || request.HeaderRowCount < 0 || request.DataStartRow < 1)
        {
            return Error<ImportResult>(400, "表头行与数据起始行配置不合法");
        }

        var parser = _documentServiceFactory.GetParser(DocumentType.Excel);
        if (parser == null)
        {
            return Error<ImportResult>(500, "文档解析器不可用");
        }

        // 获取工作表信息，用于边界校验（已用区域）
        IReadOnlyList<TableInfo> tables;
        using (var stream = OpenWordFileReadStream(file))
        {
            tables = await parser.GetTablesAsync(stream);
        }

        if (request.SheetIndex < 0 || request.SheetIndex >= tables.Count)
        {
            return Error<ImportResult>(400, "工作表索引超出范围");
        }

        var sheetInfo = tables[request.SheetIndex];
        if (sheetInfo.RowCount <= 0 || sheetInfo.ColumnCount <= 0)
        {
            return Success(new ImportResult(), "工作表为空，无可导入数据");
        }

        var usedStartCol = sheetInfo.UsedRangeStartColumn;
        var usedStartRow = sheetInfo.UsedRangeStartRow;
        var usedEndCol = usedStartCol + sheetInfo.ColumnCount - 1;
        var usedEndRow = usedStartRow + sheetInfo.RowCount - 1;

        // 列越界校验（按 Excel 绝对列号）
        bool IsInUsedCols(int col) => col >= usedStartCol && col <= usedEndCol;

        if (!IsInUsedCols(request.ProjectColumn))
            return Error<ImportResult>(400, $"列号越界：ProjectColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
        if (!IsInUsedCols(request.SpecificationColumn))
            return Error<ImportResult>(400, $"列号越界：SpecificationColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
        if (request.AcceptanceColumn.HasValue && !IsInUsedCols(request.AcceptanceColumn.Value))
            return Error<ImportResult>(400, $"列号越界：AcceptanceColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
        if (request.RemarkColumn.HasValue && !IsInUsedCols(request.RemarkColumn.Value))
            return Error<ImportResult>(400, $"列号越界：RemarkColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");

        if (request.DataStartRow > usedEndRow)
        {
            return Error<ImportResult>(400, $"数据起始行超出已用区域：{request.DataStartRow} > {usedEndRow}");
        }

        // 解析数据区：以 UsedRange 作为列范围，行从 DataStartRow 开始读到 UsedRange 末尾
        var mapping = new ColumnMapping
        {
            HeaderRowIndex = Math.Max(0, request.HeaderRowStart - usedStartRow),
            HeaderRowCount = Math.Max(1, request.HeaderRowCount == 0 ? 1 : request.HeaderRowCount),
            DataStartRowIndex = Math.Max(0, request.DataStartRow - usedStartRow)
        };

        TableData tableData;
        using (var stream = OpenWordFileReadStream(file))
        {
            tableData = await parser.ExtractTableDataAsync(stream, request.SheetIndex, mapping);
        }

        int ToLocalColIndex(int col1Based) => col1Based - usedStartCol;

        var projectCol = ToLocalColIndex(request.ProjectColumn);
        var specCol = ToLocalColIndex(request.SpecificationColumn);
        var acceptanceCol = request.AcceptanceColumn.HasValue ? ToLocalColIndex(request.AcceptanceColumn.Value) : (int?)null;
        var remarkCol = request.RemarkColumn.HasValue ? ToLocalColIndex(request.RemarkColumn.Value) : (int?)null;

        var result = new ImportResult
        {
            TotalCount = tableData.Rows.Count
        };
        var excludedRowIndexes = (request.ExcludedRowIndexes ?? [])
            .Where(index => index >= 0)
            .ToHashSet();
        if (excludedRowIndexes.Count > 0)
        {
            result.TotalCount = Math.Max(0, tableData.Rows.Count - tableData.Rows.Count(row => excludedRowIndexes.Contains(row.Index)));
        }

        try
        {
            var existingSpecsInScope = await LoadExistingSpecsForImportAsync(
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                scope,
                cancellationToken);
            var duplicateSession = await CreateDuplicateDetectionSessionAsync(
                existingSpecsInScope,
                request.ConfirmedDifferenceKeys,
                request.PartiallyConfirmedDifferenceKeys,
                request.SkippedDifferenceKeys,
                request.DuplicateCheckOptions,
                cancellationToken);
            var executionContext = CreateImportExecutionContext(
                result,
                existingSpecsInScope,
                request.ConfirmedDifferenceKeys,
                request.PartiallyConfirmedDifferenceKeys,
                request.SkippedDifferenceKeys,
                duplicateSession,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                request.FileId,
                scope.UserId,
                scope.OrgUnitId,
                request.PreviewSkippedRows);

            foreach (var row in tableData.Rows)
            {
                if (excludedRowIndexes.Contains(row.Index))
                {
                    continue;
                }

                var excelRowNumber = request.DataStartRow + row.Index;

                try
                {
                    await ProcessImportRowAsync(
                        executionContext,
                        request.SheetIndex,
                        new ImportRowPayload(
                            excelRowNumber,
                            GetRowValues(row),
                            GetCellValue(row, projectCol),
                            GetCellValue(row, specCol),
                            acceptanceCol.HasValue ? GetCellValue(row, acceptanceCol.Value) : null,
                            remarkCol.HasValue ? GetCellValue(row, remarkCol.Value) : null),
                        cancellationToken);
                }
                catch (AiServiceUnavailableException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    result.Errors.Add(new ImportError
                    {
                        RowIndex = excelRowNumber,
                        Message = ex.Message
                    });
                }
            }

            if (result.PendingCount > 0)
            {
                return Success(result, $"检测到{result.PendingCount}条重复或疑似重复数据，请逐条确认后再导入");
            }

            if (executionContext.SpecsToInsert.Count > 0 || executionContext.OverwriteCount > 0)
            {
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    if (executionContext.SpecsToInsert.Count > 0)
                    {
                        await _unitOfWork.AcceptanceSpecs.AddRangeAsync(executionContext.SpecsToInsert);
                    }

                    await _unitOfWork.SaveChangesAsync();
                    await _unitOfWork.CommitTransactionAsync();
                    result.SuccessCount = executionContext.SpecsToInsert.Count + executionContext.OverwriteCount;
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }

            // 导入完成后按需清理源文件（多工作表分批导入时仅最后一次清理）
            if (request.CleanupSourceFile)
            {
                try
                {
                    await _fileStorage.DeleteIfExistsAsync(file.FilePath);
                    file.FilePath = null;
                    await _unitOfWork.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Excel导入后清理源文件失败: fileId={FileId}", request.FileId);
                }
            }

            return Success(result, $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条");
        }
        catch (AiServiceUnavailableException ex)
        {
            return Error<ImportResult>(400, BuildAiImportUnavailableMessage(request.DuplicateCheckOptions, ex));
        }
    }

    [HttpDelete("{id}")]
    [AuditOperation("delete", "document")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteFile(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error(401, "会话缺少用户上下文");
        }

        var wordFile = await GetAccessibleWordFileAsync(id, scope);
        if (wordFile == null)
        {
            return NotFound(ApiResponse.Error(404, "文件不存在"));
        }

        // 检查是否有关联的规格
        var hasSpecs = await _unitOfWork.AcceptanceSpecs.AnyAsync(s => s.WordFileId == id);
        if (hasSpecs)
        {
            return Error(400, "该文件已有关联的验收规格，无法删除");
        }

        _unitOfWork.WordFiles.Remove(wordFile);
        await _unitOfWork.SaveChangesAsync();

        // 删除物理文件（文件系统存储）
        await _fileStorage.DeleteIfExistsAsync(wordFile.FilePath);

        _logger.LogInformation("删除文件成功: {FileId} - {FileName}", wordFile.Id, wordFile.FileName);

        return Success("删除成功");
    }

    private async Task<Dictionary<int, int>> BuildSpecCountByFileAsync(List<int> fileIds, DataScopeResult scope)
    {
        var specsQuery = _unitOfWork.AcceptanceSpecs.Query()
            .Where(s => fileIds.Contains(s.WordFileId));

        if (!scope.IsAll)
        {
            var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
            if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
            {
                specsQuery = specsQuery.Where(s =>
                    (s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == scope.UserId) ||
                    (s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value)));
            }
            else if (scope.IncludeSelf)
            {
                specsQuery = specsQuery.Where(s =>
                    s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == scope.UserId);
            }
            else if (scopedOrgUnitIds.Length > 0)
            {
                specsQuery = specsQuery.Where(s =>
                    s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value));
            }
            else
            {
                return [];
            }
        }

        return await specsQuery
            .GroupBy(s => s.WordFileId)
            .Select(g => new { FileId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FileId, x => x.Count);
    }

    private IQueryable<WordFile> ApplyWordFileScopeToQuery(IQueryable<WordFile> query, DataScopeResult scope)
    {
        var ownershipQuery = WordFileDataScopeHelper.ApplyOwnershipScopeToQuery(query, scope);
        if (scope.IsAll)
        {
            return ownershipQuery;
        }

        var scopedSpecFileIds = SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(),
                scope)
            .Select(spec => spec.WordFileId)
            .Distinct();

        return ownershipQuery.Union(query.Where(file => scopedSpecFileIds.Contains(file.Id)));
    }

    private async Task<WordFile?> GetAccessibleWordFileAsync(int id, DataScopeResult scope)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(id);
        if (wordFile == null)
        {
            return null;
        }

        if (WordFileDataScopeHelper.CanAccess(wordFile, scope))
        {
            return wordFile;
        }

        var hasScopedSpec = await SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(),
                scope)
            .AnyAsync(spec => spec.WordFileId == id);

        return hasScopedSpec ? wordFile : null;
    }

    private async Task<List<AcceptanceSpec>> LoadExistingSpecsForImportAsync(
        int customerId,
        int? processId,
        int? machineModelId,
        DataScopeResult scope,
        CancellationToken cancellationToken)
    {
        return await SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(asNoTracking: false),
                scope)
            .Where(s =>
                s.CustomerId == customerId &&
                s.ProcessId == processId &&
                s.MachineModelId == machineModelId)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<ImportDuplicateDetectionSession> CreateDuplicateDetectionSessionAsync(
        IReadOnlyCollection<AcceptanceSpec> existingSpecs,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys,
        ImportDuplicateCheckOptions? options,
        CancellationToken cancellationToken)
    {
        if (HasReplayDifferenceDecisions(
                confirmedDifferenceKeys,
                partiallyConfirmedDifferenceKeys,
                skippedDifferenceKeys))
        {
            _logger.LogInformation("检测到已确认的导入差异决策，本次确认提交跳过 AI/Embedding 重复检测");
            return ImportDuplicateDetectionSession.Disabled(new ImportDuplicateCheckOptions());
        }

        return await _importDuplicateDetectionService.CreateSessionAsync(
            existingSpecs,
            options,
            cancellationToken);
    }

    private static bool HasReplayDifferenceDecisions(
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys)
    {
        return HasAnyDifferenceDecision(confirmedDifferenceKeys) ||
               HasAnyDifferenceDecision(partiallyConfirmedDifferenceKeys) ||
               HasAnyDifferenceDecision(skippedDifferenceKeys);
    }

    private static bool HasAnyDifferenceDecision(IEnumerable<string>? keys)
    {
        return keys?.Any(key => !string.IsNullOrWhiteSpace(key)) == true;
    }

    private static ImportExecutionContext CreateImportExecutionContext(
        ImportResult result,
        List<AcceptanceSpec> existingSpecs,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys,
        ImportDuplicateDetectionSession duplicateSession,
        int customerId,
        int? processId,
        int? machineModelId,
        int fileId,
        int userId,
        int? ownerOrgUnitId,
        bool previewSkippedRows)
    {
        return new ImportExecutionContext
        {
            PendingDecisionMap = BuildPendingDecisionMap(
                confirmedDifferenceKeys,
                partiallyConfirmedDifferenceKeys,
                skippedDifferenceKeys),
            Result = result,
            ExistingSpecs = existingSpecs,
            PendingInsertedSpecs = [],
            SpecsToInsert = [],
            ConfirmedDifferenceKeys = (confirmedDifferenceKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal),
            PartiallyConfirmedDifferenceKeys = (partiallyConfirmedDifferenceKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal),
            SkippedDifferenceKeys = (skippedDifferenceKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal),
            DuplicateSession = duplicateSession,
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            FileId = fileId,
            UserId = userId,
            OwnerOrgUnitId = ownerOrgUnitId,
            PreviewSkippedRows = previewSkippedRows
        };
    }

    private async Task ProcessImportRowAsync(
        ImportExecutionContext context,
        int tableIndex,
        ImportRowPayload row,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.Project) || string.IsNullOrWhiteSpace(row.Specification))
        {
            AddSkippedRow(
                context,
                row.RowIndex,
                string.IsNullOrWhiteSpace(row.Project) && string.IsNullOrWhiteSpace(row.Specification)
                    ? "项目列与规格列均为空"
                    : string.IsNullOrWhiteSpace(row.Project)
                        ? "项目列为空"
                        : "规格列为空",
                row.RowValues);
            return;
        }

        var normalizedProject = NormalizeText(row.Project);
        var normalizedSpecification = NormalizeText(row.Specification);
        var normalizedAcceptance = NormalizeText(row.Acceptance);
        var normalizedRemark = NormalizeText(row.Remark);

        if (TryApplyExplicitPendingDecision(
                context,
                tableIndex,
                row,
                normalizedProject,
                normalizedSpecification,
                normalizedAcceptance,
                normalizedRemark))
        {
            return;
        }

        var exactExisting = context.ExistingSpecs.FirstOrDefault(spec =>
            IsSameContent(spec, normalizedProject, normalizedSpecification, normalizedAcceptance, normalizedRemark));
        if (exactExisting != null)
        {
            var diffKey = BuildDifferenceKey(
                tableIndex,
                row.RowIndex,
                MatchTypeExact,
                exactExisting.Id,
                normalizedProject,
                normalizedSpecification,
                normalizedAcceptance,
                normalizedRemark);
            if (await TryApplyPendingDecisionAsync(
                    context,
                    row,
                    diffKey,
                    MatchTypeExact,
                    exactExisting,
                    null,
                    cancellationToken))
            {
                return;
            }

            AddPendingDifference(
                context,
                row,
                diffKey,
                MatchTypeExact,
                exactExisting,
                null);
            return;
        }

        var inBatchExact = context.PendingInsertedSpecs.FirstOrDefault(spec =>
            IsSameContent(spec, normalizedProject, normalizedSpecification, normalizedAcceptance, normalizedRemark));
        if (inBatchExact != null)
        {
            AddSkippedRow(context, row.RowIndex, "本次待导入数据中已存在完全相同内容，已自动保留首条", row.RowValues);
            return;
        }

        var projectConflict = context.ExistingSpecs.FirstOrDefault(spec =>
            HasSameProjectAndSpecification(spec, normalizedProject, normalizedSpecification));
        if (projectConflict != null)
        {
            var diffKey = BuildDifferenceKey(
                tableIndex,
                row.RowIndex,
                MatchTypeConflict,
                projectConflict.Id,
                normalizedProject,
                normalizedSpecification,
                normalizedAcceptance,
                normalizedRemark);
            if (await TryApplyPendingDecisionAsync(
                    context,
                    row,
                    diffKey,
                    MatchTypeConflict,
                    projectConflict,
                    null,
                    cancellationToken))
            {
                return;
            }

            AddPendingDifference(
                context,
                row,
                diffKey,
                MatchTypeConflict,
                projectConflict,
                null);
            return;
        }

        var inBatchConflict = context.PendingInsertedSpecs.FirstOrDefault(spec =>
            HasSameProjectAndSpecification(spec, normalizedProject, normalizedSpecification));
        if (inBatchConflict != null)
        {
            AddSkippedRow(context, row.RowIndex, "本次待导入数据中已存在相同项目与规格，已自动保留首条", row.RowValues);
            return;
        }

        if (context.DuplicateSession.IsEnabled && !context.SkipSemanticDetection)
        {
            var semanticMatch = await context.DuplicateSession.DetectAsync(
                normalizedProject,
                normalizedSpecification,
                cancellationToken);
            if (semanticMatch != null)
            {
                var diffKey = BuildDifferenceKey(
                    tableIndex,
                    row.RowIndex,
                    MatchTypeSemantic,
                    semanticMatch.ExistingSpec.Id,
                    normalizedProject,
                    normalizedSpecification,
                    normalizedAcceptance,
                    normalizedRemark);
                if (await TryApplyPendingDecisionAsync(
                        context,
                        row,
                        diffKey,
                        MatchTypeSemantic,
                        semanticMatch.ExistingSpec,
                        semanticMatch,
                        cancellationToken))
                {
                    return;
                }

                AddPendingDifference(
                    context,
                    row,
                    diffKey,
                    MatchTypeSemantic,
                    semanticMatch.ExistingSpec,
                    semanticMatch);
                return;
            }
        }

        var spec = CreateAcceptanceSpec(
            context.CustomerId,
            context.ProcessId,
            context.MachineModelId,
            context.FileId,
            row.Project,
            row.Specification,
            row.Acceptance,
            row.Remark,
            context.UserId,
            context.OwnerOrgUnitId);
        context.SpecsToInsert.Add(spec);
        context.PendingInsertedSpecs.Add(spec);
    }

    private static bool TryApplyExplicitPendingDecision(
        ImportExecutionContext context,
        int tableIndex,
        ImportRowPayload row,
        string normalizedProject,
        string normalizedSpecification,
        string normalizedAcceptance,
        string normalizedRemark)
    {
        if (context.PendingDecisionMap.Count == 0)
        {
            return false;
        }

        var decisionKey = BuildPendingDecisionLookupKey(
            tableIndex,
            row.RowIndex,
            normalizedProject,
            normalizedSpecification,
            normalizedAcceptance,
            normalizedRemark);

        if (!context.PendingDecisionMap.TryGetValue(decisionKey, out var decision))
        {
            return false;
        }

        var existingSpec = context.ExistingSpecs.FirstOrDefault(spec => spec.Id == decision.ExistingSpecId);
        if (existingSpec == null)
        {
            throw new InvalidOperationException("已确认的重复项对应记录不存在，请重新发起导入");
        }

        if (decision.Decision == DifferenceDecision.Import)
        {
            OverwriteAcceptanceSpec(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Project,
                row.Specification,
                row.Acceptance,
                row.Remark);
            context.OverwriteCount++;
            return true;
        }

        if (decision.Decision == DifferenceDecision.PartialImport)
        {
            OverwriteAcceptanceAndRemark(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Acceptance,
                row.Remark);
            context.OverwriteCount++;
            return true;
        }

        AddSkippedRow(context, row.RowIndex, GetSkippedMessage(decision.MatchType), row.RowValues);
        return true;
    }

    private async Task<bool> TryApplyPendingDecisionAsync(
        ImportExecutionContext context,
        ImportRowPayload row,
        string diffKey,
        string matchType,
        AcceptanceSpec existingSpec,
        ImportSemanticDuplicateMatch? semanticMatch,
        CancellationToken cancellationToken)
    {
        if (context.ConfirmedDifferenceKeys.Contains(diffKey))
        {
            var searchTextChanged =
                NormalizeText(existingSpec.Project) != NormalizeText(row.Project) ||
                NormalizeText(existingSpec.Specification) != NormalizeText(row.Specification);

            OverwriteAcceptanceSpec(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Project,
                row.Specification,
                row.Acceptance,
                row.Remark);

            if (searchTextChanged && !context.SkipSemanticDetection)
            {
                await context.DuplicateSession.RefreshCandidateAsync(existingSpec, cancellationToken);
            }

            context.OverwriteCount++;
            return true;
        }

        if (context.PartiallyConfirmedDifferenceKeys.Contains(diffKey))
        {
            OverwriteAcceptanceAndRemark(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Acceptance,
                row.Remark);
            context.OverwriteCount++;
            return true;
        }

        if (context.SkippedDifferenceKeys.Contains(diffKey))
        {
            AddSkippedRow(context, row.RowIndex, GetSkippedMessage(matchType), row.RowValues);
            return true;
        }

        return false;
    }

    private static void AddPendingDifference(
        ImportExecutionContext context,
        ImportRowPayload row,
        string diffKey,
        string matchType,
        AcceptanceSpec existingSpec,
        ImportSemanticDuplicateMatch? semanticMatch)
    {
        context.Result.RequiresConfirmation = true;
        context.Result.PendingCount++;
        context.Result.PendingDifferences.Add(new ImportPendingDifference
        {
            Key = diffKey,
            MatchType = matchType,
            RowIndex = row.RowIndex,
            RowValues = row.RowValues,
            IncomingProject = NormalizeText(row.Project),
            IncomingSpecification = NormalizeText(row.Specification),
            IncomingAcceptance = NormalizeNullable(row.Acceptance),
            IncomingRemark = NormalizeNullable(row.Remark),
            ExistingSpecId = existingSpec.Id,
            ExistingProject = existingSpec.Project,
            ExistingSpecification = existingSpec.Specification,
            ExistingAcceptance = existingSpec.Acceptance,
            ExistingRemark = existingSpec.Remark,
            EmbeddingScore = semanticMatch?.EmbeddingScore,
            LlmScore = semanticMatch?.LlmScore,
            FinalScore = semanticMatch?.FinalScore,
            IsHighConfidence = semanticMatch?.IsHighConfidence ?? false,
            ReviewReason = semanticMatch?.ReviewReason,
            ReviewCommentary = semanticMatch?.ReviewCommentary
        });
    }

    private static void AddSkippedRow(
        ImportExecutionContext context,
        int rowIndex,
        string message,
        List<string> rowValues)
    {
        context.Result.SkippedCount++;
        if (!context.PreviewSkippedRows)
        {
            return;
        }

        context.Result.SkippedRows.Add(new ImportSkippedRow
        {
            RowIndex = rowIndex,
            Message = message,
            RowValues = rowValues
        });
    }

    private static string BuildAiImportUnavailableMessage(
        ImportDuplicateCheckOptions? options,
        AiServiceUnavailableException ex)
    {
        var details = ex.Details.Count > 0
            ? $" 详细信息：{string.Join("；", ex.Details)}"
            : string.Empty;

        if (options?.EnableSemanticDuplicateCheck != true)
        {
            return $"AI 服务不可用：{ex.Reason}{details}";
        }

        if (options.EnableLlmDuplicateReview && ex.Reason.Contains("LLM", StringComparison.OrdinalIgnoreCase))
        {
            return $"AI 重复复核不可用，请关闭 LLM 复核或检查 AI 服务配置后重试：{ex.Reason}{details}";
        }

        return $"AI 疑似重复识别不可用，请关闭 AI 模式后重试或检查 Embedding 服务配置：{ex.Reason}{details}";
    }

    private static string GetSkippedMessage(string matchType)
    {
        return matchType switch
        {
            MatchTypeExact => "完全重复数据已确认跳过",
            MatchTypeSemantic => "AI 疑似重复数据已确认跳过",
            _ => "差异数据已确认跳过"
        };
    }

    private static bool HasSameProjectAndSpecification(
        AcceptanceSpec spec,
        string normalizedProject,
        string normalizedSpecification)
    {
        return NormalizeText(spec.Project) == normalizedProject &&
               NormalizeText(spec.Specification) == normalizedSpecification;
    }

    private static Dictionary<string, PendingDecisionEntry> BuildPendingDecisionMap(
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys)
    {
        var result = new Dictionary<string, PendingDecisionEntry>(StringComparer.Ordinal);

        foreach (var key in confirmedDifferenceKeys ?? [])
        {
            if (TryParsePendingDecisionEntry(key, DifferenceDecision.Import, out var entry))
            {
                result[entry.LookupKey] = entry;
            }
        }

        foreach (var key in partiallyConfirmedDifferenceKeys ?? [])
        {
            if (TryParsePendingDecisionEntry(key, DifferenceDecision.PartialImport, out var entry))
            {
                result[entry.LookupKey] = entry;
            }
        }

        foreach (var key in skippedDifferenceKeys ?? [])
        {
            if (TryParsePendingDecisionEntry(key, DifferenceDecision.Skip, out var entry))
            {
                result[entry.LookupKey] = entry;
            }
        }

        return result;
    }

    private static bool TryParsePendingDecisionEntry(
        string encodedKey,
        DifferenceDecision decision,
        out PendingDecisionEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            return false;
        }

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(encodedKey));
        }
        catch
        {
            return false;
        }

        if (!TryReadNextSegment(raw, 0, out var tableIndexText, out var cursor) ||
            !int.TryParse(tableIndexText, out var tableIndex) ||
            !TryReadNextSegment(raw, cursor, out var rowIndexText, out cursor) ||
            !int.TryParse(rowIndexText, out var rowIndex) ||
            !TryReadNextSegment(raw, cursor, out var matchType, out cursor) ||
            !TryReadNextSegment(raw, cursor, out var specIdText, out cursor) ||
            !int.TryParse(specIdText, out var existingSpecId))
        {
            return false;
        }

        var contentPayload = cursor <= raw.Length ? raw[cursor..] : string.Empty;
        entry = new PendingDecisionEntry
        {
            LookupKey = BuildPendingDecisionLookupKey(tableIndex, rowIndex, contentPayload),
            MatchType = matchType,
            ExistingSpecId = existingSpecId,
            Decision = decision
        };
        return true;
    }

    private static bool TryReadNextSegment(
        string value,
        int startIndex,
        out string segment,
        out int nextIndex)
    {
        segment = string.Empty;
        nextIndex = startIndex;
        if (startIndex > value.Length)
        {
            return false;
        }

        var separatorIndex = value.IndexOf('|', startIndex);
        if (separatorIndex < 0)
        {
            return false;
        }

        segment = value[startIndex..separatorIndex];
        nextIndex = separatorIndex + 1;
        return true;
    }

    private static string BuildPendingDecisionLookupKey(
        int tableIndex,
        int rowIndex,
        string normalizedProject,
        string normalizedSpecification,
        string normalizedAcceptance,
        string normalizedRemark)
    {
        return $"{tableIndex}|{rowIndex}|{normalizedProject}|{normalizedSpecification}|{normalizedAcceptance}|{normalizedRemark}";
    }

    private static string BuildPendingDecisionLookupKey(int tableIndex, int rowIndex, string contentPayload)
    {
        return $"{tableIndex}|{rowIndex}|{contentPayload}";
    }

    private static void OverwriteAcceptanceSpec(
        AcceptanceSpec existingSpec,
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        string? project,
        string? specification,
        string? acceptance,
        string? remark)
    {
        existingSpec.CustomerId = customerId;
        existingSpec.ProcessId = processId;
        existingSpec.MachineModelId = machineModelId;
        existingSpec.Project = project?.Trim() ?? string.Empty;
        existingSpec.Specification = specification?.Trim() ?? string.Empty;
        existingSpec.Acceptance = NormalizeNullable(acceptance);
        existingSpec.Remark = NormalizeNullable(remark);
        existingSpec.WordFileId = wordFileId;
        existingSpec.ImportedAt = DateTime.UtcNow;
    }

    private static void OverwriteAcceptanceAndRemark(
        AcceptanceSpec existingSpec,
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        string? acceptance,
        string? remark)
    {
        existingSpec.CustomerId = customerId;
        existingSpec.ProcessId = processId;
        existingSpec.MachineModelId = machineModelId;
        existingSpec.Acceptance = NormalizeNullable(acceptance);
        existingSpec.Remark = NormalizeNullable(remark);
        existingSpec.WordFileId = wordFileId;
        existingSpec.ImportedAt = DateTime.UtcNow;
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }

    /// <summary>
    /// 创建验收规格实体
    /// </summary>
    private static AcceptanceSpec CreateAcceptanceSpec(
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        string? project,
        string? specification,
        string? acceptance,
        string? remark,
        int createdByUserId,
        int? ownerOrgUnitId)
    {
        return new AcceptanceSpec
        {
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            Project = project?.Trim() ?? string.Empty,
            Specification = specification?.Trim() ?? string.Empty,
            Acceptance = NormalizeNullable(acceptance),
            Remark = NormalizeNullable(remark),
            CreatedByUserId = createdByUserId,
            OwnerOrgUnitId = ownerOrgUnitId,
            WordFileId = wordFileId,
            ImportedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 规范化文本（用于比较）
    /// </summary>
    private static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    /// <summary>
    /// 规范化可空文本（用于入库/返回）
    /// </summary>
    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// 判断库中规格与导入内容是否完全一致
    /// </summary>
    private static bool IsSameContent(
        AcceptanceSpec spec,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        return NormalizeText(spec.Project) == project &&
               NormalizeText(spec.Specification) == specification &&
               NormalizeText(spec.Acceptance) == acceptance &&
               NormalizeText(spec.Remark) == remark;
    }

    /// <summary>
    /// 构造差异确认键
    /// </summary>
    private static string BuildDifferenceKey(
        int tableIndex,
        int rowIndex,
        string matchType,
        int existingSpecId,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        var raw = $"{tableIndex}|{rowIndex}|{matchType}|{existingSpecId}|{project}|{specification}|{acceptance}|{remark}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// 获取单元格值
    /// </summary>
    private static string? GetCellValue(RowData row, int columnIndex)
    {
        return row.GetValue(columnIndex);
    }

    /// <summary>
    /// 获取整行列值（按列索引顺序）
    /// </summary>
    private static List<string> GetRowValues(RowData row)
    {
        if (row.Cells == null || row.Cells.Count == 0)
        {
            return [];
        }

        var maxColumnIndex = row.Cells.Max(c => c.ColumnIndex);
        var valuesByColumn = row.Cells
            .GroupBy(c => c.ColumnIndex)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.Value ?? string.Empty);

        var values = new List<string>(maxColumnIndex + 1);
        for (var col = 0; col <= maxColumnIndex; col++)
        {
            values.Add(valuesByColumn.TryGetValue(col, out var value) ? value : string.Empty);
        }

        return values;
    }

    /// <summary>
    /// 打开Word文件读取流：优先文件系统路径，缺失时回退到DB二进制（兼容旧数据）
    /// </summary>
    private Stream OpenWordFileReadStream(WordFile wordFile)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            if (System.IO.File.Exists(fullPath))
            {
                return System.IO.File.OpenRead(fullPath);
            }
        }

        if (wordFile.FileContent != null && wordFile.FileContent.Length > 0)
        {
            return new MemoryStream(wordFile.FileContent);
        }

        throw new InvalidOperationException("文件内容不可用（未找到物理文件且数据库内容为空）");
    }

    private sealed class ImportExecutionContext
    {
        public required ImportResult Result { get; init; }

        public required List<AcceptanceSpec> ExistingSpecs { get; init; }

        public required List<AcceptanceSpec> PendingInsertedSpecs { get; init; }

        public required List<AcceptanceSpec> SpecsToInsert { get; init; }

        public required HashSet<string> ConfirmedDifferenceKeys { get; init; }

        public required HashSet<string> PartiallyConfirmedDifferenceKeys { get; init; }

        public required HashSet<string> SkippedDifferenceKeys { get; init; }

        public required Dictionary<string, PendingDecisionEntry> PendingDecisionMap { get; init; }

        public required ImportDuplicateDetectionSession DuplicateSession { get; init; }

        public required int CustomerId { get; init; }

        public required int? ProcessId { get; init; }

        public required int? MachineModelId { get; init; }

        public required int FileId { get; init; }

        public required int UserId { get; init; }

        public required int? OwnerOrgUnitId { get; init; }

        public required bool PreviewSkippedRows { get; init; }

        public int OverwriteCount { get; set; }

        public bool SkipSemanticDetection =>
            PendingDecisionMap.Count > 0 ||
            ConfirmedDifferenceKeys.Count > 0 ||
            PartiallyConfirmedDifferenceKeys.Count > 0 ||
            SkippedDifferenceKeys.Count > 0;
    }

    private sealed class PendingDecisionEntry
    {
        public required string LookupKey { get; init; }

        public required string MatchType { get; init; }

        public required int ExistingSpecId { get; init; }

        public required DifferenceDecision Decision { get; init; }
    }

    private enum DifferenceDecision
    {
        Import,
        PartialImport,
        Skip
    }

    private sealed record ImportRowPayload(
        int RowIndex,
        List<string> RowValues,
        string? Project,
        string? Specification,
        string? Acceptance,
        string? Remark);
}
