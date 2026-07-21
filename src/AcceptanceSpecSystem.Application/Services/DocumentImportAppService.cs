using System.Text;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 文档导入服务入口，按文件类型解析表格并写入验收规格主数据。
/// </summary>
public interface IDocumentImportAppService
{
    Task<DocumentImportAppResult> ImportWordAsync(
        SpecAccessContext scope,
        ImportDataRequest request,
        CancellationToken cancellationToken);

    Task<DocumentImportAppResult> ImportExcelAsync(
        SpecAccessContext scope,
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
    private readonly IDocumentImportExecutionRepository _importExecutions;
    private readonly IDocumentFileAccessService _documentFileAccessService;
    private readonly IDocumentImportTableReader _documentTableAccessService;
    private readonly ImportDuplicateDetectionService _importDuplicateDetectionService;
    private readonly IImportEmbeddingCache _specEmbeddingCacheService;
    private readonly IImportWarmupTrigger _embeddingCacheWarmupTrigger;
    private readonly ColumnMappingLearningService _columnMappingLearningService;
    private readonly IMatchingApprovalTokenProtector _decisionTokenProtector;
    private readonly ILogger<DocumentImportAppService> _logger;

    public DocumentImportAppService(
        IUnitOfWork unitOfWork,
        IDocumentImportExecutionRepository importExecutions,
        IDocumentFileAccessService documentFileAccessService,
        IDocumentImportTableReader documentTableAccessService,
        ImportDuplicateDetectionService importDuplicateDetectionService,
        IImportEmbeddingCache specEmbeddingCacheService,
        IImportWarmupTrigger embeddingCacheWarmupTrigger,
        ColumnMappingLearningService columnMappingLearningService,
        IMatchingApprovalTokenProtector decisionTokenProtector,
        ILogger<DocumentImportAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _importExecutions = importExecutions;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _importDuplicateDetectionService = importDuplicateDetectionService;
        _specEmbeddingCacheService = specEmbeddingCacheService;
        _embeddingCacheWarmupTrigger = embeddingCacheWarmupTrigger;
        _columnMappingLearningService = columnMappingLearningService;
        _decisionTokenProtector = decisionTokenProtector;
        _logger = logger;
    }

    public Task<DocumentImportAppResult> ImportWordAsync(
        SpecAccessContext scope,
        ImportDataRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteIdempotentImportAsync(
            scope,
            request.ExecutionRequestId,
            request,
            () => AuthorizeImportReplayAsync(
                scope,
                request.FileId,
                UploadedFileType.WordDocx,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                cancellationToken),
            idempotency => ImportWordCoreAsync(scope, request, idempotency, cancellationToken),
            cancellationToken);
    }

    private async Task<DocumentImportAppResult> ImportWordCoreAsync(
        SpecAccessContext scope,
        ImportDataRequest request,
        ImportIdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        try
        {
            var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(
                request.FileId,
                scope,
                includeScopedSpecs: true,
                cancellationToken);
            if (wordFile == null)
            {
                throw new ApplicationServiceException(400, "文件不存在");
            }

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                throw new ApplicationServiceException(400, "该文件为 Excel，请使用 Excel 导入接口");
            }

            await ValidateImportTargetAsync(
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                cancellationToken);

            if (!request.Mapping.SpecificationColumn.HasValue ||
                !request.Mapping.AcceptanceColumn.HasValue ||
                !request.Mapping.RemarkColumn.HasValue)
            {
                throw new ApplicationServiceException(400, "规格列、验收标准列、备注列为必填");
            }

            ValidateSpecificationOnlyProjectBackfill(
                request.IsSpecificationOnly,
                request.Mapping.ProjectColumn,
                request.Mapping.SpecificationColumn,
                projectColumnBase: 0);

            var headerRowCount = Math.Max(1, request.HeaderRowCount);
            var headerEndRowIndex = request.Mapping.HeaderRowIndex + headerRowCount - 1;
            if (request.Mapping.HeaderRowIndex < 0 ||
                request.Mapping.DataStartRowIndex <= headerEndRowIndex ||
                (request.DataEndRowIndex.HasValue &&
                 request.DataEndRowIndex.Value < request.Mapping.DataStartRowIndex))
            {
                throw new ApplicationServiceException(400, "Word 表头行或数据起始行配置不合法");
            }

            var mapping = new ColumnMapping
            {
                ProjectColumn = request.Mapping.ProjectColumn,
                SpecificationColumn = request.Mapping.SpecificationColumn,
                AcceptanceColumn = request.Mapping.AcceptanceColumn,
                RemarkColumn = request.Mapping.RemarkColumn,
                HeaderRowIndex = request.Mapping.HeaderRowIndex,
                HeaderRowCount = headerRowCount,
                DataStartRowIndex = request.Mapping.DataStartRowIndex
            };

            var maxDataRowCount = request.DataEndRowIndex.HasValue
                ? request.DataEndRowIndex.Value - request.Mapping.DataStartRowIndex + 1
                : (int?)null;

            TableData tableData;
            try
            {
                tableData = await _documentTableAccessService.ExtractTableDataAsync(
                    wordFile,
                    request.TableIndex,
                    mapping,
                    maxDataRowCount,
                    cancellationToken: cancellationToken);
            }
            catch (ApplicationServiceException)
            {
                throw;
            }

            ValidateSpecificationOnlyColumnHealth(
                tableData,
                request.Mapping.ProjectColumn,
                request.Mapping.SpecificationColumn!.Value,
                request.IsSpecificationOnly);

            var importResult = await ExecuteImportAsync(
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
                row =>
                {
                    var specification = GetCellValue(row, request.Mapping.SpecificationColumn!.Value);
                    return new ImportRowPayload(
                        row.Index,
                        GetRowValues(row),
                        ResolveImportProjectValue(
                            row,
                            request.Mapping.ProjectColumn,
                            request.Mapping.SpecificationColumn!.Value,
                            request.IsSpecificationOnly),
                        specification,
                        GetCellValue(row, request.Mapping.AcceptanceColumn!.Value),
                        GetCellValue(row, request.Mapping.RemarkColumn!.Value));
                },
                "表格",
                idempotency,
                cancellationToken);

            MarkSpecificationOnlyBackfill(importResult, request.IsSpecificationOnly, request.Mapping.ProjectColumn);

            await TryLearnColumnMappingsAfterImportAsync(
                request.CustomerId,
                tableData.Headers.ToList(),
                request.Mapping.ProjectColumn,
                request.Mapping.SpecificationColumn,
                request.Mapping.AcceptanceColumn,
                request.Mapping.RemarkColumn,
                $"表格{request.TableIndex + 1}",
                importResult,
                cancellationToken);
            return importResult;
        }
        catch (AiServiceUnavailableException ex)
        {
            throw new ApplicationServiceException(400, BuildAiImportUnavailableMessage(request.DuplicateCheckOptions, ex));
        }
    }

    public Task<DocumentImportAppResult> ImportExcelAsync(
        SpecAccessContext scope,
        ExcelImportDataRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteIdempotentImportAsync(
            scope,
            request.ExecutionRequestId,
            request,
            () => AuthorizeImportReplayAsync(
                scope,
                request.FileId,
                UploadedFileType.ExcelXlsx,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                cancellationToken),
            idempotency => ImportExcelCoreAsync(scope, request, idempotency, cancellationToken),
            cancellationToken);
    }

    private async Task<DocumentImportAppResult> ImportExcelCoreAsync(
        SpecAccessContext scope,
        ExcelImportDataRequest request,
        ImportIdempotencyContext? idempotency,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await _documentFileAccessService.GetAccessibleWordFileAsync(
                request.FileId,
                scope,
                includeScopedSpecs: true,
                cancellationToken);
            if (file == null)
            {
                throw new ApplicationServiceException(400, "文件不存在");
            }

            if (file.FileType != UploadedFileType.ExcelXlsx)
            {
                throw new ApplicationServiceException(400, "该文件不是 Excel（.xlsx）");
            }

            await ValidateImportTargetAsync(
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                cancellationToken);

            if (request.SpecificationColumn <= 0)
            {
                throw new ApplicationServiceException(400, "规格内容列为必填，且列号必须 >= 1");
            }

            ValidateSpecificationOnlyProjectBackfill(
                request.IsSpecificationOnly,
                request.ProjectColumn,
                request.SpecificationColumn,
                projectColumnBase: 1);

            if (request.HeaderRowStart < 1 ||
                request.HeaderRowCount < 0 ||
                request.DataStartRow < 1 ||
                request.DataEndRow is <= 0)
            {
                throw new ApplicationServiceException(400, "表头行与数据范围配置不合法");
            }

            IReadOnlyList<TableInfo> tables;
            tables = await _documentTableAccessService.GetTablesAsync(file, cancellationToken);

            if (request.SheetIndex < 0 || request.SheetIndex >= tables.Count)
            {
                throw new ApplicationServiceException(400, "工作表索引超出范围");
            }

            var sheetInfo = tables[request.SheetIndex];
            if (sheetInfo.RowCount <= 0 || sheetInfo.ColumnCount <= 0)
            {
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
                    new TableData { TableIndex = request.SheetIndex },
                    _ => throw new InvalidOperationException("空工作表不应生成导入行"),
                    "工作表",
                    idempotency,
                    cancellationToken,
                    completedMessageOverride: "工作表为空，无可导入数据");
            }

            var usedStartCol = sheetInfo.UsedRangeStartColumn;
            var usedStartRow = sheetInfo.UsedRangeStartRow;
            var usedEndCol = usedStartCol + sheetInfo.ColumnCount - 1;
            var usedEndRow = usedStartRow + sheetInfo.RowCount - 1;

            static bool IsInRange(int value, int start, int end) => value >= start && value <= end;

            if (!IsInRange(request.HeaderRowStart, usedStartRow, usedEndRow))
            {
                throw new ApplicationServiceException(400, $"表头起始行超出已用区域：{request.HeaderRowStart}，允许范围为 {usedStartRow}~{usedEndRow}");
            }

            var effectiveHeaderRowCount = Math.Max(1, request.HeaderRowCount);
            var minimumDataStartRow = request.HeaderRowStart + effectiveHeaderRowCount;
            if (request.DataStartRow < usedStartRow || request.DataStartRow < minimumDataStartRow)
            {
                throw new ApplicationServiceException(400, $"数据起始行必须位于表头之后且不能早于已用区域：最小为 {Math.Max(usedStartRow, minimumDataStartRow)}");
            }

            if (request.ProjectColumn.HasValue && !IsInRange(request.ProjectColumn.Value, usedStartCol, usedEndCol))
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
                maxDataRowCount,
                cancellationToken);

            var projectCol = request.ProjectColumn.HasValue ? request.ProjectColumn.Value - usedStartCol : (int?)null;
            var specCol = request.SpecificationColumn - usedStartCol;
            var acceptanceCol = request.AcceptanceColumn.HasValue ? request.AcceptanceColumn.Value - usedStartCol : (int?)null;
            var remarkCol = request.RemarkColumn.HasValue ? request.RemarkColumn.Value - usedStartCol : (int?)null;

            ValidateSpecificationOnlyColumnHealth(
                tableData,
                projectCol,
                specCol,
                request.IsSpecificationOnly);

            var importResult = await ExecuteImportAsync(
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
                row =>
                {
                    var specification = GetCellValue(row, specCol);
                    return new ImportRowPayload(
                        request.DataStartRow + row.Index,
                        GetRowValues(row),
                        ResolveImportProjectValue(row, projectCol, specCol, request.IsSpecificationOnly),
                        specification,
                        acceptanceCol.HasValue ? GetCellValue(row, acceptanceCol.Value) : null,
                        remarkCol.HasValue ? GetCellValue(row, remarkCol.Value) : null);
                },
                "工作表",
                idempotency,
                cancellationToken);

            MarkSpecificationOnlyBackfill(importResult, request.IsSpecificationOnly, projectCol);

            await TryLearnColumnMappingsAfterImportAsync(
                request.CustomerId,
                tableData.Headers.ToList(),
                projectCol,
                specCol,
                acceptanceCol,
                remarkCol,
                sheetInfo.Name,
                importResult,
                cancellationToken);
            return importResult;
        }
        catch (AiServiceUnavailableException ex)
        {
            throw new ApplicationServiceException(400, BuildAiImportUnavailableMessage(request.DuplicateCheckOptions, ex));
        }
    }

}

public sealed record DocumentImportAppResult(ImportResult Result, string Message);
