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
/// 文档处理API控制器
/// </summary>
[Route("api/documents")]
public class DocumentsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<DocumentsController> _logger;

    /// <summary>
    /// 创建文档控制器实例
    /// </summary>
    public DocumentsController(
        IUnitOfWork unitOfWork,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        ILogger<DocumentsController> logger)
    {
        _unitOfWork = unitOfWork;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
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
        var allFiles = string.IsNullOrWhiteSpace(keyword)
            ? await _unitOfWork.WordFiles.GetAllAsync()
            : await _unitOfWork.WordFiles.FindAsync(f => f.FileName.Contains(keyword));

        // 排除手动录入的占位文件
        allFiles = allFiles.Where(f => f.FileName != "__MANUAL_ENTRY__").ToList();

        var total = allFiles.Count;
        var items = allFiles
            .OrderByDescending(f => f.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new WordFileDto
            {
                Id = f.Id,
                FileName = f.FileName,
                FileType = f.FileType,
                FileHash = f.FileHash,
                UploadedAt = f.UploadedAt,
                SpecCount = f.AcceptanceSpecs?.Count ?? 0
            })
            .ToList();

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
    /// </summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileUploadResponse>>> UploadFile(IFormFile file)
    {
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

        // 读取文件内容
        var fileType = extension == ".xlsx" ? UploadedFileType.ExcelXlsx : UploadedFileType.WordDocx;

        byte[] fileContent;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream);
            fileContent = memoryStream.ToArray();
        }

        // 计算文件哈希
        var fileHash = ComputeFileHash(fileContent);

        // 检查是否为重复文件
        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(f => f.FileHash == fileHash);
        if (existingFile != null)
        {
            // 若历史数据仍存DB二进制且未落盘，则在“重复上传”时顺便迁移到文件系统存储
            // （避免后续读写总是依赖 FileContent）
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
                // 不影响返回（只记录日志）
                _logger.LogWarning(ex, "重复文件落盘迁移失败: {FileId} - {FileName}", existingFile.Id, existingFile.FileName);
            }

            // 获取表格数量
            var tableCount = 0;
            using (var stream = OpenWordFileReadStream(existingFile))
            {
                var parser = _documentServiceFactory.GetParser(existingFile.FilePath ?? existingFile.FileName);
                if (parser != null)
                {
                    var tables = await parser.GetTablesAsync(stream);
                    tableCount = tables.Count;
                }
            }

            return Success(new FileUploadResponse
            {
                FileId = existingFile.Id,
                FileName = existingFile.FileName,
                FileHash = existingFile.FileHash,
                IsDuplicate = true,
                TableCount = tableCount,
                FileType = existingFile.FileType
            }, "该文件已存在");
        }

        // 保存新文件（文件系统存储）
        var filePath = fileType == UploadedFileType.ExcelXlsx
            ? await _fileStorage.SaveUploadedExcelAsync(file.FileName, fileContent)
            : await _fileStorage.SaveUploadedWordAsync(file.FileName, fileContent);
        var wordFile = new WordFile
        {
            FileName = file.FileName,
            FileContent = Array.Empty<byte>(), // 新存储方式不再写入DB（兼容字段保留）
            FilePath = filePath,
            FileHash = fileHash,
            UploadedAt = DateTime.Now,
            FileType = fileType
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile);
        await _unitOfWork.SaveChangesAsync();

        // 获取表格数量
        var newTableCount = 0;
        using (var stream = new MemoryStream(fileContent))
        {
            var parser = fileType == UploadedFileType.ExcelXlsx
                ? _documentServiceFactory.GetParser(DocumentType.Excel)
                : _documentServiceFactory.GetParser(DocumentType.Word);
            if (parser != null)
            {
                var tables = await parser.GetTablesAsync(stream);
                newTableCount = tables.Count;
            }
        }

        _logger.LogInformation("文件上传成功: {FileId} - {FileName}", wordFile.Id, wordFile.FileName);

        return Success(new FileUploadResponse
        {
            FileId = wordFile.Id,
            FileName = wordFile.FileName,
            FileHash = wordFile.FileHash,
            IsDuplicate = false,
            TableCount = newTableCount,
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
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(id);
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
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(id);
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
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResult>>> ImportData([FromBody] ImportDataRequest request)
    {
        // 验证文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(request.FileId);
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

        // 导入数据
        var result = new ImportResult
        {
            TotalCount = tableData.Rows.Count
        };

        foreach (var row in tableData.Rows)
        {
            try
            {
                var project = GetCellValue(row, request.Mapping.ProjectColumn!.Value);
                var specification = GetCellValue(row, request.Mapping.SpecificationColumn!.Value);

                // 跳过空行
                if (string.IsNullOrWhiteSpace(project) && string.IsNullOrWhiteSpace(specification))
                {
                    result.SkippedCount++;
                    continue;
                }

                // 验证必填字段
                if (string.IsNullOrWhiteSpace(project))
                {
                    result.FailedCount++;
                    result.Errors.Add(new ImportError
                    {
                        RowIndex = row.Index,
                        Message = "项目名称不能为空"
                    });
                    continue;
                }

                // 列映射已校验必填，这里直接读取（单元格内容仍允许为空）
                var acceptance = GetCellValue(row, request.Mapping.AcceptanceColumn!.Value);
                var remark = GetCellValue(row, request.Mapping.RemarkColumn!.Value);

                var spec = new AcceptanceSpec
                {
                    CustomerId = request.CustomerId,
                    ProcessId = request.ProcessId,
                    MachineModelId = request.MachineModelId,
                    Project = project.Trim(),
                    Specification = string.IsNullOrWhiteSpace(specification)
                        ? string.Empty
                        : specification.Trim(),
                    Acceptance = acceptance?.Trim(),
                    Remark = remark?.Trim(),
                    WordFileId = request.FileId,
                    ImportedAt = DateTime.Now
                };

                await _unitOfWork.AcceptanceSpecs.AddAsync(spec);
                result.SuccessCount++;
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

        await _unitOfWork.SaveChangesAsync();

        // 写入操作历史（导入）
        try
        {
            var history = new OperationHistory
            {
                OperationType = OperationType.Import,
                TargetFile = wordFile.FilePath,
                Details = JsonSerializer.Serialize(new
                {
                    fileId = request.FileId,
                    tableIndex = request.TableIndex,
                    customerId = request.CustomerId,
                    processId = request.ProcessId,
                    machineModelId = request.MachineModelId,
                    success = result.SuccessCount,
                    failed = result.FailedCount,
                    skipped = result.SkippedCount
                }),
                CanUndo = false,
                CreatedAt = DateTime.Now
            };
            await _unitOfWork.OperationHistories.AddAsync(history);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入导入操作历史失败: fileId={FileId}", request.FileId);
        }

        _logger.LogInformation(
            "导入完成: 文件{FileId}, 表格{TableIndex}, 客户{CustomerId}, 制程{ProcessId}, 机型{MachineModelId}, 成功{Success}, 失败{Failed}, 跳过{Skipped}",
            request.FileId, request.TableIndex, request.CustomerId, request.ProcessId, request.MachineModelId, result.SuccessCount, result.FailedCount, result.SkippedCount);

        return Success(result, $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条");
    }

    /// <summary>
    /// 删除已上传的文件
    /// </summary>
    /// <summary>
    /// Excel 导入：按列序号配置导入（列号/行号均为 1-based）
    /// </summary>
    [HttpPost("excel/import")]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResult>>> ImportExcelData([FromBody] ExcelImportDataRequest request)
    {
        var file = await _unitOfWork.WordFiles.GetByIdAsync(request.FileId);
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

        foreach (var row in tableData.Rows)
        {
            var excelRowNumber = request.DataStartRow + row.Index;

            try
            {
                var project = GetCellValue(row, projectCol);
                var specification = GetCellValue(row, specCol);

                if (string.IsNullOrWhiteSpace(project) && string.IsNullOrWhiteSpace(specification))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(project))
                {
                    result.FailedCount++;
                    result.Errors.Add(new ImportError
                    {
                        RowIndex = excelRowNumber,
                        Message = "项目名称不能为空"
                    });
                    continue;
                }

                var acceptance = acceptanceCol.HasValue ? GetCellValue(row, acceptanceCol.Value) : null;
                var remark = remarkCol.HasValue ? GetCellValue(row, remarkCol.Value) : null;

                var spec = new AcceptanceSpec
                {
                    CustomerId = request.CustomerId,
                    ProcessId = request.ProcessId,
                    MachineModelId = request.MachineModelId,
                    Project = project.Trim(),
                    Specification = string.IsNullOrWhiteSpace(specification)
                        ? string.Empty
                        : specification.Trim(),
                    Acceptance = string.IsNullOrWhiteSpace(acceptance) ? null : acceptance.Trim(),
                    Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
                    WordFileId = request.FileId,
                    ImportedAt = DateTime.Now
                };

                await _unitOfWork.AcceptanceSpecs.AddAsync(spec);
                result.SuccessCount++;
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

        await _unitOfWork.SaveChangesAsync();
        return Success(result, $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条");
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteFile(int id)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(id);
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

        // 写入操作历史（删除）
        try
        {
            var history = new OperationHistory
            {
                OperationType = OperationType.Delete,
                TargetFile = wordFile.FilePath,
                Details = JsonSerializer.Serialize(new { fileId = id, fileName = wordFile.FileName }),
                CanUndo = false,
                CreatedAt = DateTime.Now
            };
            await _unitOfWork.OperationHistories.AddAsync(history);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入删除操作历史失败: fileId={FileId}", id);
        }

        _logger.LogInformation("删除文件成功: {FileId} - {FileName}", wordFile.Id, wordFile.FileName);

        return Success("删除成功");
    }

    /// <summary>
    /// 计算文件哈希
    /// </summary>
    private static string ComputeFileHash(byte[] content)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(content);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 获取单元格值
    /// </summary>
    private static string? GetCellValue(RowData row, int columnIndex)
    {
        return row.GetValue(columnIndex);
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
}
