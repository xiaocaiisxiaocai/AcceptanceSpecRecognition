using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Services;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class BatchReplyExecutionHistoryAdapter : IBatchReplyExecutionHistoryPort
{
    private readonly ExecutionHistoryAppService _executionHistoryAppService;

    public BatchReplyExecutionHistoryAdapter(ExecutionHistoryAppService executionHistoryAppService)
    {
        _executionHistoryAppService = executionHistoryAppService;
    }

    public async Task SaveAsync(
        BatchReplyUserContext user,
        string taskId,
        BatchReplySourceSession session,
        IReadOnlyCollection<BatchReplyTargetFile> targetFiles,
        IReadOnlyCollection<BatchReplyExecuteFileResult> executeResults,
        IReadOnlyDictionary<string, IReadOnlyCollection<BatchReplyWriteTable>> executionHistoryRows,
        CancellationToken cancellationToken = default)
    {
        var resultLookup = executeResults.ToDictionary(item => item.TargetId, item => item);
        var files = targetFiles
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(file =>
            {
                resultLookup.TryGetValue(file.TargetId, out var result);
                var success = result?.Success == true;
                executionHistoryRows.TryGetValue(file.TargetId, out var targetTables);
                var historyTables = targetTables?.Count > 0
                    ? targetTables
                    : BuildSourceFallbackHistoryTables(session.SourceTables);

                return new ExecutionHistoryFileDto
                {
                    FileName = file.FileName,
                    FileType = file.FileType,
                    Sheets = historyTables.OrderBy(table => table.TableIndex).Select(table => new ExecutionHistorySheetDto
                    {
                        SheetIndex = table.TableIndex,
                        SheetName = $"表格 {table.TableIndex + 1}",
                        Rows = table.Rows.OrderBy(row => row.RowIndex).Select(row => new ExecutionHistoryRowDto
                        {
                            RowIndex = row.RowIndex,
                            Project = row.Project,
                            Specification = row.Specification,
                            Acceptance = row.Acceptance,
                            Remark = row.Remark,
                            ConfidencePercent = success ? 100 : 0,
                            Status = success ? ExecutionHistoryStatuses.Adopted : ExecutionHistoryStatuses.Skipped,
                            IsManualSelected = false,
                            AcceptanceColumnIndex = table.AcceptanceColumnIndex,
                            RemarkColumnIndex = table.RemarkColumnIndex
                        }).ToList()
                    }).ToList()
                };
            }).ToList();

        await _executionHistoryAppService.SaveAsync(user, new ExecutionHistoryDraft
        {
            TaskId = taskId,
            TaskType = ExecutionHistoryTaskTypes.BatchReply,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            CreatedAt = DateTime.UtcNow,
            Files = files,
            BatchReplyDetail = new ExecutionHistoryBatchReplyDetailDto { Files = files }
        }, cancellationToken);
    }

    private static IReadOnlyCollection<BatchReplyWriteTable> BuildSourceFallbackHistoryTables(
        IReadOnlyCollection<BatchReplySourceTable> sourceTables) => sourceTables.Select(table => new BatchReplyWriteTable
        {
            TableIndex = table.TableIndex,
            AcceptanceColumnIndex = table.AcceptanceColumnIndex,
            RemarkColumnIndex = table.RemarkColumnIndex,
            Rows = table.Rows.Select(row => new BatchReplyWriteRow
            {
                RowIndex = row.RowIndex,
                Project = row.Project,
                Specification = row.Specification,
                Acceptance = row.Acceptance,
                Remark = row.Remark
            }).ToList()
        }).ToList();
}
