using System.Text;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

public interface IDocumentImportAppService
{
    Task<DocumentImportAppResult> ImportWordAsync(
        DataScopeResult scope,
        ImportDataRequest request,
        CancellationToken cancellationToken);

    Task<DocumentImportAppResult> ImportExcelAsync(
        DataScopeResult scope,
        ExcelImportDataRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 文档导入应用服务。
/// </summary>
public sealed partial class DocumentImportAppService : IDocumentImportAppService
{
    private const string MatchTypeExact = "exact";
    private const string MatchTypeConflict = "conflict";
    private const string MatchTypeSemantic = "semantic";

    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly ImportDuplicateDetectionService _importDuplicateDetectionService;
    private readonly SpecEmbeddingCacheService _specEmbeddingCacheService;
    private readonly EmbeddingCacheWarmupManager _embeddingCacheWarmupManager;
    private readonly ILogger<DocumentImportAppService> _logger;

    public DocumentImportAppService(
        IUnitOfWork unitOfWork,
        DocumentFileAccessService documentFileAccessService,
        DocumentTableAccessService documentTableAccessService,
        ImportDuplicateDetectionService importDuplicateDetectionService,
        SpecEmbeddingCacheService specEmbeddingCacheService,
        EmbeddingCacheWarmupManager embeddingCacheWarmupManager,
        ILogger<DocumentImportAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _importDuplicateDetectionService = importDuplicateDetectionService;
        _specEmbeddingCacheService = specEmbeddingCacheService;
        _embeddingCacheWarmupManager = embeddingCacheWarmupManager;
        _logger = logger;
    }

    public async Task<DocumentImportAppResult> ImportWordAsync(
        DataScopeResult scope,
        ImportDataRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(
                request.FileId,
                scope,
                includeScopedSpecs: true);
            if (wordFile == null)
            {
                throw new ApplicationServiceException(400, "文件不存在");
            }

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                throw new ApplicationServiceException(400, "该文件为 Excel，请使用 Excel 导入接口");
            }

            await ValidateImportTargetAsync(request.CustomerId, request.ProcessId, request.MachineModelId);

            if (!request.Mapping.ProjectColumn.HasValue ||
                !request.Mapping.SpecificationColumn.HasValue ||
                !request.Mapping.AcceptanceColumn.HasValue ||
                !request.Mapping.RemarkColumn.HasValue)
            {
                throw new ApplicationServiceException(400, "项目列、规格列、验收标准列、备注列为必填");
            }

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
            try
            {
                tableData = await _documentTableAccessService.ExtractTableDataAsync(
                    wordFile,
                    request.TableIndex,
                    mapping);
            }
            catch (ApplicationServiceException)
            {
                throw;
            }

            return await ExecuteImportAsync(
                scope,
                wordFile,
                request.TableIndex,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                request.ConfirmedDifferenceKeys,
                request.PartiallyConfirmedDifferenceKeys,
                request.SkippedDifferenceKeys,
                request.ExcludedRowIndexes,
                request.DuplicateCheckOptions,
                request.PreviewSkippedRows,
                request.CleanupSourceFile,
                tableData,
                row => new ImportRowPayload(
                    row.Index,
                    GetRowValues(row),
                    GetCellValue(row, request.Mapping.ProjectColumn!.Value),
                    GetCellValue(row, request.Mapping.SpecificationColumn!.Value),
                    GetCellValue(row, request.Mapping.AcceptanceColumn!.Value),
                    GetCellValue(row, request.Mapping.RemarkColumn!.Value)),
                "表格",
                cancellationToken);
        }
        catch (AiServiceUnavailableException ex)
        {
            throw new ApplicationServiceException(400, BuildAiImportUnavailableMessage(request.DuplicateCheckOptions, ex));
        }
    }

    public async Task<DocumentImportAppResult> ImportExcelAsync(
        DataScopeResult scope,
        ExcelImportDataRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await _documentFileAccessService.GetAccessibleWordFileAsync(
                request.FileId,
                scope,
                includeScopedSpecs: true);
            if (file == null)
            {
                throw new ApplicationServiceException(400, "文件不存在");
            }

            if (file.FileType != UploadedFileType.ExcelXlsx)
            {
                throw new ApplicationServiceException(400, "该文件不是 Excel（.xlsx）");
            }

            await ValidateImportTargetAsync(request.CustomerId, request.ProcessId, request.MachineModelId);

            if (request.ProjectColumn <= 0 || request.SpecificationColumn <= 0)
            {
                throw new ApplicationServiceException(400, "项目列与规格内容列为必填，且列号必须 >= 1");
            }

            if (request.HeaderRowStart < 1 ||
                request.HeaderRowCount < 0 ||
                request.DataStartRow < 1 ||
                request.DataEndRow is <= 0)
            {
                throw new ApplicationServiceException(400, "表头行与数据范围配置不合法");
            }

            IReadOnlyList<TableInfo> tables;
            tables = await _documentTableAccessService.GetTablesAsync(file);

            if (request.SheetIndex < 0 || request.SheetIndex >= tables.Count)
            {
                throw new ApplicationServiceException(400, "工作表索引超出范围");
            }

            var sheetInfo = tables[request.SheetIndex];
            if (sheetInfo.RowCount <= 0 || sheetInfo.ColumnCount <= 0)
            {
                return new DocumentImportAppResult(new ImportResult(), "工作表为空，无可导入数据");
            }

            var usedStartCol = sheetInfo.UsedRangeStartColumn;
            var usedStartRow = sheetInfo.UsedRangeStartRow;
            var usedEndCol = usedStartCol + sheetInfo.ColumnCount - 1;
            var usedEndRow = usedStartRow + sheetInfo.RowCount - 1;

            static bool IsInRange(int value, int start, int end) => value >= start && value <= end;

            if (!IsInRange(request.ProjectColumn, usedStartCol, usedEndCol))
            {
                throw new ApplicationServiceException(400, $"列号越界：ProjectColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
            }

            if (!IsInRange(request.SpecificationColumn, usedStartCol, usedEndCol))
            {
                throw new ApplicationServiceException(400, $"列号越界：SpecificationColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
            }

            if (request.AcceptanceColumn.HasValue && !IsInRange(request.AcceptanceColumn.Value, usedStartCol, usedEndCol))
            {
                throw new ApplicationServiceException(400, $"列号越界：AcceptanceColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
            }

            if (request.RemarkColumn.HasValue && !IsInRange(request.RemarkColumn.Value, usedStartCol, usedEndCol))
            {
                throw new ApplicationServiceException(400, $"列号越界：RemarkColumn，已用区域列范围为 {usedStartCol}~{usedEndCol}");
            }

            if (request.DataStartRow > usedEndRow)
            {
                throw new ApplicationServiceException(400, $"数据起始行超出已用区域：{request.DataStartRow} > {usedEndRow}");
            }

            if (request.DataEndRow.HasValue)
            {
                if (request.DataEndRow.Value < request.DataStartRow)
                {
                    throw new ApplicationServiceException(400, "数据结束行不能早于数据起始行");
                }

                if (request.DataEndRow.Value > usedEndRow)
                {
                    throw new ApplicationServiceException(400, $"数据结束行超出已用区域：{request.DataEndRow.Value} > {usedEndRow}");
                }
            }

            var mapping = new ColumnMapping
            {
                HeaderRowIndex = Math.Max(0, request.HeaderRowStart - usedStartRow),
                HeaderRowCount = Math.Max(1, request.HeaderRowCount == 0 ? 1 : request.HeaderRowCount),
                DataStartRowIndex = Math.Max(0, request.DataStartRow - usedStartRow)
            };

            var maxDataRowCount = request.DataEndRow.HasValue
                ? request.DataEndRow.Value - request.DataStartRow + 1
                : (int?)null;

            TableData tableData;
            tableData = await _documentTableAccessService.ExtractTableDataAsync(
                file,
                request.SheetIndex,
                mapping,
                maxDataRowCount);

            var projectCol = request.ProjectColumn - usedStartCol;
            var specCol = request.SpecificationColumn - usedStartCol;
            var acceptanceCol = request.AcceptanceColumn.HasValue ? request.AcceptanceColumn.Value - usedStartCol : (int?)null;
            var remarkCol = request.RemarkColumn.HasValue ? request.RemarkColumn.Value - usedStartCol : (int?)null;

            return await ExecuteImportAsync(
                scope,
                file,
                request.SheetIndex,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                request.ConfirmedDifferenceKeys,
                request.PartiallyConfirmedDifferenceKeys,
                request.SkippedDifferenceKeys,
                request.ExcludedRowIndexes,
                request.DuplicateCheckOptions,
                request.PreviewSkippedRows,
                request.CleanupSourceFile,
                tableData,
                row => new ImportRowPayload(
                    request.DataStartRow + row.Index,
                    GetRowValues(row),
                    GetCellValue(row, projectCol),
                    GetCellValue(row, specCol),
                    acceptanceCol.HasValue ? GetCellValue(row, acceptanceCol.Value) : null,
                    remarkCol.HasValue ? GetCellValue(row, remarkCol.Value) : null),
                "工作表",
                cancellationToken);
        }
        catch (AiServiceUnavailableException ex)
        {
            throw new ApplicationServiceException(400, BuildAiImportUnavailableMessage(request.DuplicateCheckOptions, ex));
        }
    }







}

public sealed record DocumentImportAppResult(ImportResult Result, string Message);
