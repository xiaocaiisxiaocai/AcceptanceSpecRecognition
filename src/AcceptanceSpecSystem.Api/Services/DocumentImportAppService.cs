using System.Text;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 文档导入应用服务。
/// </summary>
public sealed class DocumentImportAppService
{
    private const string MatchTypeExact = "exact";
    private const string MatchTypeConflict = "conflict";
    private const string MatchTypeSemantic = "semantic";

    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly ImportDuplicateDetectionService _importDuplicateDetectionService;
    private readonly ILogger<DocumentImportAppService> _logger;

    public DocumentImportAppService(
        IUnitOfWork unitOfWork,
        DocumentFileAccessService documentFileAccessService,
        DocumentTableAccessService documentTableAccessService,
        ImportDuplicateDetectionService importDuplicateDetectionService,
        ILogger<DocumentImportAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _importDuplicateDetectionService = importDuplicateDetectionService;
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

    private async Task ValidateImportTargetAsync(int customerId, int? processId, int? machineModelId)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
        if (customer == null)
        {
            throw new ApplicationServiceException(400, "客户不存在");
        }

        if (processId.HasValue)
        {
            var process = await _unitOfWork.Processes.GetByIdAsync(processId.Value);
            if (process == null)
            {
                throw new ApplicationServiceException(400, "制程不存在");
            }
        }

        if (machineModelId.HasValue)
        {
            var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(machineModelId.Value);
            if (machineModel == null)
            {
                throw new ApplicationServiceException(400, "机型不存在");
            }
        }
    }

    private async Task<DocumentImportAppResult> ExecuteImportAsync(
        DataScopeResult scope,
        WordFile wordFile,
        int sourceIndex,
        int customerId,
        int? processId,
        int? machineModelId,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys,
        IEnumerable<int>? excludedRowIndexes,
        ImportDuplicateCheckOptions? duplicateCheckOptions,
        bool previewSkippedRows,
        bool cleanupSourceFile,
        TableData tableData,
        Func<RowData, ImportRowPayload> rowPayloadFactory,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        var result = new ImportResult
        {
            TotalCount = tableData.Rows.Count
        };

        var excludedSet = (excludedRowIndexes ?? [])
            .Where(index => index >= 0)
            .ToHashSet();
        if (excludedSet.Count > 0)
        {
            result.TotalCount = Math.Max(0, tableData.Rows.Count - tableData.Rows.Count(row => excludedSet.Contains(row.Index)));
        }

        var existingSpecsInScope = await LoadExistingSpecsForImportAsync(
            customerId,
            processId,
            machineModelId,
            scope,
            cancellationToken);
        var duplicateSession = await CreateDuplicateDetectionSessionAsync(
            existingSpecsInScope,
            confirmedDifferenceKeys,
            partiallyConfirmedDifferenceKeys,
            skippedDifferenceKeys,
            duplicateCheckOptions,
            cancellationToken);
        var executionContext = CreateImportExecutionContext(
            result,
            existingSpecsInScope,
            confirmedDifferenceKeys,
            partiallyConfirmedDifferenceKeys,
            skippedDifferenceKeys,
            duplicateSession,
            customerId,
            processId,
            machineModelId,
            wordFile.Id,
            scope.UserId,
            scope.OrgUnitId,
            previewSkippedRows);

        foreach (var row in tableData.Rows)
        {
            if (excludedSet.Contains(row.Index))
            {
                continue;
            }

            var payload = rowPayloadFactory(row);
            try
            {
                await ProcessImportRowAsync(executionContext, sourceIndex, payload, cancellationToken);
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
                    RowIndex = payload.RowIndex,
                    Message = ex.Message
                });
            }
        }

        if (result.PendingCount > 0)
        {
            return new DocumentImportAppResult(
                result,
                $"检测到{result.PendingCount}条重复或疑似重复数据，请逐条确认后再导入");
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

        if (cleanupSourceFile)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
                {
                    await using var stream = _documentFileAccessService.OpenReadStream(wordFile);
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream, cancellationToken);
                    wordFile.FileContent = memoryStream.ToArray();
                    await _documentFileAccessService.DeleteIfExistsAsync(wordFile.FilePath, cancellationToken);
                    wordFile.FilePath = null;
                }

                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{SourceLabel}导入后清理源文件失败: fileId={FileId}", sourceLabel, wordFile.Id);
            }
        }

        _logger.LogInformation(
            "{SourceLabel}导入完成: 文件{FileId}, 索引{SourceIndex}, 客户{CustomerId}, 制程{ProcessId}, 机型{MachineModelId}, 成功{Success}, 失败{Failed}, 跳过{Skipped}",
            sourceLabel,
            wordFile.Id,
            sourceIndex,
            customerId,
            processId,
            machineModelId,
            result.SuccessCount,
            result.FailedCount,
            result.SkippedCount);

        return new DocumentImportAppResult(
            result,
            $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条");
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
            AddSkippedRow(context, row.RowIndex, "数据库中已存在完全相同内容，已自动跳过", row.RowValues);
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

            // 确认回放时，只重跑有待确认项的工作表；其它工作表可能已先落库。
            // 同一文件内后续发现的相同项目/规格，直接保留已落库首条，避免反复弹确认框。
            if (IsSameFileConfirmationReplayConflict(context, projectConflict))
            {
                AddSkippedRow(context, row.RowIndex, "同一文件已导入相同项目与规格，确认回放时已自动保留首条", row.RowValues);
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
            .Where(spec =>
                spec.CustomerId == customerId &&
                spec.ProcessId == processId &&
                spec.MachineModelId == machineModelId)
            .OrderBy(spec => spec.Id)
            .ToListAsync(cancellationToken);
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

    private static bool IsSameFileConfirmationReplayConflict(
        ImportExecutionContext context,
        AcceptanceSpec existingSpec)
    {
        return context.IsConfirmationReplay &&
               existingSpec.WordFileId == context.FileId;
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

    private static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

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

    private static string? GetCellValue(RowData row, int columnIndex)
    {
        return row.GetValue(columnIndex);
    }

    private static List<string> GetRowValues(RowData row)
    {
        if (row.Cells == null || row.Cells.Count == 0)
        {
            return [];
        }

        var maxColumnIndex = row.Cells.Max(cell => cell.ColumnIndex);
        var valuesByColumn = row.Cells
            .GroupBy(cell => cell.ColumnIndex)
            .ToDictionary(group => group.Key, group => group.FirstOrDefault()?.Value ?? string.Empty);

        var values = new List<string>(maxColumnIndex + 1);
        for (var col = 0; col <= maxColumnIndex; col++)
        {
            values.Add(valuesByColumn.TryGetValue(col, out var value) ? value : string.Empty);
        }

        return values;
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

        public bool IsConfirmationReplay => SkipSemanticDetection;
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

public sealed record DocumentImportAppResult(ImportResult Result, string Message);
