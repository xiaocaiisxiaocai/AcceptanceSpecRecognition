using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配结果写回协作组件。
/// </summary>
public sealed class MatchingResultWriteBackService
{
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly DocumentFileAccessService _documentFileAccessService;

    public MatchingResultWriteBackService(
        DocumentServiceFactory documentServiceFactory,
        DocumentFileAccessService documentFileAccessService)
    {
        _documentServiceFactory = documentServiceFactory;
        _documentFileAccessService = documentFileAccessService;
    }

    internal async Task<WriteBackSummary> ApplyFillResultToSourceFileAsync(
        WordFile wordFile,
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default)
    {
        var rendered = await RenderFillResultToSourceFileAsync(wordFile, taskResult, cancellationToken);

        if (rendered.Summary.WrittenCells > 0)
        {
            await _documentFileAccessService.PersistUpdatedFileContentAsync(
                wordFile,
                rendered.Content,
                cancellationToken);
        }

        return rendered.Summary;
    }

    internal async Task<RenderedWriteBackFile> RenderFillResultToSourceFileAsync(
        WordFile wordFile,
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default)
    {
        using var resultStream = new MemoryStream();
        await using (var sourceStream = _documentFileAccessService.OpenReadStream(wordFile))
        {
            await sourceStream.CopyToAsync(resultStream, cancellationToken);
        }

        resultStream.Position = 0;
        var (requestedCells, writtenCells) = await WriteOperationsAsync(
            resultStream,
            taskResult,
            GetRequiredWriter(wordFile.FileType));

        return new RenderedWriteBackFile(resultStream.ToArray(), new WriteBackSummary(requestedCells, writtenCells));
    }

    internal async Task<byte[]> RenderFilledContentAsync(
        WordFile wordFile,
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default)
    {
        using var resultStream = new MemoryStream();
        await using (var sourceStream = _documentFileAccessService.OpenReadStream(wordFile))
        {
            await sourceStream.CopyToAsync(resultStream, cancellationToken);
        }

        resultStream.Position = 0;
        await WriteOperationsAsync(resultStream, taskResult, GetRequiredWriter(wordFile.FileType));
        return resultStream.ToArray();
    }

    internal async Task<GeneratedArtifactFile> GenerateBatchReplyTargetFileAsync(
        WordFile targetFile,
        IReadOnlyCollection<BatchReplyWriteTable> writeTables,
        CancellationToken cancellationToken = default)
    {
        using var resultStream = new MemoryStream();
        await using (var sourceStream = _documentFileAccessService.OpenReadStream(targetFile))
        {
            await sourceStream.CopyToAsync(resultStream, cancellationToken);
        }

        resultStream.Position = 0;
        var tableOperations = writeTables
            .Select(table => new
            {
                table.TableIndex,
                Operations = BuildReplyCellWriteOperations(table.Rows, table.AcceptanceColumnIndex, table.RemarkColumnIndex)
            })
            .Where(item => item.Operations.Count > 0)
            .ToDictionary(item => item.TableIndex, item => item.Operations);

        if (tableOperations.Count == 0)
        {
            throw new InvalidOperationException("来源回复结果为空，无法执行批量回复");
        }

        var requestedCells = tableOperations.Sum(item => item.Value.Count);
        var writtenCells = await GetRequiredWriter(targetFile.FileType)
            .WriteMultipleTablesAsync(resultStream, tableOperations);
        if (writtenCells != requestedCells)
        {
            throw new InvalidOperationException($"目标文件写回不完整，期望写入{requestedCells}个单元格，实际写入{writtenCells}个");
        }

        return new GeneratedArtifactFile
        {
            FileId = targetFile.Id,
            FileName = targetFile.FileName,
            ContentType = GetDownloadContentType(targetFile.FileType),
            Content = resultStream.ToArray()
        };
    }

    internal static string GetDownloadContentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    private async Task<(int RequestedCells, int WrittenCells)> WriteOperationsAsync(
        MemoryStream resultStream,
        FillTaskResult taskResult,
        IDocumentWriter writer)
    {
        var requestedCells = 0;
        var writtenCells = 0;

        // 批量模式按表格分组写回，单表模式写入 SourceTableIndex；
        // 两条路径共用单元格构造逻辑，确保验收/备注列处理口径一致。
        if (taskResult.IsBatchMode)
        {
            var tableOperations = new Dictionary<int, List<CellWriteOperation>>();
            foreach (var entry in taskResult.TableEntries)
            {
                var operations = BuildCellWriteOperations(
                    entry.FillResults,
                    entry.AcceptanceColumnIndex,
                    entry.RemarkColumnIndex);
                if (operations.Count == 0)
                {
                    continue;
                }

                requestedCells += operations.Count;
                tableOperations[entry.TableIndex] = operations;
            }

            if (tableOperations.Count > 0)
            {
                writtenCells = await writer.WriteMultipleTablesAsync(resultStream, tableOperations);
            }
        }
        else
        {
            var operations = BuildCellWriteOperations(
                taskResult.FillResults,
                taskResult.AcceptanceColumnIndex ?? 0,
                taskResult.RemarkColumnIndex);
            if (operations.Count > 0)
            {
                requestedCells = operations.Count;
                writtenCells = await writer.WriteTableDataAsync(resultStream, taskResult.SourceTableIndex, operations);
            }
        }

        return (requestedCells, writtenCells);
    }

    private IDocumentWriter GetRequiredWriter(UploadedFileType fileType)
    {
        var writer = _documentServiceFactory.GetWriter(fileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word);
        if (writer == null)
        {
            throw new InvalidOperationException(fileType == UploadedFileType.ExcelXlsx
                ? "Excel 文档写入器不可用"
                : "文档写入器不可用");
        }

        return writer;
    }

    private static List<CellWriteOperation> BuildCellWriteOperations(
        List<FillResult> fillResults,
        int acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        var operations = new List<CellWriteOperation>();
        foreach (var fillResult in fillResults)
        {
            operations.Add(new CellWriteOperation
            {
                RowIndex = fillResult.RowIndex,
                ColumnIndex = acceptanceColumnIndex,
                Value = fillResult.Acceptance,
                PreserveFormatting = true
            });

            if (remarkColumnIndex.HasValue &&
                remarkColumnIndex.Value != acceptanceColumnIndex)
            {
                operations.Add(new CellWriteOperation
                {
                    RowIndex = fillResult.RowIndex,
                    ColumnIndex = remarkColumnIndex.Value,
                    Value = fillResult.Remark ?? string.Empty,
                    PreserveFormatting = true
                });
            }
        }

        return operations;
    }

    private static List<CellWriteOperation> BuildReplyCellWriteOperations(
        IReadOnlyCollection<BatchReplyWriteRow> sourceRows,
        int acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        var operations = new List<CellWriteOperation>();
        foreach (var row in sourceRows)
        {
            operations.Add(new CellWriteOperation
            {
                RowIndex = row.RowIndex,
                ColumnIndex = acceptanceColumnIndex,
                Value = row.Acceptance,
                PreserveFormatting = true
            });

            if (remarkColumnIndex.HasValue &&
                remarkColumnIndex.Value != acceptanceColumnIndex)
            {
                operations.Add(new CellWriteOperation
                {
                    RowIndex = row.RowIndex,
                    ColumnIndex = remarkColumnIndex.Value,
                    Value = row.Remark ?? string.Empty,
                    PreserveFormatting = true
                });
            }
        }

        return operations;
    }
}

internal readonly record struct RenderedWriteBackFile(byte[] Content, WriteBackSummary Summary);
