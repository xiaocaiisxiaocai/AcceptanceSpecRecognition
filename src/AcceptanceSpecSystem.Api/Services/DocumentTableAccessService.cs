using System.Text.RegularExpressions;
using System.Diagnostics;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 文档表格访问协作组件。
/// </summary>
public sealed class DocumentTableAccessService : IDocumentImportTableReader, IBatchReplyDocumentTablePort
{
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IUploadedDocumentSnapshotProvider _snapshotProvider;
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;
    private readonly ILogger<DocumentTableAccessService> _logger;

    public DocumentTableAccessService(
        DocumentServiceFactory documentServiceFactory,
        IUploadedDocumentSnapshotProvider snapshotProvider,
        IResourceBudgetGovernor resourceBudgetGovernor,
        ILogger<DocumentTableAccessService> logger)
    {
        _documentServiceFactory = documentServiceFactory;
        _snapshotProvider = snapshotProvider;
        _resourceBudgetGovernor = resourceBudgetGovernor;
        _logger = logger;
    }

    public async Task<int> CountTablesAsync(
        UploadedFileType fileType,
        byte[] fileContent,
        CancellationToken cancellationToken = default)
    {
        _resourceBudgetGovernor.ValidateDocumentSize(fileContent.LongLength);
        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(
            ResourceWorkload.DocumentParsing,
            cancellationToken);
        var parser = _documentServiceFactory.GetParser(GetDocumentType(fileType));
        if (parser == null)
        {
            return 0;
        }

        using var stream = new MemoryStream(fileContent);
        var tables = await parser.GetTablesAsync(stream, cancellationToken);
        return tables.Count;
    }

    public async Task<List<TableInfoDto>> GetTableInfoDtosAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default)
    {
        var tables = await GetTablesAsync(wordFile, cancellationToken);
        return DocumentTableQueryAppService.MapTableInfos(tables);
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.TraceId.ToString() ?? "none";
        var snapshot = await _snapshotProvider.GetSnapshotAsync(wordFile, cancellationToken);
        _logger.LogInformation(
            "文档表格元数据读取完成: FileId={FileId}, FileType={FileType}, TableCount={TableCount}, QueueMs={QueueMs}, ParseMs={ParseMs}, ElapsedMs={ElapsedMs}, TraceId={TraceId}",
            wordFile.Id,
            wordFile.FileType,
            snapshot.Tables.Count,
            0,
            0,
            totalStopwatch.ElapsedMilliseconds,
            traceId);
        return snapshot.Tables;
    }

    public async Task<TableData> ExtractTableDataAsync(
        WordFile wordFile,
        int tableIndex,
        ColumnMapping mapping,
        int? maxDataRowCount = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExtractProjectedTableDataAsync(
                wordFile,
                tableIndex,
                mapping,
                maxDataRowCount,
                cancellationToken);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ApplicationServiceException(400, "表格索引超出范围");
        }
    }

    public async Task<TableDataDto> GetTablePreviewAsync(
        WordFile wordFile,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int? dataEndRowIndex = null,
        CancellationToken cancellationToken = default)
    {
        DocumentTableQueryAppService.ValidatePreviewSize(previewRows);
        if (dataEndRowIndex.HasValue && dataEndRowIndex.Value < dataStartRowIndex)
            throw new ApplicationServiceException(400, "数据结束行不能早于数据起始行");

        var previewRangeRowCount = dataEndRowIndex.HasValue
            ? dataEndRowIndex.Value - dataStartRowIndex + 1
            : (int?)null;
        int? maxDataRowCount = previewRows;
        if (previewRangeRowCount.HasValue)
        {
            maxDataRowCount = maxDataRowCount.HasValue
                ? Math.Min(maxDataRowCount.Value, previewRangeRowCount.Value)
                : previewRangeRowCount.Value;
        }

        var tableData = await ExtractTableDataAsync(
            wordFile,
            tableIndex,
            new ColumnMapping
            {
                HeaderRowIndex = headerRowIndex,
                HeaderRowCount = headerRowCount,
                DataStartRowIndex = dataStartRowIndex
            },
            maxDataRowCount,
            cancellationToken);
        return DocumentTableQueryAppService.MapPreview(tableData, previewRows, previewRangeRowCount);
    }

    /// <summary>
    /// 提取匹配来源行，并将解析器内部行号换算为后续写回使用的表格行号。
    /// </summary>
    public async Task<List<MatchSourceItem>> ExtractMatchSourceItemsAsync(
        WordFile wordFile,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex,
        int? headerRowStart = null,
        int? headerRowCount = null,
        int? dataStartRow = null,
        int? dataEndRow = null,
        bool filterEmptySourceRows = true,
        CancellationToken cancellationToken = default)
    {
        TableData tableData;
        var excelDataStartRowIndexForWriteBack = 1;
        int? maxDataRowCount = null;
        try
        {
            var snapshot = await _snapshotProvider.GetSnapshotAsync(wordFile, cancellationToken);
            if (tableIndex < 0 || tableIndex >= snapshot.Tables.Count)
            {
                return [];
            }

            var mapping = new ColumnMapping
            {
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            };

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                var sheetInfo = snapshot.Tables[tableIndex];
                var usedStartRow = Math.Max(1, sheetInfo.UsedRangeStartRow);

                var normalizedHeaderRowStart = headerRowStart.GetValueOrDefault(usedStartRow);
                if (normalizedHeaderRowStart < usedStartRow)
                {
                    normalizedHeaderRowStart = usedStartRow;
                }

                var normalizedHeaderRowCount = headerRowCount.GetValueOrDefault(1);
                if (normalizedHeaderRowCount < 0)
                {
                    normalizedHeaderRowCount = 0;
                }

                var minDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
                var normalizedDataStartRow = dataStartRow.GetValueOrDefault(minDataStartRow);
                if (normalizedDataStartRow < minDataStartRow)
                {
                    normalizedDataStartRow = minDataStartRow;
                }

                mapping = new ColumnMapping
                {
                    HeaderRowIndex = Math.Max(0, normalizedHeaderRowStart - usedStartRow),
                    HeaderRowCount = Math.Max(1, normalizedHeaderRowCount == 0 ? 1 : normalizedHeaderRowCount),
                    DataStartRowIndex = Math.Max(0, normalizedDataStartRow - usedStartRow)
                };
                excelDataStartRowIndexForWriteBack = mapping.DataStartRowIndex;
                if (dataEndRow.HasValue)
                {
                    var normalizedDataEndRow = Math.Max(normalizedDataStartRow, dataEndRow.Value);
                    maxDataRowCount = normalizedDataEndRow - normalizedDataStartRow + 1;
                }
            }
            else if (headerRowStart.HasValue || headerRowCount.HasValue || dataStartRow.HasValue || dataEndRow.HasValue)
            {
                var normalizedHeaderRowStart = Math.Max(1, headerRowStart.GetValueOrDefault(1));
                var normalizedHeaderRowCount = Math.Max(0, headerRowCount.GetValueOrDefault(1));
                var minDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
                var normalizedDataStartRow = Math.Max(
                    minDataStartRow,
                    dataStartRow.GetValueOrDefault(minDataStartRow));
                mapping = new ColumnMapping
                {
                    HeaderRowIndex = normalizedHeaderRowStart - 1,
                    HeaderRowCount = Math.Max(1, normalizedHeaderRowCount == 0 ? 1 : normalizedHeaderRowCount),
                    DataStartRowIndex = normalizedDataStartRow - 1
                };
                if (dataEndRow.HasValue)
                {
                    if (dataEndRow.Value < normalizedDataStartRow)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(dataEndRow),
                            "数据结束行不能早于数据起始行");
                    }

                    maxDataRowCount = dataEndRow.Value - normalizedDataStartRow + 1;
                }
            }

            tableData = ProjectFromSnapshot(
                snapshot,
                tableIndex,
                mapping,
                maxDataRowCount,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }
        catch (Exception ex)
        {
            throw CreateDocumentParsingException(wordFile, tableIndex, ex);
        }

        if (tableData.ColumnCount < 2 ||
            projectColumnIndex < 0 || projectColumnIndex >= tableData.ColumnCount ||
            specificationColumnIndex < 0 || specificationColumnIndex >= tableData.ColumnCount)
        {
            return [];
        }

        var items = new List<MatchSourceItem>();
        foreach (var row in tableData.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = row.GetValue(projectColumnIndex) ?? string.Empty;
            var specification = row.GetValue(specificationColumnIndex) ?? string.Empty;

            if (filterEmptySourceRows &&
                string.IsNullOrWhiteSpace(project) &&
                string.IsNullOrWhiteSpace(specification))
            {
                continue;
            }

            var writeBackRowIndex = row.Index;
            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                writeBackRowIndex += excelDataStartRowIndexForWriteBack;
            }

            items.Add(new MatchSourceItem
            {
                RowIndex = writeBackRowIndex,
                Project = project.Trim(),
                Specification = specification.Trim()
            });
        }

        return items;
    }

    /// <summary>
    /// 按批量回复表配置提取来源回复数据；返回的 RowIndex 必须与目标写回坐标一致。
    /// </summary>
    public async Task<List<ReplySourceItem>> ExtractReplySourceItemsAsync(
        WordFile wordFile,
        BatchTableConfig config,
        CancellationToken cancellationToken = default)
    {
        TableData tableData;
        var excelDataStartRowIndexForWriteBack = 1;
        int? maxDataRowCount = null;
        try
        {
            var snapshot = await _snapshotProvider.GetSnapshotAsync(wordFile, cancellationToken);
            if (config.TableIndex < 0 || config.TableIndex >= snapshot.Tables.Count)
            {
                return [];
            }

            var mapping = new ColumnMapping
            {
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            };

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                var sheetInfo = snapshot.Tables[config.TableIndex];
                var usedStartRow = Math.Max(1, sheetInfo.UsedRangeStartRow);
                var normalizedHeaderRowStart = Math.Max(
                    usedStartRow,
                    config.HeaderRowStart.GetValueOrDefault(usedStartRow));
                var normalizedHeaderRowCount = Math.Max(0, config.HeaderRowCount.GetValueOrDefault(1));
                var minDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
                var normalizedDataStartRow = Math.Max(
                    minDataStartRow,
                    config.DataStartRow.GetValueOrDefault(minDataStartRow));

                mapping = new ColumnMapping
                {
                    HeaderRowIndex = Math.Max(0, normalizedHeaderRowStart - usedStartRow),
                    HeaderRowCount = Math.Max(1, normalizedHeaderRowCount == 0 ? 1 : normalizedHeaderRowCount),
                    DataStartRowIndex = Math.Max(0, normalizedDataStartRow - usedStartRow)
                };
                excelDataStartRowIndexForWriteBack = mapping.DataStartRowIndex;
                if (config.DataEndRow.HasValue)
                {
                    var normalizedDataEndRow = Math.Max(normalizedDataStartRow, config.DataEndRow.Value);
                    maxDataRowCount = normalizedDataEndRow - normalizedDataStartRow + 1;
                }
            }

            tableData = ProjectFromSnapshot(
                snapshot,
                config.TableIndex,
                mapping,
                maxDataRowCount,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }
        catch (Exception ex)
        {
            throw CreateDocumentParsingException(wordFile, config.TableIndex, ex);
        }

        var requiredColumns = new[]
        {
            config.ProjectColumnIndex,
            config.SpecificationColumnIndex,
            config.AcceptanceColumnIndex,
            config.RemarkColumnIndex ?? -1
        };
        if (requiredColumns.Any(index => index >= tableData.ColumnCount))
        {
            return [];
        }

        var filterEmptySourceRows = config.FilterEmptySourceRows ?? true;
        var items = new List<ReplySourceItem>();
        foreach (var row in tableData.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = row.GetValue(config.ProjectColumnIndex) ?? string.Empty;
            var specification = row.GetValue(config.SpecificationColumnIndex) ?? string.Empty;
            if (filterEmptySourceRows &&
                string.IsNullOrWhiteSpace(project) &&
                string.IsNullOrWhiteSpace(specification))
            {
                continue;
            }

            var writeBackRowIndex = row.Index;
            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                writeBackRowIndex += excelDataStartRowIndexForWriteBack;
            }

            items.Add(new ReplySourceItem
            {
                RowIndex = writeBackRowIndex,
                Project = project.Trim(),
                Specification = specification.Trim(),
                Acceptance = (row.GetValue(config.AcceptanceColumnIndex) ?? string.Empty).Trim(),
                Remark = config.RemarkColumnIndex.HasValue
                    ? (row.GetValue(config.RemarkColumnIndex.Value) ?? string.Empty).Trim()
                    : null
            });
        }

        return items;
    }

    private async Task<TableData> ExtractProjectedTableDataAsync(
        WordFile wordFile,
        int tableIndex,
        ColumnMapping mapping,
        int? maxDataRowCount,
        CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotProvider.GetSnapshotAsync(wordFile, cancellationToken);
        return ProjectFromSnapshot(snapshot, tableIndex, mapping, maxDataRowCount, cancellationToken);
    }

    private static TableData ProjectFromSnapshot(
        DocumentTableSnapshot snapshot,
        int tableIndex,
        ColumnMapping mapping,
        int? maxDataRowCount,
        CancellationToken cancellationToken)
    {
        if (tableIndex < 0 || tableIndex >= snapshot.TableData.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(tableIndex));
        }

        var projected = TableDataProjection.Project(
            snapshot.TableData[tableIndex],
            mapping,
            cancellationToken);
        return ApplyMaxDataRowCount(projected, maxDataRowCount);
    }

    private static TableData ApplyMaxDataRowCount(TableData tableData, int? maxDataRowCount)
    {
        if (!maxDataRowCount.HasValue || tableData.Rows.Count <= maxDataRowCount.Value)
        {
            return tableData;
        }

        return new TableData
        {
            TableIndex = tableData.TableIndex,
            Headers = tableData.Headers.ToList(),
            Rows = tableData.Rows.Take(maxDataRowCount.Value).Select(DocumentTableSnapshotCloner.CloneRow).ToList(),
            TotalDataRowCount = tableData.TotalDataRowCount,
            OriginalRowCount = tableData.OriginalRowCount,
            MergedCells = tableData.MergedCells
                .Select(merged => new MergedCellInfo
                {
                    StartRow = merged.StartRow,
                    StartColumn = merged.StartColumn,
                    EndRow = merged.EndRow,
                    EndColumn = merged.EndColumn
                })
                .ToList()
        };
    }

    private ApplicationServiceException CreateDocumentParsingException(
        WordFile wordFile,
        int tableIndex,
        Exception exception)
    {
        _logger.LogError(
            exception,
            "文档解析失败: FileId={FileId}, FileType={FileType}, TableIndex={TableIndex}, ExceptionType={ExceptionType}",
            wordFile.Id,
            wordFile.FileType,
            tableIndex,
            exception.GetType().Name);

        return new ApplicationServiceException(
            400,
            "文档解析失败，请确认文件完整且未被占用");
    }

    private static DocumentType GetDocumentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word;
    }

}
