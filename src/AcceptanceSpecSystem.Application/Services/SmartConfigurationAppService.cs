using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 智能结构配置应用服务。
/// </summary>
public interface ISmartConfigurationAppService
{
    Task<SmartConfigurationRecognizeResult> RecognizeAsync(
        SmartConfigurationRecognizeCommand command,
        CancellationToken cancellationToken = default);

    Task<SmartConfigurationConfirmResult> ConfirmAsync(
        SmartConfigurationConfirmCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 智能结构配置应用服务。
/// </summary>
public sealed class SmartConfigurationAppService : ISmartConfigurationAppService
{
    private const int MaxConfirmedHeaderCount = 512;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> CustomerConfirmLocks = new();
    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IDocumentIntelligenceService _intelligenceService;
    private readonly ILlmDocumentStructureAdjudicationService _structureAdjudicationService;
    private readonly ILlmColumnSemanticRecallService _columnSemanticRecallService;
    private readonly DocumentTemplateAppService _templateService;
    private readonly SmartConfigurationLearningService _learningService;
    private readonly IUploadedDocumentPathResolver _documentPathResolver;
    private readonly ISmartConfigurationFileAccessService _fileAccessService;
    private readonly ILogger<SmartConfigurationAppService> _logger;
    private readonly SmartConfigurationOptions _options;

    public SmartConfigurationAppService(
        IUnitOfWork unitOfWork,
        DocumentServiceFactory documentServiceFactory,
        IDocumentIntelligenceService intelligenceService,
        ILlmDocumentStructureAdjudicationService structureAdjudicationService,
        ILlmColumnSemanticRecallService columnSemanticRecallService,
        DocumentTemplateAppService templateService,
        SmartConfigurationLearningService learningService,
        IUploadedDocumentPathResolver documentPathResolver,
        ISmartConfigurationFileAccessService fileAccessService,
        ILogger<SmartConfigurationAppService> logger,
        IOptions<SmartConfigurationOptions> options)
    {
        _unitOfWork = unitOfWork;
        _documentServiceFactory = documentServiceFactory;
        _intelligenceService = intelligenceService;
        _structureAdjudicationService = structureAdjudicationService;
        _columnSemanticRecallService = columnSemanticRecallService;
        _templateService = templateService;
        _learningService = learningService;
        _documentPathResolver = documentPathResolver;
        _fileAccessService = fileAccessService;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// 识别已上传文件的全文档表格结构，返回扁平表格列表。
    /// </summary>
    public async Task<SmartConfigurationRecognizeResult> RecognizeAsync(
        SmartConfigurationRecognizeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.FileId <= 0)
        {
            throw new ApplicationServiceException(400, "FileId 不能为空");
        }

        var file = await _fileAccessService.GetAccessibleFileAsync(command.FileId, cancellationToken);
        if (file == null)
        {
            throw new ApplicationServiceException(404, $"文件不存在：{command.FileId}");
        }

        if (string.IsNullOrWhiteSpace(file.FilePath))
        {
            throw new ApplicationServiceException(400, "文件路径为空");
        }

        if (command.CustomerId.HasValue &&
            !await _fileAccessService.CanAccessCustomerAsync(command.CustomerId.Value, cancellationToken))
        {
            // 客户级模板和学习规则同样属于规格数据范围；不能仅凭文件可访问就套用其他客户的结构。
            throw new ApplicationServiceException(404, $"客户不存在或无权访问：{command.CustomerId.Value}");
        }

        var documentType = file.FileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word;
        var parser = _documentServiceFactory.GetParser(documentType)
            ?? throw new ApplicationServiceException(400, "文档解析器不可用");

        var absolutePath = _documentPathResolver.ResolveAbsolutePath(file.FilePath);
        var tablesInfo = await parser.GetTablesAsync(absolutePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = File.OpenRead(absolutePath);
        var tablesData = await parser.ExtractAllTablesDataAsync(stream, cancellationToken);

        var columnHeaderRuleSets = await BuildColumnHeaderRuleSetsAsync(
            command.CustomerId,
            cancellationToken);
        var columnHeaderRules = columnHeaderRuleSets.All;
        var routingRules = await _unitOfWork.SmartStructureRoutingRules.GetEffectiveForCustomerAsync(
            command.CustomerId,
            cancellationToken);
        var headerKeywordMatcher = HeaderKeywordMatcher.FromRules(columnHeaderRules);
        var fieldConflictMatcher = HeaderKeywordMatcher.FromRules(
            columnHeaderRuleSets.ConflictEligible);
        var tables = new List<SmartConfigurationRecognizedTable>();
        var llmAssistanceEnabled = command.EnableLlmAssistance && command.LlmServiceId.HasValue;
        var llmCallBudget = llmAssistanceEnabled
            ? Math.Max(0, _options.MaxLlmCallsPerRecognizeDocument)
            : 0;
        var structureAdjudicationBudget = llmAssistanceEnabled
            ? Math.Max(0, _options.MaxStructureAdjudicationCallsPerDocument)
            : 0;
        var columnSemanticRecallBudget = llmAssistanceEnabled
            ? Math.Max(0, _options.MaxColumnSemanticRecallCallsPerDocument)
            : 0;
        var llmCircuitOpen = false;
        string? llmAssistanceIssue = command.EnableLlmAssistance && !command.LlmServiceId.HasValue
            ? "AI 增强未执行：请选择一个可用的 LLM 服务"
            : null;
        void OpenLlmCircuit(Exception exception)
        {
            if (llmCircuitOpen)
            {
                return;
            }

            llmCircuitOpen = true;
            llmCallBudget = 0;
            structureAdjudicationBudget = 0;
            columnSemanticRecallBudget = 0;
            llmAssistanceIssue = BuildLlmAssistanceFailureMessage(exception);
        }
        var structureAdjudicationCache =
            new Dictionary<string, DocumentStructureCandidate?>(
                StringComparer.Ordinal);
        var columnSemanticRecallCache =
            new Dictionary<string, IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>>(
                StringComparer.Ordinal);
        for (var i = 0; i < tablesData.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tableData = tablesData[i];
            var fullTableData = tableData;
            var tableInfo = tablesInfo.FirstOrDefault(table => table.Index == tableData.TableIndex)
                ?? tablesInfo.ElementAtOrDefault(i);

            var headerProfile = DetectHeaderProfile(tableData, headerKeywordMatcher);
            if (headerProfile.HeaderRowIndex > 0 || headerProfile.HeaderRowCount > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var tableStream = File.OpenRead(absolutePath);
                tableData = await parser.ExtractTableDataAsync(
                    tableStream,
                    tableData.TableIndex,
                    new ColumnMapping
                    {
                        HeaderRowIndex = headerProfile.HeaderRowIndex,
                        HeaderRowCount = headerProfile.HeaderRowCount,
                        DataStartRowIndex = headerProfile.HeaderRowIndex + headerProfile.HeaderRowCount
                    },
                    cancellationToken: cancellationToken);
            }

            var recognizedTable = await RecognizeTableAsync(
                command.CustomerId,
                parser,
                absolutePath,
                tableInfo,
                tableData,
                fullTableData,
                headerProfile,
                headerKeywordMatcher,
                columnHeaderRules,
                routingRules,
                command.LlmServiceId,
                () => !llmCircuitOpen && TryConsumeLlmBudget(ref llmCallBudget, ref structureAdjudicationBudget),
                () => !llmCircuitOpen && TryConsumeLlmBudget(ref llmCallBudget, ref columnSemanticRecallBudget),
                OpenLlmCircuit,
                structureAdjudicationCache,
                columnSemanticRecallCache,
                cancellationToken);
            recognizedTable = DetectLogicalRegions(fullTableData, recognizedTable, headerKeywordMatcher);
            recognizedTable = AttachFieldConflicts(
                fullTableData,
                recognizedTable,
                headerKeywordMatcher,
                fieldConflictMatcher);
            tables.Add(AddLlmAssistanceIssue(recognizedTable, llmAssistanceIssue));
        }

        return new SmartConfigurationRecognizeResult
        {
            FileId = command.FileId,
            Tables = tables
        };
    }

    private SmartConfigurationRecognizedTable DetectLogicalRegions(
        TableData fullTableData,
        SmartConfigurationRecognizedTable recognizedTable,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        if (string.Equals(recognizedTable.Source, "Template", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateTemplateRegions(fullTableData, recognizedTable, headerKeywordMatcher);
        }

        return DetectLogicalRegionsFromCurrentStructure(
            fullTableData,
            recognizedTable,
            headerKeywordMatcher);
    }

    private static SmartConfigurationRecognizedTable AttachFieldConflicts(
        TableData fullTableData,
        SmartConfigurationRecognizedTable table,
        HeaderKeywordMatcher mappingMatcher,
        HeaderKeywordMatcher conflictMatcher)
    {
        if (table.Regions.Count == 0)
        {
            return table;
        }

        var detectionTable = BuildHeaderDetectionTableData(fullTableData);
        var regions = table.Regions.Select(region =>
        {
            region = PreferPerRowWritableTargetColumns(detectionTable, region, mappingMatcher);
            var conflicts = BuildFieldConflicts(detectionTable, region, conflictMatcher);
            if (conflicts.Count == 0)
            {
                return region;
            }

            var issues = region.Issues.ToList();
            foreach (var conflict in conflicts)
            {
                if (issues.Any(issue =>
                        issue.Code == "AmbiguousFieldCandidates" &&
                        string.Equals(issue.Field, conflict.Field, StringComparison.Ordinal)))
                {
                    continue;
                }

                issues.Add(new SmartConfigurationRecognitionIssue
                {
                    Code = "AmbiguousFieldCandidates",
                    Severity = "Warning",
                    Field = conflict.Field,
                    Message = $"{GetFieldDisplayName(conflict.Field)}存在多个同分高置信候选，请选择最终列"
                });
            }

            return region with
            {
                Decision = "NeedConfirm",
                Issues = issues,
                FieldConflicts = conflicts
            };
        }).ToList();
        var primary = regions[0];
        return table with
        {
            Decision = regions.Any(region => region.FieldConflicts.Count > 0)
                ? "NeedConfirm"
                : table.Decision,
            Fields = primary.Fields,
            FieldConflicts = regions.SelectMany(region => region.FieldConflicts).ToList(),
            Regions = regions
        };
    }

    private static SmartConfigurationRecognizedRegion PreferPerRowWritableTargetColumns(
        TableData detectionTable,
        SmartConfigurationRecognizedRegion region,
        HeaderKeywordMatcher matcher)
    {
        var acceptanceColumnIndex = SelectPerRowWritableTargetColumn(
            detectionTable,
            region,
            matcher,
            ColumnType.Acceptance,
            region.AcceptanceColumnIndex);
        var remarkColumnIndex = SelectPerRowWritableTargetColumn(
            detectionTable,
            region,
            matcher,
            ColumnType.Remark,
            region.RemarkColumnIndex);
        if (acceptanceColumnIndex == region.AcceptanceColumnIndex &&
            remarkColumnIndex == region.RemarkColumnIndex)
        {
            return region;
        }

        var fields = region.Fields.Select(field =>
        {
            var columnIndex = field.Field switch
            {
                "Acceptance" => acceptanceColumnIndex,
                "Remark" => remarkColumnIndex,
                _ => field.ColumnIndex
            };
            return new SmartConfigurationRecognizedField
            {
                Field = field.Field,
                ColumnIndex = columnIndex,
                Header = field.Field is "Acceptance" or "Remark"
                    ? GetHeader(region.Headers, columnIndex)
                    : field.Header,
                Confidence = field.Confidence,
                Source = field.Source
            };
        }).ToList();

        return region with
        {
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex,
            Fields = fields
        };
    }

    private static int? SelectPerRowWritableTargetColumn(
        TableData detectionTable,
        SmartConfigurationRecognizedRegion region,
        HeaderKeywordMatcher matcher,
        ColumnType columnType,
        int? selectedColumnIndex)
    {
        if (selectedColumnIndex.HasValue &&
            !HasDominantVerticalMergeAcrossDataRows(
                detectionTable,
                region,
                selectedColumnIndex.Value))
        {
            return selectedColumnIndex;
        }

        const double highConfidenceThreshold = 0.95;
        return region.Headers
            .Select((header, columnIndex) => new
            {
                ColumnIndex = columnIndex,
                Rank = matcher.GetRank(columnType, header)
            })
            .Where(item =>
                item.Rank.Confidence >= highConfidenceThreshold &&
                !HasDominantVerticalMergeAcrossDataRows(
                    detectionTable,
                    region,
                    item.ColumnIndex))
            .OrderByDescending(item => item.Rank.Confidence)
            .ThenByDescending(item => item.Rank.IsCustomerSpecific)
            .ThenByDescending(item => item.Rank.Priority)
            .ThenBy(item => item.ColumnIndex)
            .Select(item => (int?)item.ColumnIndex)
            .FirstOrDefault();
    }

    private static bool HasDominantVerticalMergeAcrossDataRows(
        TableData detectionTable,
        SmartConfigurationRecognizedRegion region,
        int columnIndex)
    {
        var dataEndRowIndex = region.DataEndRowIndex ??
                              Math.Max(region.DataStartRowIndex, detectionTable.Rows.Count - 1);
        var dataRowCount = Math.Max(1, dataEndRowIndex - region.DataStartRowIndex + 1);
        return detectionTable.MergedCells.Any(merged =>
        {
            if (!merged.IsVerticalMerge ||
                columnIndex < merged.StartColumn ||
                columnIndex > merged.EndColumn)
            {
                return false;
            }

            var overlapStart = Math.Max(region.DataStartRowIndex, merged.StartRow);
            var overlapEnd = Math.Min(dataEndRowIndex, merged.EndRow);
            var overlapRowCount = overlapEnd - overlapStart + 1;
            return overlapRowCount > 1 && overlapRowCount * 2 >= dataRowCount;
        });
    }

    private static string? GetHeader(IReadOnlyList<string> headers, int? columnIndex)
    {
        return columnIndex.HasValue &&
               columnIndex.Value >= 0 &&
               columnIndex.Value < headers.Count
            ? headers[columnIndex.Value]
            : null;
    }

    private static List<SmartConfigurationFieldConflict> BuildFieldConflicts(
        TableData detectionTable,
        SmartConfigurationRecognizedRegion region,
        HeaderKeywordMatcher matcher)
    {
        const double highConfidenceThreshold = 0.95;
        const double ambiguityMargin = 0.02;
        var fieldDefinitions = new[]
        {
            (Field: "Project", Type: ColumnType.Project, Selected: region.ProjectColumnIndex),
            (Field: "Specification", Type: ColumnType.Specification, Selected: region.SpecificationColumnIndex),
            (Field: "Acceptance", Type: ColumnType.Acceptance, Selected: region.AcceptanceColumnIndex),
            (Field: "Remark", Type: ColumnType.Remark, Selected: region.RemarkColumnIndex)
        };
        var conflicts = new List<SmartConfigurationFieldConflict>();
        foreach (var definition in fieldDefinitions)
        {
            if (definition.Field == "Project" && region.IsSpecificationOnly)
            {
                continue;
            }

            var rankedCandidates = region.Headers
                .Select((header, columnIndex) =>
                {
                    var rank = matcher.GetRank(definition.Type, header);
                    return new
                    {
                        Rank = rank,
                        Candidate = new SmartConfigurationFieldCandidate
                        {
                            ColumnIndex = columnIndex,
                            Header = header?.Trim() ?? string.Empty,
                            Confidence = rank.Confidence,
                            IsRecommended = columnIndex == definition.Selected,
                            Samples = GetFieldCandidateSamples(
                                detectionTable,
                                region,
                                columnIndex)
                        }
                    };
                })
                .Where(item =>
                    item.Candidate.Header.Length > 0 &&
                    item.Rank.Confidence >= highConfidenceThreshold)
                .OrderByDescending(item => item.Rank.Confidence)
                .ThenByDescending(item => item.Rank.IsCustomerSpecific)
                .ThenByDescending(item => item.Rank.Priority)
                .ThenBy(item => item.Candidate.ColumnIndex)
                .ToList();
            if (rankedCandidates.Count < 2)
            {
                continue;
            }

            var bestRank = rankedCandidates[0].Rank;
            var ambiguous = rankedCandidates
                .Where(item =>
                    bestRank.Confidence - item.Rank.Confidence <= ambiguityMargin &&
                    item.Rank.IsCustomerSpecific == bestRank.IsCustomerSpecific &&
                    item.Rank.Priority == bestRank.Priority)
                .Select(item => item.Candidate)
                .GroupBy(
                    candidate => BuildCandidateEquivalenceKey(candidate),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.FirstOrDefault(candidate => candidate.IsRecommended) ??
                    group.OrderBy(candidate => candidate.ColumnIndex).First())
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.ColumnIndex)
                .Take(4)
                .ToList();
            if (ambiguous.Count < 2)
            {
                continue;
            }

            conflicts.Add(new SmartConfigurationFieldConflict
            {
                Field = definition.Field,
                RecommendedColumnIndex = definition.Selected,
                Candidates = ambiguous
            });
        }

        return conflicts;
    }

    private static string BuildCandidateEquivalenceKey(
        SmartConfigurationFieldCandidate candidate)
    {
        var header = string.Concat(candidate.Header
            .Where(character => !char.IsWhiteSpace(character)))
            .ToLowerInvariant();
        var samples = string.Join("\u001f", candidate.Samples.Select(sample =>
            string.Concat(sample.Where(character => !char.IsWhiteSpace(character)))
                .ToLowerInvariant()));
        return $"{header}\u001e{samples}";
    }

    private static List<string> GetFieldCandidateSamples(
        TableData detectionTable,
        SmartConfigurationRecognizedRegion region,
        int columnIndex)
    {
        var endRowIndex = Math.Min(
            region.DataEndRowIndex ?? region.DataStartRowIndex + 30,
            detectionTable.Rows.Count - 1);
        if (endRowIndex < region.DataStartRowIndex)
        {
            return [];
        }

        return Enumerable.Range(
                region.DataStartRowIndex,
                endRowIndex - region.DataStartRowIndex + 1)
            .Select(rowIndex => detectionTable.Rows[rowIndex].GetValue(columnIndex)?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Length <= 80 ? value : $"{value[..77]}…")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    private static string GetFieldDisplayName(string field) => field switch
    {
        "Project" => "项目列",
        "Specification" => "规格列",
        "Acceptance" => "验收列",
        "Remark" => "备注列",
        _ => field
    };

    private SmartConfigurationRecognizedTable DetectLogicalRegionsFromCurrentStructure(
        TableData fullTableData,
        SmartConfigurationRecognizedTable recognizedTable,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        if (!recognizedTable.SpecificationColumnIndex.HasValue ||
            !recognizedTable.DataEndRowIndex.HasValue)
        {
            return recognizedTable;
        }

        var detectionTable = BuildHeaderDetectionTableData(fullTableData);
        if (detectionTable.Rows.Count == 0)
        {
            return recognizedTable;
        }

        var firstDataEnd = FindRegionDataEnd(
            detectionTable,
            recognizedTable.DataStartRowIndex,
            recognizedTable,
            headerKeywordMatcher);
        if (!HasHealthyRegionData(
                detectionTable,
                recognizedTable,
                recognizedTable.DataStartRowIndex,
                firstDataEnd,
                headerKeywordMatcher))
        {
            // 区域扫描没有命中任何可信业务行时，FindRegionDataEnd 的保底值会等于
            // dataStart，不能把原本覆盖整张工作表的闭区间静默压成 start-start。
            // 保留工厂根据工作表元数据计算出的末行，并要求用户确认起始位置。
            var preservedRegion = CreateRegion(
                recognizedTable,
                0,
                recognizedTable.Headers,
                recognizedTable.HeaderRowIndex,
                recognizedTable.HeaderRowCount,
                recognizedTable.DataStartRowIndex,
                recognizedTable.DataEndRowIndex.Value);
            preservedRegion = AddRegionIssue(
                preservedRegion,
                "InvalidDetectedDataStart",
                "识别的数据起始行没有命中有效业务数据，已保留工作表原范围，请确认起始行");
            var hasTemplateStructureChange = recognizedTable.Issues.Any(issue =>
                string.Equals(issue.Code, "TemplateRegionStructureChanged", StringComparison.Ordinal));
            if (recognizedTable.DataEndRowIndex.Value > firstDataEnd &&
                (hasTemplateStructureChange ||
                 Enumerable.Range(firstDataEnd + 1, recognizedTable.DataEndRowIndex.Value - firstDataEnd)
                     .Where(rowIndex => rowIndex >= 0 && rowIndex < detectionTable.Rows.Count)
                     .Any(rowIndex => LooksLikeRegionDataRow(
                         detectionTable.Rows[rowIndex],
                         recognizedTable,
                         headerKeywordMatcher))))
            {
                preservedRegion = AddRegionIssue(
                    preservedRegion,
                    "UnassignedDataAfterGap",
                    "空白带后仍存在疑似业务数据，已保留原范围，请确认区域边界");
            }

            return recognizedTable with
            {
                Decision = "NeedConfirm",
                Regions = [preservedRegion]
            };
        }

        var firstRegion = CreateRegion(
                recognizedTable,
                0,
                recognizedTable.Headers,
                recognizedTable.HeaderRowIndex,
                recognizedTable.HeaderRowCount,
                recognizedTable.DataStartRowIndex,
                firstDataEnd);

        var regions = new List<SmartConfigurationRecognizedRegion>
        {
            firstRegion
        };

        var scanRow = Math.Max(firstDataEnd + 1, recognizedTable.DataStartRowIndex + 1);
        var maxHeaderRowCount = Math.Clamp(_options.MaxHeaderRowCount, 1, 20);
        while (scanRow < detectionTable.Rows.Count)
        {
            var candidateRow = -1;
            for (var rowIndex = scanRow; rowIndex < detectionTable.Rows.Count; rowIndex++)
            {
                if (headerKeywordMatcher.IsCompleteHeader(detectionTable.Rows[rowIndex]) ||
                    LooksLikeMappedHeaderRow(detectionTable.Rows[rowIndex], recognizedTable, headerKeywordMatcher) ||
                    LooksLikeCompositeHeaderStart(
                        detectionTable,
                        rowIndex,
                        maxHeaderRowCount,
                        headerKeywordMatcher))
                {
                    candidateRow = rowIndex;
                    break;
                }
            }

            if (candidateRow < 0)
            {
                break;
            }

            var headerStart = ExpandHeaderStart(
                detectionTable,
                candidateRow,
                maxHeaderRowCount,
                headerKeywordMatcher);
            var headerRowCount = DetectHeaderRowCount(
                detectionTable,
                headerStart,
                maxHeaderRowCount,
                headerKeywordMatcher);
            var compositeHeaders = BuildRegionHeaders(detectionTable, headerStart, headerRowCount);
            var regionTable = ApplyRegionHeaderMappings(
                recognizedTable,
                compositeHeaders,
                headerKeywordMatcher);
            var dataStart = FindNextRegionDataStart(
                detectionTable,
                headerStart + headerRowCount,
                regionTable,
                headerKeywordMatcher);
            if (!dataStart.HasValue || dataStart.Value - (headerStart + headerRowCount) > 4)
            {
                scanRow = candidateRow + 1;
                continue;
            }

            var dataEnd = FindRegionDataEnd(
                detectionTable,
                dataStart.Value,
                regionTable,
                headerKeywordMatcher);
            // 重复数据区域统一以首条数据的上一行作为唯一表头。前面的分组标题
            // 只用于辅助定位，不进入最终确认范围和后续学习模板。
            var leafHeaderRowIndex = dataStart.Value - 1;
            var leafHeaders = BuildRegionHeaders(detectionTable, leafHeaderRowIndex, 1);
            var candidateRegion = CreateRegion(
                regionTable,
                regions.Count,
                leafHeaders,
                leafHeaderRowIndex,
                1,
                dataStart.Value,
                dataEnd);
            if (candidateRegion.Issues.Any(issue =>
                    string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)) ||
                !HasHealthyRegionData(
                    detectionTable,
                    regionTable,
                    dataStart.Value,
                    dataEnd,
                    headerKeywordMatcher))
            {
                scanRow = Math.Max(candidateRow + 1, headerStart + headerRowCount);
                continue;
            }

            regions.Add(candidateRegion);
            scanRow = dataEnd + 1;
        }

        if (regions.Count > 1 &&
            !HasHealthyRegionData(
                detectionTable,
                recognizedTable,
                recognizedTable.DataStartRowIndex,
                firstDataEnd,
                headerKeywordMatcher))
        {
            regions[0] = AddRegionIssue(
                regions[0],
                "UnhealthyRegionData",
                "该区域有效数据不足或有效数据比例过低，请确认范围");
        }

        if (regions.Count > 1)
        {
            AuditRegionCoverage(
                detectionTable,
                recognizedTable,
                regions,
                headerKeywordMatcher);
        }

        // 若没有形成第二个可信区域，不能因为中间两个空行就静默截掉后续仍像业务数据的行。
        // 保留原范围并降级确认，让用户显式决定边界。
        var templateStructureChanged = recognizedTable.Issues.Any(issue =>
            string.Equals(issue.Code, "TemplateRegionStructureChanged", StringComparison.Ordinal));
        if (regions.Count == 1 &&
            recognizedTable.DataEndRowIndex.Value > firstDataEnd &&
            (templateStructureChanged ||
             Enumerable.Range(firstDataEnd + 1, recognizedTable.DataEndRowIndex.Value - firstDataEnd)
                 .Where(rowIndex => rowIndex >= 0 && rowIndex < detectionTable.Rows.Count)
                 .Any(rowIndex => LooksLikeRegionDataRow(
                     detectionTable.Rows[rowIndex],
                     recognizedTable,
                     headerKeywordMatcher))))
        {
            var preservedRegion = CreateRegion(
                recognizedTable,
                0,
                recognizedTable.Headers,
                recognizedTable.HeaderRowIndex,
                recognizedTable.HeaderRowCount,
                recognizedTable.DataStartRowIndex,
                recognizedTable.DataEndRowIndex.Value);
            preservedRegion = AddRegionIssue(
                preservedRegion,
                "UnassignedDataAfterGap",
                "空白带后仍存在疑似业务数据，已保留原范围，请确认区域边界");
            return recognizedTable with
            {
                Decision = "NeedConfirm",
                Regions = [preservedRegion]
            };
        }

        var primaryRegion = regions[0];
        return recognizedTable with
        {
            Headers = primaryRegion.Headers,
            HeaderRowIndex = primaryRegion.HeaderRowIndex,
            HeaderRowCount = primaryRegion.HeaderRowCount,
            DataStartRowIndex = primaryRegion.DataStartRowIndex,
            ProjectColumnIndex = primaryRegion.ProjectColumnIndex,
            SpecificationColumnIndex = primaryRegion.SpecificationColumnIndex,
            AcceptanceColumnIndex = primaryRegion.AcceptanceColumnIndex,
            RemarkColumnIndex = primaryRegion.RemarkColumnIndex,
            IsSpecificationOnly = primaryRegion.IsSpecificationOnly,
            Fields = primaryRegion.Fields,
            DataEndRowIndex = firstDataEnd,
            Regions = regions,
            Decision = regions.Count > 1 || regions.Any(HasErrorIssue)
                ? "NeedConfirm"
                : recognizedTable.Decision
        };
    }

    private SmartConfigurationRecognizedTable ValidateTemplateRegions(
        TableData fullTableData,
        SmartConfigurationRecognizedTable recognizedTable,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        if (recognizedTable.Regions.Count == 0)
        {
            return recognizedTable;
        }

        var detectionTable = BuildHeaderDetectionTableData(fullTableData);
        var validatedRegions = new List<SmartConfigurationRecognizedRegion>(recognizedTable.Regions.Count);
        foreach (var region in recognizedTable.Regions.OrderBy(item => item.RegionIndex))
        {
            var validated = region;
            var dataEnd = region.DataEndRowIndex ?? (detectionTable.Rows.Count - 1);
            var coordinatesValid = region.HeaderRowIndex >= 0 &&
                                   region.HeaderRowCount > 0 &&
                                   region.HeaderRowIndex + region.HeaderRowCount <= detectionTable.Rows.Count &&
                                   region.DataStartRowIndex >= region.HeaderRowIndex + region.HeaderRowCount &&
                                   region.DataStartRowIndex < detectionTable.Rows.Count &&
                                   dataEnd >= region.DataStartRowIndex &&
                                   dataEnd < detectionTable.Rows.Count;
            if (!coordinatesValid)
            {
                validatedRegions.Add(AddRegionIssue(
                    validated,
                    "TemplateRegionOutOfRange",
                    "已学习区域超出当前表格范围，请重新确认"));
                continue;
            }

            var actualHeaders = BuildRegionHeaders(
                detectionTable,
                region.HeaderRowIndex,
                region.HeaderRowCount);
            if (!MappedHeadersMatch(region, actualHeaders))
            {
                validated = AddRegionIssue(
                    validated,
                    "TemplateHeaderChanged",
                    "当前文件表头与已学习区域不一致，请重新确认列映射");
            }

            var regionMapping = recognizedTable with
            {
                Headers = actualHeaders,
                ProjectColumnIndex = region.IsSpecificationOnly ? null : region.ProjectColumnIndex,
                SpecificationColumnIndex = region.SpecificationColumnIndex,
                AcceptanceColumnIndex = region.AcceptanceColumnIndex,
                RemarkColumnIndex = region.RemarkColumnIndex,
                IsSpecificationOnly = region.IsSpecificationOnly
            };
            if (!HasHealthyRegionData(
                    detectionTable,
                    regionMapping,
                    region.DataStartRowIndex,
                    dataEnd,
                    headerKeywordMatcher))
            {
                validated = AddRegionIssue(
                    validated,
                    "TemplateRegionDataChanged",
                    "历史模板范围内的有效数据不足，行列位置可能已经变化");
            }

            validatedRegions.Add(NormalizeRegionToLeafHeader(
                detectionTable,
                validated));
        }

        for (var index = 1; index < validatedRegions.Count; index++)
        {
            var previous = validatedRegions[index - 1];
            if (previous.DataEndRowIndex.HasValue &&
                validatedRegions[index].HeaderRowIndex <= previous.DataEndRowIndex.Value)
            {
                validatedRegions[index] = AddRegionIssue(
                    validatedRegions[index],
                    "TemplateRegionOverlap",
                    "已学习区域在当前文件中发生重叠，请重新确认范围");
            }
        }

        // 模板的闭区间是后续导入/填充的执行真相，不能只检查整张表的数据密度。
        // 同时扫描当前已用区域，防止旧模板在数据下移或新增业务块后继续自动采用并静默漏行。
        AuditRegionCoverage(
            detectionTable,
            recognizedTable with
            {
                DataStartRowIndex = validatedRegions.Min(region => region.DataStartRowIndex),
                DataEndRowIndex = detectionTable.Rows.Count - 1
            },
            validatedRegions,
            headerKeywordMatcher);
        validatedRegions = DowngradeConfirmedCoverageIssues(
            validatedRegions,
            recognizedTable.Decision);

        // 旧模板只保存了第一块时，后续新区域可能连列位置也发生变化。此时用旧列
        // 审计业务行会看不到数据，因此额外寻找未覆盖的完整表头和其后的健康数据。
        // 暂不擅自改变已确认模板范围，但必须降级确认，禁止静默漏掉整块数据。
        var shiftedRegionHeader = FindUncoveredShiftedRegionHeader(
            detectionTable,
            recognizedTable,
            validatedRegions,
            headerKeywordMatcher);
        if (shiftedRegionHeader.HasValue)
        {
            validatedRegions[0] = AddRegionIssue(
                validatedRegions[0],
                "UncoveredRegionHeader",
                $"第 {shiftedRegionHeader.Value + 1} 行可能是新的表头，当前范围尚未包含该区域");
        }

        // 早期版本可能把整张工作表保存成一个过宽的单区域。即使后续重复表头
        // 落在这个旧闭区间内，也要按当前文件重新探测并展示离散区域；否则旧模板
        // 会永久遮蔽已经具备识别能力的新结构。
        var rediscovered = DetectLogicalRegionsFromCurrentStructure(
            fullTableData,
            recognizedTable with { Regions = [] },
            headerKeywordMatcher);
        if (rediscovered.Regions.Count > validatedRegions.Count)
        {
            var rediscoveredRegions = rediscovered.Regions
                .OrderBy(region => region.RegionIndex)
                .ToList();
            var shiftedHeaderNowCovered = shiftedRegionHeader.HasValue &&
                rediscoveredRegions.Any(region =>
                    shiftedRegionHeader.Value == region.DataStartRowIndex - 1 ||
                    (shiftedRegionHeader.Value >= region.HeaderRowIndex &&
                     shiftedRegionHeader.Value < region.HeaderRowIndex + region.HeaderRowCount));
            var existingIssueCodes = rediscoveredRegions[0].Issues
                .Select(issue => issue.Code)
                .ToHashSet(StringComparer.Ordinal);
            var preservedTemplateIssues = validatedRegions
                .SelectMany(region => region.Issues)
                // 当前结构识别已经把该表头及其数据纳入新区域时，旧模板审计产生的
                // “未覆盖表头”已失效，继续展示会与页面上的区域范围相互矛盾。
                .Where(issue => issue.Code != "UncoveredRegionHeader" || !shiftedHeaderNowCovered)
                .Where(issue => existingIssueCodes.Add(issue.Code))
                .ToList();
            if (preservedTemplateIssues.Count > 0)
            {
                rediscoveredRegions[0] = rediscoveredRegions[0] with
                {
                    Issues = [.. rediscoveredRegions[0].Issues, .. preservedTemplateIssues]
                };
            }
            rediscoveredRegions[0] = AddRegionIssue(
                rediscoveredRegions[0],
                "TemplateRegionStructureChanged",
                $"当前文件识别到 {rediscoveredRegions.Count} 个数据区域，旧模板仅保存 {validatedRegions.Count} 个，请确认后更新模板");
            return rediscovered with
            {
                Source = recognizedTable.Source,
                Decision = "NeedConfirm",
                Regions = rediscoveredRegions
            };
        }

        return recognizedTable with
        {
            Regions = validatedRegions,
            Decision = validatedRegions.Any(HasErrorIssue) ? "NeedConfirm" : recognizedTable.Decision
        };
    }

    private static List<SmartConfigurationRecognizedRegion> DowngradeConfirmedCoverageIssues(
        IReadOnlyList<SmartConfigurationRecognizedRegion> regions,
        string confirmedDecision)
    {
        return regions.Select(region =>
        {
            var issues = region.Issues.Select(issue =>
                string.Equals(issue.Code, "UncoveredBusinessRows", StringComparison.Ordinal)
                    ? new SmartConfigurationRecognitionIssue
                    {
                        Code = issue.Code,
                        Severity = "Warning",
                        Field = issue.Field,
                        Message = issue.Message
                    }
                    : issue).ToList();
            return region with
            {
                Issues = issues,
                Decision = issues.Any(issue => string.Equals(
                    issue.Severity,
                    "Error",
                    StringComparison.OrdinalIgnoreCase))
                    ? "NeedConfirm"
                    : confirmedDecision
            };
        }).ToList();
    }

    private static bool MappedHeadersMatch(
        SmartConfigurationRecognizedRegion region,
        IReadOnlyList<string> actualHeaders)
    {
        var mappedColumns = new int?[]
        {
            region.IsSpecificationOnly ? null : region.ProjectColumnIndex,
            region.SpecificationColumnIndex,
            region.AcceptanceColumnIndex,
            region.RemarkColumnIndex
        };
        return mappedColumns
            .Where(column => column.HasValue)
            .Select(column => column!.Value)
            .All(column => column >= 0 &&
                           column < region.Headers.Count &&
                           column < actualHeaders.Count &&
                           string.Equals(
                               NormalizeConfirmedHeader(region.Headers[column]),
                               NormalizeConfirmedHeader(actualHeaders[column]),
                               StringComparison.OrdinalIgnoreCase));
    }

    private static SmartConfigurationRecognizedRegion NormalizeRegionToLeafHeader(
        TableData detectionTable,
        SmartConfigurationRecognizedRegion region)
    {
        if (region.DataStartRowIndex <= 0)
        {
            return region;
        }

        var leafHeaderRowIndex = region.DataStartRowIndex - 1;
        if (leafHeaderRowIndex >= detectionTable.Rows.Count)
        {
            return region;
        }

        var leafHeaders = BuildRegionHeaders(detectionTable, leafHeaderRowIndex, 1);
        var fields = region.Fields.Select(field => new SmartConfigurationRecognizedField
        {
            Field = field.Field,
            ColumnIndex = field.ColumnIndex,
            Header = field.ColumnIndex.HasValue &&
                     field.ColumnIndex.Value >= 0 &&
                     field.ColumnIndex.Value < leafHeaders.Count
                ? leafHeaders[field.ColumnIndex.Value]
                : null,
            Confidence = field.Confidence,
            Source = field.Source
        }).ToList();

        return region with
        {
            HeaderRowIndex = leafHeaderRowIndex,
            HeaderRowCount = 1,
            Headers = leafHeaders.ToList(),
            Fields = fields
        };
    }

    private static SmartConfigurationRecognizedRegion AddRegionIssue(
        SmartConfigurationRecognizedRegion region,
        string code,
        string message)
    {
        if (region.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)))
        {
            return region with { Decision = "NeedConfirm" };
        }

        return region with
        {
            Decision = "NeedConfirm",
            Issues =
            [
                .. region.Issues,
                new SmartConfigurationRecognitionIssue
                {
                    Code = code,
                    Severity = "Error",
                    Message = message
                }
            ]
        };
    }

    private static bool HasErrorIssue(SmartConfigurationRecognizedRegion region) =>
        region.Issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));

    private static (SmartConfigurationRecognizedTable Table, DocumentTemplate Template)
        SelectBestDegradedTemplateCandidate(
            IReadOnlyList<(SmartConfigurationRecognizedTable Table, DocumentTemplate Template)> candidates)
    {
        return candidates
            .OrderBy(candidate => Math.Abs(
                GetPersistedTemplateRegionCount(candidate.Template) - candidate.Table.Regions.Count))
            .ThenBy(candidate => candidate.Table.Regions.Sum(region =>
                region.Issues.Count(issue =>
                    string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase))))
            .ThenByDescending(candidate => candidate.Template.UpdatedAt)
            .ThenByDescending(candidate => candidate.Template.UsageCount)
            .ThenByDescending(candidate => candidate.Template.Id)
            .First();
    }

    private static int GetPersistedTemplateRegionCount(DocumentTemplate template) =>
        template.Regions.Count > 0 ? template.Regions.Count : 1;

    private static bool HasExactSingleRegionTemplateHeaders(
        DocumentTemplate template,
        IReadOnlyList<string> currentHeaders)
    {
        if (GetPersistedTemplateRegionCount(template) != 1)
        {
            return false;
        }

        try
        {
            var templateHeaders = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                template.HeadersJson);
            return templateHeaders is not null &&
                   templateHeaders.Count == currentHeaders.Count &&
                   templateHeaders.Select(NormalizeConfirmedHeader).SequenceEqual(
                       currentHeaders.Select(NormalizeConfirmedHeader),
                       StringComparer.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static SmartConfigurationRecognizedTable AlignSingleRegionTemplateToCurrentHeader(
        SmartConfigurationRecognizedTable recognized,
        DocumentTemplate template,
        HeaderProfile headerProfile,
        TableInfo? tableInfo,
        TableData tableData)
    {
        var persistedRegion = template.Regions.SingleOrDefault();
        var persistedDataStart = persistedRegion?.DataStartRowIndex ?? template.DataStartRowIndex;
        var persistedDataEnd = persistedRegion?.DataEndRowIndex ?? template.DataEndRowIndex;
        var dataStart = headerProfile.HeaderRowIndex + headerProfile.HeaderRowCount;
        var totalRowCount = GetTotalRowCount(tableInfo, tableData);
        int? dataEnd = null;
        if (totalRowCount > dataStart)
        {
            var lastRowIndex = totalRowCount - 1;
            dataEnd = persistedDataEnd.HasValue
                ? Math.Clamp(
                    persistedDataEnd.Value + dataStart - persistedDataStart,
                    dataStart,
                    lastRowIndex)
                : lastRowIndex;
        }

        var region = recognized.Regions.Single() with
        {
            HeaderRowIndex = headerProfile.HeaderRowIndex,
            HeaderRowCount = headerProfile.HeaderRowCount,
            DataStartRowIndex = dataStart,
            DataEndRowIndex = dataEnd
        };
        return recognized with
        {
            HeaderRowIndex = region.HeaderRowIndex,
            HeaderRowCount = region.HeaderRowCount,
            DataStartRowIndex = region.DataStartRowIndex,
            DataEndRowIndex = region.DataEndRowIndex,
            Regions = [region]
        };
    }

    private static void AuditRegionCoverage(
        TableData detectionTable,
        SmartConfigurationRecognizedTable recognizedTable,
        List<SmartConfigurationRecognizedRegion> regions,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        if (!recognizedTable.DataEndRowIndex.HasValue || regions.Count == 0)
        {
            return;
        }

        var uncoveredRows = new List<(int RowIndex, string Specification, bool HasProjectAndSpecification)>();
        var scanEnd = Math.Min(recognizedTable.DataEndRowIndex.Value, detectionTable.Rows.Count - 1);
        for (var rowIndex = Math.Max(0, recognizedTable.DataStartRowIndex); rowIndex <= scanEnd; rowIndex++)
        {
            var covered = regions.Any(region =>
                (rowIndex >= region.HeaderRowIndex &&
                 rowIndex < region.HeaderRowIndex + region.HeaderRowCount) ||
                // 数据起始行的上一行是用户确认范围时采用的末级表头位置；即使
                // 复合表头探测只记录了前面的分组行，也不能再把它报告为未覆盖。
                rowIndex == region.DataStartRowIndex - 1 ||
                (region.DataEndRowIndex.HasValue &&
                 rowIndex >= region.DataStartRowIndex &&
                 rowIndex <= region.DataEndRowIndex.Value));
            if (covered)
            {
                continue;
            }

            var nearestRegion = regions
                .Where(region => region.HeaderRowIndex <= rowIndex)
                .OrderByDescending(region => region.HeaderRowIndex)
                .FirstOrDefault() ?? regions[0];
            var mapping = recognizedTable with
            {
                Headers = nearestRegion.Headers,
                ProjectColumnIndex = nearestRegion.IsSpecificationOnly ? null : nearestRegion.ProjectColumnIndex,
                SpecificationColumnIndex = nearestRegion.SpecificationColumnIndex,
                AcceptanceColumnIndex = nearestRegion.AcceptanceColumnIndex,
                RemarkColumnIndex = nearestRegion.RemarkColumnIndex,
                IsSpecificationOnly = nearestRegion.IsSpecificationOnly
            };
            if (!LooksLikeRegionDataRow(detectionTable.Rows[rowIndex], mapping, headerKeywordMatcher) ||
                !nearestRegion.SpecificationColumnIndex.HasValue)
            {
                continue;
            }

            var specification = detectionTable.Rows[rowIndex]
                .GetValue(nearestRegion.SpecificationColumnIndex.Value)?.Trim() ?? string.Empty;
            var project = nearestRegion.ProjectColumnIndex.HasValue
                ? detectionTable.Rows[rowIndex].GetValue(nearestRegion.ProjectColumnIndex.Value)?.Trim() ?? string.Empty
                : string.Empty;
            uncoveredRows.Add((
                rowIndex,
                specification,
                !nearestRegion.IsSpecificationOnly && project.Length > 0 && specification.Length > 0));
        }

        var hasSingleRowStrongEvidence = uncoveredRows.Any(item => item.HasProjectAndSpecification);
        var hasRepeatedSpecificationEvidence = uncoveredRows.Count >= 2 &&
            uncoveredRows.Select(item => item.Specification)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .Count() >= 2;
        if (!hasSingleRowStrongEvidence && !hasRepeatedSpecificationEvidence)
        {
            return;
        }

        var rowRanges = FormatOneBasedRowRanges(uncoveredRows.Select(item => item.RowIndex));
        regions[0] = AddRegionIssue(
            regions[0],
            "UncoveredBusinessRows",
            $"发现 {uncoveredRows.Count} 行疑似业务数据未包含在当前范围内（{rowRanges}），请调整范围");
    }

    private static string FormatOneBasedRowRanges(IEnumerable<int> zeroBasedRows)
    {
        var rows = zeroBasedRows.Distinct().OrderBy(row => row).Select(row => row + 1).ToList();
        if (rows.Count == 0)
            return string.Empty;

        var ranges = new List<string>();
        var start = rows[0];
        var end = start;
        foreach (var row in rows.Skip(1))
        {
            if (row == end + 1)
            {
                end = row;
                continue;
            }

            ranges.Add(start == end ? $"第 {start} 行" : $"第 {start}-{end} 行");
            start = row;
            end = row;
        }

        ranges.Add(start == end ? $"第 {start} 行" : $"第 {start}-{end} 行");
        const int maxDisplayedRanges = 4;
        if (ranges.Count <= maxDisplayedRanges)
            return string.Join("、", ranges);

        return $"{string.Join("、", ranges.Take(maxDisplayedRanges))}等 {ranges.Count} 个区间";
    }

    private static int? FindUncoveredShiftedRegionHeader(
        TableData detectionTable,
        SmartConfigurationRecognizedTable recognizedTable,
        IReadOnlyList<SmartConfigurationRecognizedRegion> regions,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        for (var rowIndex = 0; rowIndex < detectionTable.Rows.Count; rowIndex++)
        {
            var covered = regions.Any(region =>
                (rowIndex >= region.HeaderRowIndex &&
                 rowIndex < region.HeaderRowIndex + region.HeaderRowCount) ||
                rowIndex == region.DataStartRowIndex - 1 ||
                (region.DataEndRowIndex.HasValue &&
                 rowIndex >= region.DataStartRowIndex &&
                 rowIndex <= region.DataEndRowIndex.Value));
            if (covered || !headerKeywordMatcher.IsCompleteHeader(detectionTable.Rows[rowIndex]))
            {
                continue;
            }

            var headers = BuildRegionHeaders(detectionTable, rowIndex, 1);
            var shiftedMapping = ApplyRegionHeaderMappings(
                recognizedTable,
                headers,
                headerKeywordMatcher);
            var dataStart = FindNextRegionDataStart(
                detectionTable,
                rowIndex + 1,
                shiftedMapping,
                headerKeywordMatcher);
            if (!dataStart.HasValue || dataStart.Value - rowIndex > 5)
            {
                continue;
            }

            var leafHeaderRowIndex = dataStart.Value - 1;
            var leafHeaderCovered = regions.Any(region =>
                leafHeaderRowIndex == region.DataStartRowIndex - 1 ||
                (leafHeaderRowIndex >= region.HeaderRowIndex &&
                 leafHeaderRowIndex < region.HeaderRowIndex + region.HeaderRowCount));
            if (leafHeaderCovered)
            {
                continue;
            }

            var dataEnd = FindRegionDataEnd(
                detectionTable,
                dataStart.Value,
                shiftedMapping,
                headerKeywordMatcher);
            if (HasHealthyRegionData(
                    detectionTable,
                    shiftedMapping,
                    dataStart.Value,
                    dataEnd,
                    headerKeywordMatcher))
            {
                // 复合表头可能从分组标题行开始，但用户调整范围时需要定位的是
                // 首条数据正上方的末级表头行，而不是整个表头块的起始行。
                return leafHeaderRowIndex;
            }
        }

        return null;
    }

    private static SmartConfigurationRecognizedTable ApplyRegionHeaderMappings(
        SmartConfigurationRecognizedTable table,
        IReadOnlyList<string> headers,
        HeaderKeywordMatcher matcher)
    {
        int? Resolve(ColumnType type, int? fallback)
        {
            var matches = headers
                .Select((header, index) => (header, index))
                .Where(item => matcher.Matches(type, item.header))
                .Select(item => item.index)
                .ToList();
            // 当前区域的明确字段词命中优先于旧区域的列位置。否则当列发生移动、
            // 旧位置恰好存在未知非空表头时，会错误沿用旧列并丢弃整个后续区域。
            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                return fallback.HasValue && matches.Contains(fallback.Value)
                    ? fallback
                    : matches[0];
            }

            if (fallback.HasValue && fallback.Value >= 0 && fallback.Value < headers.Count)
            {
                var fallbackHeader = headers[fallback.Value];
                if (matcher.Matches(type, fallbackHeader) ||
                    (!string.IsNullOrWhiteSpace(fallbackHeader) &&
                     !IsMappedToDifferentField(type, fallbackHeader, matcher)))
                {
                    return fallback;
                }
            }

            return null;
        }

        var specification = Resolve(ColumnType.Specification, table.SpecificationColumnIndex);
        var project = table.IsSpecificationOnly ? null : Resolve(ColumnType.Project, table.ProjectColumnIndex);
        if (project.HasValue &&
            specification.HasValue &&
            table.ProjectColumnIndex.HasValue &&
            project.Value < table.ProjectColumnIndex.Value &&
            table.ProjectColumnIndex.Value < specification.Value &&
            table.ProjectColumnIndex.Value < headers.Count &&
            LooksLikeProjectDetailHeader(headers[table.ProjectColumnIndex.Value]))
        {
            // “项目 / 细项 / 规格”是常见的层级表头：左侧“项目”是分类列，
            // 原项目位置的中间列才是逐行具体项目。仅在明确形成 左<旧位<规格
            // 的夹层结构时保留旧位；“细项”即使也是规格别名，也不能覆盖右侧明确
            // 的“规格”列。真正整体移列（如 C/D -> E/F）仍优先新命中。
            project = table.ProjectColumnIndex;
        }
        var acceptance = Resolve(ColumnType.Acceptance, table.AcceptanceColumnIndex);
        var remark = Resolve(ColumnType.Remark, table.RemarkColumnIndex);
        return table with
        {
            Headers = headers.ToList(),
            ProjectColumnIndex = project,
            SpecificationColumnIndex = specification,
            AcceptanceColumnIndex = acceptance,
            RemarkColumnIndex = remark
        };
    }

    private static bool LooksLikeProjectDetailHeader(string? header)
    {
        var normalized = string.Concat((header ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character)))
            .ToLowerInvariant();
        return normalized is
            "细项" or "細項" or
            "具体项" or "具體項" or
            "具体项目" or "具體項目" or
            "项目明细" or "項目明細" or
            "明细项目" or "明細項目";
    }

    private static bool HasHealthyRegionData(
        TableData tableData,
        SmartConfigurationRecognizedTable mapping,
        int dataStartRowIndex,
        int dataEndRowIndex,
        HeaderKeywordMatcher matcher)
    {
        if (!mapping.SpecificationColumnIndex.HasValue || dataEndRowIndex < dataStartRowIndex)
        {
            return false;
        }

        var rows = Enumerable.Range(dataStartRowIndex, dataEndRowIndex - dataStartRowIndex + 1)
            .Where(rowIndex => rowIndex >= 0 && rowIndex < tableData.Rows.Count)
            .Select(rowIndex => tableData.Rows[rowIndex])
            .ToList();
        var validRows = rows
            .Where(row => LooksLikeRegionDataRow(row, mapping, matcher))
            .ToList();
        if (validRows.Count == 0 || validRows.Count * 2 < rows.Count)
        {
            return false;
        }

        var distinctSpecifications = validRows
            .Select(row => row.GetValue(mapping.SpecificationColumnIndex.Value) ?? string.Empty)
            .Select(value => string.Concat(value.Where(character => !char.IsWhiteSpace(character))))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count();
        return distinctSpecifications >= Math.Min(2, validRows.Count);
    }

    private static bool IsMappedToDifferentField(
        ColumnType expectedType,
        string header,
        HeaderKeywordMatcher matcher)
    {
        return new[]
            {
                ColumnType.Project,
                ColumnType.Specification,
                ColumnType.Acceptance,
                ColumnType.Remark
            }
            .Any(type => type != expectedType && matcher.Matches(type, header));
    }

    private static bool LooksLikeMappedHeaderRow(
        RowData row,
        SmartConfigurationRecognizedTable table,
        HeaderKeywordMatcher matcher)
    {
        var matchedValues = new List<string>();
        void AddMatchedValue(int? columnIndex, ColumnType columnType)
        {
            if (!columnIndex.HasValue ||
                !matcher.Matches(columnType, row.GetValue(columnIndex.Value)))
            {
                return;
            }

            var normalizedValue = string.Concat(
                (row.GetValue(columnIndex.Value) ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character)));
            if (normalizedValue.Length > 0)
            {
                matchedValues.Add(normalizedValue);
            }
        }

        var specificationMatched = MatchesMappedHeader(
            row,
            table.SpecificationColumnIndex,
            ColumnType.Specification,
            matcher);
        AddMatchedValue(table.SpecificationColumnIndex, ColumnType.Specification);
        AddMatchedValue(table.ProjectColumnIndex, ColumnType.Project);
        AddMatchedValue(table.AcceptanceColumnIndex, ColumnType.Acceptance);
        AddMatchedValue(table.RemarkColumnIndex, ColumnType.Remark);
        return specificationMatched &&
               matchedValues.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() >= 2;
    }

    private static bool LooksLikeCompositeHeaderStart(
        TableData tableData,
        int startRowIndex,
        int maxHeaderRowCount,
        HeaderKeywordMatcher matcher)
    {
        if (startRowIndex < 0 || startRowIndex >= tableData.Rows.Count ||
            !matcher.HasProjectAndSpecificationEvidence(tableData.Rows[startRowIndex]))
        {
            return false;
        }

        var availableRowCount = Math.Min(
            maxHeaderRowCount,
            tableData.Rows.Count - startRowIndex);
        for (var rowCount = 2; rowCount <= availableRowCount; rowCount++)
        {
            if (matcher.IsCompleteHeader(
                    tableData.Rows.Skip(startRowIndex).Take(rowCount)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesMappedHeader(
        RowData row,
        int? columnIndex,
        ColumnType columnType,
        HeaderKeywordMatcher matcher)
    {
        return columnIndex.HasValue && matcher.Matches(columnType, row.GetValue(columnIndex.Value));
    }

    private static int? FindNextRegionDataStart(
        TableData tableData,
        int startRowIndex,
        SmartConfigurationRecognizedTable mapping,
        HeaderKeywordMatcher matcher)
    {
        var end = Math.Min(tableData.Rows.Count, startRowIndex + 12);
        for (var rowIndex = Math.Max(0, startRowIndex); rowIndex < end; rowIndex++)
        {
            if (LooksLikeRegionDataRow(tableData.Rows[rowIndex], mapping, matcher))
            {
                return rowIndex;
            }

            // Excel 横向合并会把左上角值展开到项目列和规格列。第二数据段的
            // 首条分类数据因此可能出现“项目 == 规格”，但它仍然属于业务范围。
            // 只有紧随其后的行已经是健康数据时才保留该锚点，避免把孤立标题
            // 或真正的重复表头误纳入数据区。
            if (rowIndex + 1 < end &&
                LooksLikeMergedRegionOpeningDataRow(
                    tableData.Rows[rowIndex],
                    mapping,
                    matcher) &&
                LooksLikeRegionDataRow(
                    tableData.Rows[rowIndex + 1],
                    mapping,
                    matcher))
            {
                return rowIndex;
            }
        }

        return null;
    }

    private static bool LooksLikeMergedRegionOpeningDataRow(
        RowData row,
        SmartConfigurationRecognizedTable mapping,
        HeaderKeywordMatcher matcher)
    {
        if (mapping.IsSpecificationOnly ||
            !mapping.ProjectColumnIndex.HasValue ||
            !mapping.SpecificationColumnIndex.HasValue ||
            mapping.ProjectColumnIndex.Value == mapping.SpecificationColumnIndex.Value)
        {
            return false;
        }

        var project = row.GetValue(mapping.ProjectColumnIndex.Value)?.Trim() ?? string.Empty;
        var specification = row.GetValue(mapping.SpecificationColumnIndex.Value)?.Trim() ?? string.Empty;
        var hasAcceptanceHeaderEvidence = MatchesMappedHeader(
            row,
            mapping.AcceptanceColumnIndex,
            ColumnType.Acceptance,
            matcher);
        var hasRemarkHeaderEvidence = MatchesMappedHeader(
            row,
            mapping.RemarkColumnIndex,
            ColumnType.Remark,
            matcher);
        if (project.Length == 0 ||
            !string.Equals(project, specification, StringComparison.OrdinalIgnoreCase) ||
            LooksLikeSectionHeaderRow(row, matcher) ||
            hasAcceptanceHeaderEvidence ||
            hasRemarkHeaderEvidence)
        {
            return false;
        }

        return row.Cells.Count(cell => string.Equals(
            cell.Value?.Trim(),
            specification,
            StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    private static int FindRegionDataEnd(
        TableData tableData,
        int dataStartRowIndex,
        SmartConfigurationRecognizedTable mapping,
        HeaderKeywordMatcher matcher)
    {
        var lastValid = Math.Clamp(dataStartRowIndex, 0, Math.Max(0, tableData.Rows.Count - 1));
        var invalidStreak = 0;
        for (var rowIndex = lastValid; rowIndex < tableData.Rows.Count; rowIndex++)
        {
            if (LooksLikeRegionDataRow(tableData.Rows[rowIndex], mapping, matcher))
            {
                lastValid = rowIndex;
                invalidStreak = 0;
                continue;
            }

            invalidStreak++;
            if (invalidStreak >= 2 || LooksLikeMappedHeaderRow(tableData.Rows[rowIndex], mapping, matcher))
            {
                break;
            }
        }

        return lastValid;
    }

    private static bool LooksLikeRegionDataRow(
        RowData row,
        SmartConfigurationRecognizedTable mapping,
        HeaderKeywordMatcher matcher)
    {
        if (!mapping.SpecificationColumnIndex.HasValue)
        {
            return false;
        }

        var specification = row.GetValue(mapping.SpecificationColumnIndex.Value)?.Trim() ?? string.Empty;
        if (specification.Length == 0 ||
            matcher.IsCompleteHeader(row) ||
            LooksLikeSectionHeaderRow(row, matcher) ||
            LooksLikeMappedHeaderRow(row, mapping, matcher))
        {
            return false;
        }

        if (mapping.IsSpecificationOnly)
        {
            return true;
        }

        if (!mapping.ProjectColumnIndex.HasValue)
        {
            return false;
        }

        var project = row.GetValue(mapping.ProjectColumnIndex.Value)?.Trim() ?? string.Empty;
        return project.Length > 0 && !string.Equals(project, specification, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSectionHeaderRow(RowData row, HeaderKeywordMatcher matcher)
    {
        var firstCell = row.GetValue(0)?.Trim() ?? string.Empty;
        if (firstCell.Length == 0 || (!firstCell.Contains('：') && !firstCell.Contains(':')))
        {
            return false;
        }

        var values = row.Cells
            .Select(cell => cell.Value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToList();
        var matchedFieldCount = new[]
            {
                ColumnType.Project,
                ColumnType.Specification,
                ColumnType.Acceptance,
                ColumnType.Remark
            }
            .Count(type => values.Any(value => matcher.Matches(type, value)));
        return values.Count >= 3 && matchedFieldCount >= 3;
    }

    private static List<string> BuildRegionHeaders(TableData tableData, int headerRowIndex, int headerRowCount)
    {
        var headers = new List<string>(tableData.ColumnCount);
        for (var columnIndex = 0; columnIndex < tableData.ColumnCount; columnIndex++)
        {
            var parts = Enumerable.Range(headerRowIndex, headerRowCount)
                .Where(rowIndex => rowIndex >= 0 && rowIndex < tableData.Rows.Count)
                .Select(rowIndex => tableData.Rows[rowIndex].GetValue(columnIndex)?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            headers.Add(string.Join(" / ", parts));
        }

        return headers;
    }

    private static SmartConfigurationRecognizedRegion CreateRegion(
        SmartConfigurationRecognizedTable table,
        int regionIndex,
        IReadOnlyList<string> headers,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int dataEndRowIndex)
    {
        var fields = new[]
        {
            (Field: "Project", Column: table.ProjectColumnIndex),
            (Field: "Specification", Column: table.SpecificationColumnIndex),
            (Field: "Acceptance", Column: table.AcceptanceColumnIndex),
            (Field: "Remark", Column: table.RemarkColumnIndex)
        }.Select(item =>
        {
            var originalField = table.Fields.FirstOrDefault(field =>
                string.Equals(field.Field, item.Field, StringComparison.OrdinalIgnoreCase));
            return new SmartConfigurationRecognizedField
            {
                Field = item.Field,
                ColumnIndex = item.Column,
                Header = item.Column.HasValue && item.Column.Value >= 0 && item.Column.Value < headers.Count
                    ? headers[item.Column.Value]
                    : null,
                Confidence = item.Column.HasValue
                    ? originalField?.Confidence ?? table.Confidence
                    : 0,
                Source = regionIndex == 0
                    ? originalField?.Source ?? table.Source
                    : "RepeatedHeader"
            };
        }).ToList();

        var issues = new List<SmartConfigurationRecognitionIssue>();
        if (!table.IsSpecificationOnly && !table.ProjectColumnIndex.HasValue)
        {
            issues.Add(new SmartConfigurationRecognitionIssue
            {
                Code = "MissingProjectColumn",
                Severity = "Error",
                Field = "Project",
                Message = "该区域未识别到项目列"
            });
        }
        if (!table.SpecificationColumnIndex.HasValue)
        {
            issues.Add(new SmartConfigurationRecognitionIssue
            {
                Code = "MissingSpecificationColumn",
                Severity = "Error",
                Field = "Specification",
                Message = "该区域未识别到规格列"
            });
        }
        if (!table.AcceptanceColumnIndex.HasValue)
        {
            issues.Add(new SmartConfigurationRecognitionIssue
            {
                Code = "MissingAcceptanceColumn",
                Severity = "Error",
                Field = "Acceptance",
                Message = "该区域未识别到验收列"
            });
        }
        var mappedColumns = new int?[]
        {
            table.IsSpecificationOnly ? null : table.ProjectColumnIndex,
            table.SpecificationColumnIndex,
            table.AcceptanceColumnIndex,
            table.RemarkColumnIndex
        }.Where(column => column.HasValue).Select(column => column!.Value).ToList();
        if (mappedColumns.Distinct().Count() != mappedColumns.Count)
        {
            issues.Add(new SmartConfigurationRecognitionIssue
            {
                Code = "DuplicateMappedColumns",
                Severity = "Error",
                Message = "该区域存在重复字段列，请调整后确认"
            });
        }
        var duplicateMappedHeaders = fields
            .Where(field => field.ColumnIndex.HasValue && !string.IsNullOrWhiteSpace(field.Header))
            .GroupBy(field => field.Header!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
        if (duplicateMappedHeaders)
        {
            issues.Add(new SmartConfigurationRecognitionIssue
            {
                Code = "ConflictingMappedHeaders",
                Severity = "Warning",
                Message = "该区域多个字段落在同一合并表头，请调整后确认"
            });
        }

        return new SmartConfigurationRecognizedRegion
        {
            RegionId = SmartConfigurationRecognizedTableFactory.BuildRegionId(table.TableIndex, regionIndex),
            RegionIndex = regionIndex,
            Headers = headers.ToList(),
            HeaderRowIndex = headerRowIndex,
            HeaderRowCount = headerRowCount,
            DataStartRowIndex = dataStartRowIndex,
            DataEndRowIndex = dataEndRowIndex,
            ProjectColumnIndex = table.ProjectColumnIndex,
            SpecificationColumnIndex = table.SpecificationColumnIndex,
            AcceptanceColumnIndex = table.AcceptanceColumnIndex,
            RemarkColumnIndex = table.RemarkColumnIndex,
            IsSpecificationOnly = table.IsSpecificationOnly,
            Confidence = regionIndex == 0 ? table.Confidence : Math.Min(table.Confidence, 0.88),
            Source = regionIndex == 0 ? table.Source : "RepeatedHeader",
            Decision = issues.Count > 0 || regionIndex > 0 ? "NeedConfirm" : table.Decision,
            Issues = issues,
            Fields = fields
        };
    }
    private HeaderProfile DetectHeaderProfile(
        TableData tableData,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        var detectionTable = BuildHeaderDetectionTableData(tableData);
        var scanRowLimit = Math.Clamp(
            _options.HeaderDetectionScanRowLimit,
            1,
            Math.Max(1, detectionTable.Rows.Count));
        var maxHeaderRowCount = Math.Clamp(_options.MaxHeaderRowCount, 1, 20);
        var structuralLeafHeaderRowIndex = FindStructuralHeaderToDataTransition(
            detectionTable,
            scanRowLimit);
        if (structuralLeafHeaderRowIndex.HasValue)
        {
            return new HeaderProfile(structuralLeafHeaderRowIndex.Value, 1);
        }

        var anchorRowIndex = _intelligenceService.DetectHeaderRowIndex(detectionTable, scanRowLimit);
        var repeatedLeafHeaderRowIndex = FindCompleteRepeatedLeafHeaderRow(
            detectionTable,
            anchorRowIndex,
            maxHeaderRowCount,
            headerKeywordMatcher);
        if (repeatedLeafHeaderRowIndex.HasValue)
        {
            return new HeaderProfile(repeatedLeafHeaderRowIndex.Value, 1);
        }

        var headerRowIndex = ExpandHeaderStart(detectionTable, anchorRowIndex, maxHeaderRowCount, headerKeywordMatcher);
        var headerRowCount = DetectHeaderRowCount(detectionTable, headerRowIndex, maxHeaderRowCount, headerKeywordMatcher);
        return new HeaderProfile(headerRowIndex, headerRowCount);
    }

    private static int? FindStructuralHeaderToDataTransition(
        TableData tableData,
        int scanRowLimit)
    {
        if (tableData.Rows.Count < 2)
        {
            return null;
        }

        var columnCount = Math.Max(
            tableData.ColumnCount,
            tableData.Rows
                .SelectMany(row => row.Cells)
                .Select(cell => cell.ColumnIndex + 1)
                .DefaultIfEmpty(0)
                .Max());
        var lastCandidateRowIndex = Math.Min(
            tableData.Rows.Count - 3,
            Math.Max(0, scanRowLimit - 1));

        for (var rowIndex = 1; rowIndex <= lastCandidateRowIndex; rowIndex++)
        {
            var headerShape = AnalyzeStructuralRow(tableData.Rows[rowIndex], columnCount);
            if (!LooksLikeStructuralLeafHeader(headerShape, columnCount))
            {
                continue;
            }

            if (!HasStructuralHeaderBoundaryEvidence(tableData, rowIndex - 1, columnCount))
            {
                continue;
            }

            if (!HasStrongGroupedHeaderContext(
                    tableData.Rows[rowIndex - 1],
                    tableData.Rows[rowIndex],
                    headerShape))
            {
                continue;
            }

            var followingShapes = tableData.Rows
                .Skip(rowIndex + 1)
                .Take(2)
                .Select(row => AnalyzeStructuralRow(row, columnCount))
                .ToList();
            if (!LooksLikeStableStructuralDataBand(headerShape, followingShapes))
            {
                continue;
            }

            // 结构证据足够时直接采用最早候选；不满足强结构则继续走现有
            // 可配置字段规则和 AI 判定，避免用弱启发式猜测普通数据行。
            return rowIndex;
        }

        return null;
    }

    private static bool HasStructuralHeaderBoundaryEvidence(
        TableData tableData,
        int groupedHeaderRowIndex,
        int columnCount)
    {
        var hasHorizontalMerge = tableData.MergedCells.Any(merged =>
            merged.IsHorizontalMerge &&
            groupedHeaderRowIndex >= merged.StartRow &&
            groupedHeaderRowIndex <= merged.EndRow);
        if (hasHorizontalMerge)
        {
            return true;
        }

        if (groupedHeaderRowIndex <= 0)
        {
            return false;
        }

        var precedingShape = AnalyzeStructuralRow(
            tableData.Rows[groupedHeaderRowIndex - 1],
            columnCount);
        return precedingShape.NonEmptyCount == 0 || precedingShape.FillRate <= 0.15;
    }

    private static bool HasStrongGroupedHeaderContext(
        RowData parentRow,
        RowData leafRow,
        StructuralRowShape leafShape)
    {
        var parentGroups = FindAdjacentRepeatedValueRuns(parentRow);
        if (parentGroups.Count < 2)
        {
            return false;
        }

        var groupedColumns = parentGroups
            .SelectMany(group => Enumerable.Range(
                group.StartColumnIndex,
                group.EndColumnIndex - group.StartColumnIndex + 1))
            .ToHashSet();
        if (groupedColumns.Count < 4 ||
            groupedColumns.Intersect(leafShape.PopulatedColumns).Count() /
            (double)Math.Max(1, leafShape.NonEmptyCount) < 0.40)
        {
            return false;
        }

        var refinedGroupCount = parentGroups.Count(group =>
            leafRow.Cells
                .Where(cell =>
                    cell.ColumnIndex >= group.StartColumnIndex &&
                    cell.ColumnIndex <= group.EndColumnIndex)
                .Select(cell => cell.Value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .Count() >= 2);
        return refinedGroupCount >= 2;
    }

    private static IReadOnlyList<StructuralRepeatedValueRun> FindAdjacentRepeatedValueRuns(RowData row)
    {
        var orderedCells = row.Cells
            .OrderBy(cell => cell.ColumnIndex)
            .Select(cell => new
            {
                cell.ColumnIndex,
                Value = cell.Value?.Trim() ?? string.Empty
            })
            .ToList();
        var runs = new List<StructuralRepeatedValueRun>();
        var startIndex = 0;
        while (startIndex < orderedCells.Count)
        {
            var start = orderedCells[startIndex];
            if (start.Value.Length == 0)
            {
                startIndex++;
                continue;
            }

            var endIndex = startIndex;
            while (endIndex + 1 < orderedCells.Count &&
                   orderedCells[endIndex + 1].ColumnIndex ==
                   orderedCells[endIndex].ColumnIndex + 1 &&
                   string.Equals(
                       orderedCells[endIndex + 1].Value,
                       start.Value,
                       StringComparison.OrdinalIgnoreCase))
            {
                endIndex++;
            }

            if (endIndex > startIndex)
            {
                runs.Add(new StructuralRepeatedValueRun(
                    start.ColumnIndex,
                    orderedCells[endIndex].ColumnIndex));
            }

            startIndex = endIndex + 1;
        }

        return runs;
    }

    private static StructuralRowShape AnalyzeStructuralRow(RowData row, int columnCount)
    {
        var populatedCells = row.Cells
            .Select(cell => new
            {
                cell.ColumnIndex,
                Value = cell.Value?.Trim() ?? string.Empty
            })
            .Where(cell => cell.Value.Length > 0)
            .ToList();
        if (populatedCells.Count == 0)
        {
            return StructuralRowShape.Empty;
        }

        var values = populatedCells.Select(cell => cell.Value).ToList();
        var distinctValueCount = values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new StructuralRowShape(
            populatedCells.Count,
            populatedCells.Select(cell => cell.ColumnIndex).ToHashSet(),
            populatedCells.Count / (double)Math.Max(1, columnCount),
            values.Count(value => value.Length <= 24) / (double)values.Count,
            distinctValueCount / (double)values.Count,
            values.Average(value => value.Length),
            values.Max(value => value.Length),
            values.Count(LooksLikeStructuralNumericValue));
    }

    private static bool LooksLikeStructuralLeafHeader(
        StructuralRowShape shape,
        int columnCount)
    {
        var minimumPopulatedCells = Math.Min(3, Math.Max(1, columnCount));
        return shape.NonEmptyCount >= minimumPopulatedCells &&
               shape.FillRate >= 0.30 &&
               shape.ShortTextRatio >= 0.75 &&
               shape.DistinctValueRatio >= 0.55 &&
               shape.MaximumValueLength <= 32 &&
               shape.NumericLikeCount <= Math.Max(1, shape.NonEmptyCount / 5);
    }

    private static bool LooksLikeStableStructuralDataBand(
        StructuralRowShape headerShape,
        IReadOnlyList<StructuralRowShape> followingShapes)
    {
        if (followingShapes.Count < 2 ||
            followingShapes[0].NonEmptyCount < 2 ||
            followingShapes[1].NonEmptyCount < 2)
        {
            return false;
        }

        var dataLikeRows = followingShapes
            .Take(2)
            .Where(shape =>
                shape.NonEmptyCount >= 2 &&
                (shape.MaximumValueLength >= Math.Max(18, headerShape.MaximumValueLength + 4) ||
                 shape.AverageValueLength >= headerShape.AverageValueLength + 3) &&
                (shape.MaximumValueLength >= 18 ||
                 shape.NumericLikeCount > headerShape.NumericLikeCount))
            .ToList();
        if (dataLikeRows.Count != 2)
        {
            return false;
        }

        var first = dataLikeRows[0].PopulatedColumns;
        var second = dataLikeRows[1].PopulatedColumns;
        var intersection = first.Intersect(second).ToHashSet();
        var unionCount = first.Union(second).Count();
        if (unionCount == 0 ||
            intersection.Count / (double)unionCount < 0.60)
        {
            return false;
        }

        return intersection.Count > 0 &&
               intersection.Count(headerShape.PopulatedColumns.Contains) /
               (double)intersection.Count >= 0.67;
    }

    private static bool LooksLikeStructuralNumericValue(string value)
    {
        var hasDigit = false;
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                hasDigit = true;
                continue;
            }

            if (!char.IsWhiteSpace(character) &&
                character is not '-' and not '+' and not '.' and not ',' and
                not '/' and not '\\' and not '%' and not '(' and not ')')
            {
                return false;
            }
        }

        return hasDigit;
    }

    private static int? FindCompleteRepeatedLeafHeaderRow(
        TableData tableData,
        int anchorRowIndex,
        int maxHeaderRowCount,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        if (tableData.Rows.Count == 0)
        {
            return null;
        }

        // 锚点可能落在分组行或首条数据行；宽窗口只用于收集候选，后续仅允许相邻叶行晋级。
        var startRowIndex = Math.Max(0, anchorRowIndex - maxHeaderRowCount + 1);
        var endRowIndex = Math.Min(tableData.Rows.Count - 1, anchorRowIndex + maxHeaderRowCount - 1);

        var repeatedLeafRowWithGroupContext = Enumerable.Range(startRowIndex + 1, endRowIndex - startRowIndex)
            .Where(rowIndex =>
                rowIndex == anchorRowIndex + 2 &&
                !headerKeywordMatcher.IsCompleteRepeatedLeafHeader(tableData.Rows[anchorRowIndex]) &&
                headerKeywordMatcher.IsCompleteHeader(tableData.Rows[rowIndex]) &&
                CountRepeatedValueGroups(tableData.Rows[rowIndex - 2]) > 0 &&
                HaveSameNormalizedCellValues(tableData.Rows[rowIndex - 1], tableData.Rows[rowIndex]))
            .OrderByDescending(rowIndex => rowIndex)
            .Select(rowIndex => (int?)rowIndex)
            .FirstOrDefault();
        if (repeatedLeafRowWithGroupContext.HasValue)
        {
            return repeatedLeafRowWithGroupContext;
        }

        var candidates = Enumerable.Range(startRowIndex, endRowIndex - startRowIndex + 1)
            .Where(rowIndex => headerKeywordMatcher.IsCompleteRepeatedLeafHeader(tableData.Rows[rowIndex]))
            .ToList();
        var nearestAtOrBeforeAnchor = candidates
            .Where(rowIndex => rowIndex <= anchorRowIndex)
            .OrderByDescending(rowIndex => rowIndex)
            .Select(rowIndex => (int?)rowIndex)
            .FirstOrDefault();
        if (!nearestAtOrBeforeAnchor.HasValue)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            var firstCandidate = candidates.Min();
            if (firstCandidate != anchorRowIndex + 1)
            {
                return null;
            }

            var hasGroupedHeaderContext =
                CountRepeatedValueGroups(tableData.Rows[anchorRowIndex]) >= 2;
            return hasGroupedHeaderContext
                ? AdvanceAcrossIdenticalCandidates(tableData, candidates, firstCandidate)
                : null;
        }

        if (nearestAtOrBeforeAnchor.Value == anchorRowIndex &&
            anchorRowIndex > 0 &&
            CountRepeatedValueGroups(tableData.Rows[anchorRowIndex - 1]) < 2 &&
            LooksLikeLeadingHeaderGroupRow(
                tableData.Rows[anchorRowIndex - 1],
                headerKeywordMatcher))
        {
            return null;
        }

        var nextRowIndex = anchorRowIndex + 1;
        if (nearestAtOrBeforeAnchor.Value == anchorRowIndex && candidates.Contains(nextRowIndex))
        {
            var anchorGroupCount = CountRepeatedValueGroups(tableData.Rows[anchorRowIndex]);
            var nextGroupCount = CountRepeatedValueGroups(tableData.Rows[nextRowIndex]);
            if (HaveSameNormalizedCellValues(
                    tableData.Rows[anchorRowIndex],
                    tableData.Rows[nextRowIndex]) ||
                anchorGroupCount > nextGroupCount)
            {
                var resolvedRowIndex = AdvanceAcrossIdenticalCandidates(
                    tableData,
                    candidates,
                    nextRowIndex);
                var trailingLeafRowIndex = resolvedRowIndex + 1;
                var followingDataRowIndex = trailingLeafRowIndex + 1;
                if (candidates.Contains(trailingLeafRowIndex) &&
                    CountRepeatedValueGroups(tableData.Rows[resolvedRowIndex]) >
                    CountRepeatedValueGroups(tableData.Rows[trailingLeafRowIndex]) &&
                    followingDataRowIndex < tableData.Rows.Count &&
                    !headerKeywordMatcher.IsCompleteHeader(tableData.Rows[followingDataRowIndex]))
                {
                    return AdvanceAcrossIdenticalCandidates(
                        tableData,
                        candidates,
                        trailingLeafRowIndex);
                }

                return resolvedRowIndex;
            }
        }

        return AdvanceAcrossIdenticalCandidates(
            tableData,
            candidates,
            nearestAtOrBeforeAnchor.Value);
    }

    private static int AdvanceAcrossIdenticalCandidates(
        TableData tableData,
        IReadOnlyCollection<int> candidates,
        int rowIndex)
    {
        while (candidates.Contains(rowIndex + 1) &&
               HaveSameNormalizedCellValues(tableData.Rows[rowIndex], tableData.Rows[rowIndex + 1]))
        {
            rowIndex++;
        }

        return rowIndex;
    }

    private static int CountRepeatedValueGroups(RowData row)
    {
        return row.Cells
            .Select(cell => cell.Value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);
    }

    private static bool HaveSameNormalizedCellValues(RowData left, RowData right)
    {
        var leftValues = left.Cells
            .OrderBy(cell => cell.ColumnIndex)
            .Select(cell => (cell.Value ?? string.Empty).Trim())
            .ToList();
        var rightValues = right.Cells
            .OrderBy(cell => cell.ColumnIndex)
            .Select(cell => (cell.Value ?? string.Empty).Trim())
            .ToList();
        return leftValues.SequenceEqual(rightValues, StringComparer.OrdinalIgnoreCase);
    }

    private static TableData BuildHeaderDetectionTableData(TableData tableData)
    {
        var rows = new List<RowData>();
        if (tableData.Headers.Count > 0)
        {
            rows.Add(new RowData
            {
                Index = 0,
                Cells = tableData.Headers
                    .Select((value, columnIndex) => new CellData
                    {
                        RowIndex = 0,
                        ColumnIndex = columnIndex,
                        Value = value
                    })
                    .ToList()
            });
        }

        var offset = rows.Count;
        rows.AddRange(tableData.Rows.Select((row, index) =>
        {
            var rowIndex = offset + index;
            return new RowData
            {
                Index = rowIndex,
                Cells = row.Cells
                    .Select(cell => new CellData
                    {
                        RowIndex = rowIndex,
                        ColumnIndex = cell.ColumnIndex,
                        Value = cell.Value
                    })
                    .ToList()
            };
        }));

        return new TableData
        {
            TableIndex = tableData.TableIndex,
            Headers = tableData.Headers,
            Rows = rows,
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

    private static int ExpandHeaderStart(
        TableData tableData,
        int anchorRowIndex,
        int maxHeaderRowCount,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        var headerRowIndex = anchorRowIndex;
        while (headerRowIndex > 0 &&
               anchorRowIndex - headerRowIndex + 1 < maxHeaderRowCount &&
               LooksLikeLeadingHeaderGroupRow(tableData.Rows[headerRowIndex - 1], headerKeywordMatcher))
        {
            headerRowIndex--;
        }

        return headerRowIndex;
    }

    private static int DetectHeaderRowCount(
        TableData tableData,
        int headerRowIndex,
        int maxHeaderRowCount,
        HeaderKeywordMatcher headerKeywordMatcher)
    {
        if (headerRowIndex < 0 || headerRowIndex >= tableData.Rows.Count)
        {
            return 1;
        }

        var count = 1;
        var maxHeaderRows = Math.Min(tableData.Rows.Count, headerRowIndex + maxHeaderRowCount);
        for (var rowIndex = headerRowIndex + 1; rowIndex < maxHeaderRows; rowIndex++)
        {
            var repeatsPreviousHeaderRow = HaveSameNormalizedCellValues(
                tableData.Rows[rowIndex - 1],
                tableData.Rows[rowIndex]);
            var completeLeafAfterAtomicHeader =
                CountRepeatedValueGroups(tableData.Rows[rowIndex - 1]) == 0 &&
                headerKeywordMatcher.IsCompleteRepeatedLeafHeader(tableData.Rows[rowIndex]);
            if (completeLeafAfterAtomicHeader)
            {
                break;
            }

            if (!repeatsPreviousHeaderRow &&
                !LooksLikeAdditionalHeaderRow(tableData.Rows[rowIndex], headerKeywordMatcher))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool LooksLikeAdditionalHeaderRow(RowData row, HeaderKeywordMatcher headerKeywordMatcher)
    {
        var values = row.Cells
            .Select(cell => cell.Value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToList();
        if (values.Count == 0)
        {
            return false;
        }

        var keywordCount = values.Count(headerKeywordMatcher.Contains);
        if (keywordCount == 0 || headerKeywordMatcher.HasAmbiguousCustomerTypeEvidence(values))
        {
            return false;
        }

        var averageLength = values.Average(value => value.Length);
        if (averageLength > 20)
        {
            return false;
        }

        return keywordCount >= 2 || (keywordCount == 1 && values.Count <= 2);
    }

    private static bool LooksLikeLeadingHeaderGroupRow(RowData row, HeaderKeywordMatcher headerKeywordMatcher)
    {
        var values = row.Cells
            .Select(cell => cell.Value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToList();
        if (values.Count < 2)
        {
            return false;
        }

        var averageLength = values.Average(value => value.Length);
        if (averageLength > 12)
        {
            return false;
        }

        var keywordCount = values.Count(headerKeywordMatcher.Contains);
        if (keywordCount >= 2)
        {
            return true;
        }

        // 分组表头常见重复文本来自横向合并单元格展开，例如“验收信息/验收信息”。
        // 只有短文本不足以证明是表头，避免把客户、机种、版本等说明行并入表头。
        var duplicateCount = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Sum(group => group.Count());
        return duplicateCount >= 2 && keywordCount > 0;
    }

    /// <summary>
    /// 确认智能结构识别结果，并沉淀客户模板与客户域学习词。
    /// </summary>
    public async Task<SmartConfigurationConfirmResult> ConfirmAsync(
        SmartConfigurationConfirmCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CustomerId <= 0)
        {
            throw new ApplicationServiceException(400, "客户不能为空");
        }

        if (command.Regions.Count == 0)
        {
            if (command.Headers.Count == 0)
            {
                throw new ApplicationServiceException(400, "表头不能为空");
            }

            ValidateConfirmCoordinates(command);
            ValidateConfirmColumnIndexes(command, command.Headers.Count);
        }

        if (command.FileId is not > 0)
        {
            throw new ApplicationServiceException(400, "确认结构时必须提供有效FileId");
        }

        if (!await _fileAccessService.CanAccessCustomerAsync(command.CustomerId, cancellationToken))
        {
            throw new ApplicationServiceException(404, $"客户不存在或无权访问：{command.CustomerId}");
        }

        var submittedRegions = command.Regions.Count > 0
            ? command.Regions.OrderBy(region => region.RegionIndex).ToList()
            : [ToLegacyConfirmRegion(command)];
        ValidateSubmittedRegions(submittedRegions);
        var tableContext = await GetConfirmedTableContextAsync(command, cancellationToken);
        var isExcel = tableContext.FileType == UploadedFileType.ExcelXlsx;
        var confirmedRegions = new List<(SmartConfigurationConfirmRegion Region, IReadOnlyList<string> Headers)>();
        foreach (var region in submittedRegions)
        {
            var regionCommand = ToRegionCommand(command, region);
            ValidateConfirmCoordinates(regionCommand, enforceExcelLeafHeader: isExcel);
            ValidateDataEndRowIndex(regionCommand, tableContext.Table.RowCount);
            var regionHeaders = await ExtractConfirmedHeadersAsync(regionCommand, tableContext, cancellationToken);
            if (regionHeaders.Count > MaxConfirmedHeaderCount ||
                regionHeaders.Any(header => header.Length > ColumnHeaderRuleMatcher.MaxHeaderInputLength))
            {
                throw new ApplicationServiceException(400, "表头数量或文本长度超出安全限制");
            }

            ValidateConfirmColumnIndexes(
                regionCommand,
                regionHeaders.Count,
                requireExcelFieldColumns: isExcel);
            confirmedRegions.Add((region, regionHeaders));
        }

        var primaryRegion = submittedRegions[0];
        var effectiveHeaders = confirmedRegions[0].Headers;
        var effectiveLearnedColumns = command.Regions.Count > 0
            ? BuildRegionLearnedColumns(confirmedRegions)
            : RebuildLearnedColumns(command, effectiveHeaders);
        var templateRegions = confirmedRegions.Select(item => new DocumentTemplateRegionInput
        {
            RegionIndex = item.Region.RegionIndex,
            Headers = item.Headers,
            HeaderRowIndex = item.Region.HeaderRowIndex,
            HeaderRowCount = item.Region.HeaderRowCount,
            DataStartRowIndex = item.Region.DataStartRowIndex,
            DataEndRowIndex = item.Region.DataEndRowIndex,
            ProjectColumnIndex = item.Region.IsSpecificationOnly ? null : item.Region.ProjectColumnIndex,
            SpecificationColumnIndex = item.Region.SpecificationColumnIndex,
            AcceptanceColumnIndex = item.Region.AcceptanceColumnIndex,
            RemarkColumnIndex = item.Region.RemarkColumnIndex,
            IsSpecificationOnly = item.Region.IsSpecificationOnly
        }).ToList();

        var customerLock = CustomerConfirmLocks.GetOrAdd(command.CustomerId, _ => new SemaphoreSlim(1, 1));
        await customerLock.WaitAsync(cancellationToken);
        try
        {
            await using var templateOperationLock = await _unitOfWork.AcquireOperationLockAsync(
                $"document-template:{command.CustomerId}",
                cancellationToken);
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var template = await _templateService.SaveTemplateAsync(
                    command.CustomerId,
                    string.IsNullOrWhiteSpace(command.TemplateName)
                        ? $"客户{command.CustomerId}-结构模板"
                        : command.TemplateName.Trim(),
                    effectiveHeaders,
                    new ColumnMapping
                    {
                        ProjectColumn = primaryRegion.IsSpecificationOnly ? null : primaryRegion.ProjectColumnIndex,
                        SpecificationColumn = primaryRegion.SpecificationColumnIndex,
                        AcceptanceColumn = primaryRegion.AcceptanceColumnIndex,
                        RemarkColumn = primaryRegion.RemarkColumnIndex,
                        HeaderRowIndex = primaryRegion.HeaderRowIndex,
                        HeaderRowCount = primaryRegion.HeaderRowCount,
                        DataStartRowIndex = primaryRegion.DataStartRowIndex
                    },
                    primaryRegion.DataEndRowIndex,
                    primaryRegion.IsSpecificationOnly,
                    command.TableKind,
                    command.Recommendation,
                    command.UserModifiedStructure,
                    cancellationToken,
                    templateRegions,
                    operationLockAlreadyHeld: true);

                var learningResult = await _learningService.ApplyLearningAsync(
                    command.CustomerId,
                    command.TemplateName,
                    command.TableKind,
                    command.Recommendation,
                    effectiveLearnedColumns,
                    cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return new SmartConfigurationConfirmResult
                {
                    TemplateSaved = true,
                    TemplateId = template.Id,
                    LearnedRuleCount = learningResult.LearnedRuleCount,
                    PromotedGlobalRuleCount = learningResult.PromotedGlobalRuleCount,
                    LearningSucceeded = true
                };
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
        finally
        {
            customerLock.Release();
        }
    }

    private static SmartConfigurationConfirmRegion ToLegacyConfirmRegion(SmartConfigurationConfirmCommand command)
    {
        return new SmartConfigurationConfirmRegion
        {
            RegionIndex = 0,
            Headers = command.Headers,
            HeaderRowIndex = command.HeaderRowIndex,
            HeaderRowCount = command.HeaderRowCount,
            DataStartRowIndex = command.DataStartRowIndex,
            DataEndRowIndex = command.DataEndRowIndex,
            ProjectColumnIndex = command.ProjectColumnIndex,
            SpecificationColumnIndex = command.SpecificationColumnIndex,
            AcceptanceColumnIndex = command.AcceptanceColumnIndex,
            RemarkColumnIndex = command.RemarkColumnIndex,
            IsSpecificationOnly = command.IsSpecificationOnly
        };
    }

    private static SmartConfigurationConfirmCommand ToRegionCommand(
        SmartConfigurationConfirmCommand command,
        SmartConfigurationConfirmRegion region)
    {
        return new SmartConfigurationConfirmCommand
        {
            FileId = command.FileId,
            TableIndex = command.TableIndex,
            CustomerId = command.CustomerId,
            TemplateName = command.TemplateName,
            Headers = region.Headers.Count > 0 ? region.Headers : command.Headers,
            ProjectColumnIndex = region.ProjectColumnIndex,
            SpecificationColumnIndex = region.SpecificationColumnIndex,
            AcceptanceColumnIndex = region.AcceptanceColumnIndex,
            RemarkColumnIndex = region.RemarkColumnIndex,
            HeaderRowIndex = region.HeaderRowIndex,
            HeaderRowCount = region.HeaderRowCount,
            DataStartRowIndex = region.DataStartRowIndex,
            DataEndRowIndex = region.DataEndRowIndex,
            IsSpecificationOnly = region.IsSpecificationOnly,
            TableKind = command.TableKind,
            Recommendation = command.Recommendation,
            UserModifiedStructure = command.UserModifiedStructure
        };
    }

    private static IReadOnlyList<SmartConfigurationLearnedColumn> BuildRegionLearnedColumns(
        IReadOnlyList<(SmartConfigurationConfirmRegion Region, IReadOnlyList<string> Headers)> regions)
    {
        return regions
            .SelectMany(item => new[]
            {
                BuildLearnedColumn(
                    item.Headers,
                    item.Region.IsSpecificationOnly ? null : item.Region.ProjectColumnIndex,
                    ColumnMappingTargetField.Project),
                BuildLearnedColumn(item.Headers, item.Region.SpecificationColumnIndex, ColumnMappingTargetField.Specification),
                BuildLearnedColumn(item.Headers, item.Region.AcceptanceColumnIndex, ColumnMappingTargetField.Acceptance),
                BuildLearnedColumn(item.Headers, item.Region.RemarkColumnIndex, ColumnMappingTargetField.Remark)
            })
            .Where(item => item != null)
            .Select(item => item!)
            .GroupBy(item => $"{(int)item.TargetField}\u001f{item.Header}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static SmartConfigurationLearnedColumn? BuildLearnedColumn(
        IReadOnlyList<string> headers,
        int? columnIndex,
        ColumnMappingTargetField targetField)
    {
        if (!columnIndex.HasValue || columnIndex.Value < 0 || columnIndex.Value >= headers.Count)
        {
            return null;
        }

        var header = headers[columnIndex.Value].Trim();
        return header.Length == 0
            ? null
            : new SmartConfigurationLearnedColumn { Header = header, TargetField = targetField };
    }
    private static void ValidateConfirmCoordinates(
        SmartConfigurationConfirmCommand command,
        bool enforceExcelLeafHeader = false)
    {
        if (command.HeaderRowIndex < 0)
        {
            throw new ApplicationServiceException(400, "表头行索引不能小于0");
        }

        if (command.HeaderRowCount <= 0)
        {
            throw new ApplicationServiceException(400, "表头行数必须大于0");
        }

        if ((long)command.DataStartRowIndex < (long)command.HeaderRowIndex + command.HeaderRowCount)
        {
            throw new ApplicationServiceException(400, "数据起始行不能早于表头结束行");
        }

        if (command.DataEndRowIndex.HasValue && command.DataEndRowIndex.Value < command.DataStartRowIndex)
        {
            throw new ApplicationServiceException(400, "数据结束行不能早于数据起始行");
        }

        if (enforceExcelLeafHeader && command.HeaderRowCount != 1)
        {
            throw new ApplicationServiceException(400, "Excel表头行数必须为1");
        }

        if (enforceExcelLeafHeader &&
            command.HeaderRowIndex != command.DataStartRowIndex - 1)
        {
            throw new ApplicationServiceException(400, "Excel表头必须是数据起始行的上一行");
        }

    }

    private static void ValidateSubmittedRegions(IReadOnlyList<SmartConfigurationConfirmRegion> regions)
    {
        if (regions.Count == 0)
        {
            throw new ApplicationServiceException(400, "至少需要一个数据区域");
        }

        if (regions.Select(region => region.RegionIndex).Distinct().Count() != regions.Count)
        {
            throw new ApplicationServiceException(400, "区域索引不能重复");
        }

        if (regions.Any(region => region.RegionIndex < 0))
        {
            throw new ApplicationServiceException(400, "区域索引不能小于0");
        }

        var orderedIndexes = regions.Select(region => region.RegionIndex).OrderBy(index => index).ToList();
        if (!orderedIndexes.SequenceEqual(Enumerable.Range(0, regions.Count)))
        {
            throw new ApplicationServiceException(400, "区域索引必须从0开始连续递增");
        }

        if (regions.Count > 1 && regions.Any(region => !region.DataEndRowIndex.HasValue))
        {
            throw new ApplicationServiceException(400, "多区域确认必须提供每个区域的数据结束行");
        }

        var submittedIds = regions
            .Select(region => region.RegionId?.Trim())
            .Where(regionId => !string.IsNullOrWhiteSpace(regionId))
            .ToList();
        if (submittedIds.Distinct(StringComparer.Ordinal).Count() != submittedIds.Count)
        {
            throw new ApplicationServiceException(400, "区域标识不能重复");
        }

        foreach (var region in regions)
        {
            var columns = new int?[]
            {
                region.IsSpecificationOnly ? null : region.ProjectColumnIndex,
                region.SpecificationColumnIndex,
                region.AcceptanceColumnIndex,
                region.RemarkColumnIndex
            }.Where(column => column.HasValue).Select(column => column!.Value).ToList();
            if (columns.Distinct().Count() != columns.Count)
            {
                throw new ApplicationServiceException(400, $"区域 {region.RegionIndex + 1} 的字段列不能重复");
            }
        }

        var ordered = regions.OrderBy(region => region.HeaderRowIndex).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            if (ordered[index].HeaderRowIndex <= previous.DataEndRowIndex.GetValueOrDefault())
            {
                throw new ApplicationServiceException(400, "数据区域之间不能重叠");
            }
        }
    }

    private static void ValidateConfirmColumnIndexes(
        SmartConfigurationConfirmCommand command,
        int headerCount,
        bool requireExcelFieldColumns = false)
    {
        ValidateColumnIndex(command.SpecificationColumnIndex, headerCount, "规格列");
        if (requireExcelFieldColumns)
        {
            if (command.IsSpecificationOnly)
            {
                ValidateOptionalColumnIndex(command.ProjectColumnIndex, headerCount, "项目列");
            }
            else
            {
                ValidateRequiredColumnIndex(command.ProjectColumnIndex, headerCount, "项目列");
            }

            ValidateRequiredColumnIndex(command.AcceptanceColumnIndex, headerCount, "验收列");
            ValidateRequiredColumnIndex(command.RemarkColumnIndex, headerCount, "备注列");
            return;
        }

        ValidateOptionalColumnIndex(command.ProjectColumnIndex, headerCount, "项目列");
        ValidateOptionalColumnIndex(command.AcceptanceColumnIndex, headerCount, "验收列");
        ValidateOptionalColumnIndex(command.RemarkColumnIndex, headerCount, "备注列");
    }

    private async Task<IReadOnlyList<string>> ExtractConfirmedHeadersAsync(
        SmartConfigurationConfirmCommand command,
        ConfirmedTableContext context,
        CancellationToken cancellationToken)
    {
        var headerEndRowIndex = (long)command.HeaderRowIndex + command.HeaderRowCount;
        if (headerEndRowIndex > context.Table.RowCount || command.DataStartRowIndex > context.Table.RowCount)
        {
            throw new ApplicationServiceException(400, "确认的行坐标超出表格范围");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = File.OpenRead(context.AbsolutePath);
        var extracted = await context.Parser.ExtractTableDataAsync(
            stream,
            command.TableIndex,
            new ColumnMapping
            {
                HeaderRowIndex = command.HeaderRowIndex,
                HeaderRowCount = command.HeaderRowCount,
                DataStartRowIndex = command.DataStartRowIndex
            },
            cancellationToken: cancellationToken);
        var headers = extracted.Headers.Select(NormalizeConfirmedHeader).ToList();
        if (headers.Count == 0)
        {
            throw new ApplicationServiceException(400, "修正坐标未提取到表头");
        }

        return headers;
    }

    private static string NormalizeConfirmedHeader(string header)
    {
        return string.Join(" / ", header
            .Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }
    private async Task<ConfirmedTableContext> GetConfirmedTableContextAsync(
        SmartConfigurationConfirmCommand command,
        CancellationToken cancellationToken)
    {
        if (command.TableIndex < 0)
        {
            throw new ApplicationServiceException(400, "表格索引不能小于0");
        }

        var file = await _fileAccessService.GetAccessibleFileAsync(command.FileId!.Value, cancellationToken);
        if (file == null)
        {
            throw new ApplicationServiceException(404, $"文件不存在：{command.FileId.Value}");
        }

        if (string.IsNullOrWhiteSpace(file.FilePath))
        {
            throw new ApplicationServiceException(400, "文件路径为空");
        }

        var documentType = file.FileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word;
        var parser = _documentServiceFactory.GetParser(documentType)
            ?? throw new ApplicationServiceException(400, "文档解析器不可用");
        var absolutePath = _documentPathResolver.ResolveAbsolutePath(file.FilePath);
        var table = (await parser.GetTablesAsync(absolutePath, cancellationToken))
            .FirstOrDefault(item => item.Index == command.TableIndex);
        if (table == null)
        {
            throw new ApplicationServiceException(400, $"表格索引超出范围：{command.TableIndex}");
        }

        return new ConfirmedTableContext(parser, absolutePath, table, file.FileType);
    }

    private static void ValidateDataEndRowIndex(
        SmartConfigurationConfirmCommand command,
        int tableRowCount)
    {
        if (command.DataEndRowIndex.HasValue && command.DataEndRowIndex.Value >= tableRowCount)
        {
            throw new ApplicationServiceException(400, "数据结束行超出表格范围");
        }
    }

    private sealed record ConfirmedTableContext(
        IDocumentParser Parser,
        string AbsolutePath,
        TableInfo Table,
        UploadedFileType FileType);

    private sealed record StructuralRowShape(
        int NonEmptyCount,
        IReadOnlySet<int> PopulatedColumns,
        double FillRate,
        double ShortTextRatio,
        double DistinctValueRatio,
        double AverageValueLength,
        int MaximumValueLength,
        int NumericLikeCount)
    {
        public static StructuralRowShape Empty { get; } = new(
            0,
            new HashSet<int>(),
            0,
            0,
            0,
            0,
            0,
            0);
    }

    private sealed record StructuralRepeatedValueRun(
        int StartColumnIndex,
        int EndColumnIndex);

    private static IReadOnlyList<SmartConfigurationLearnedColumn> RebuildLearnedColumns(
        SmartConfigurationConfirmCommand command,
        IReadOnlyList<string> headers)
    {
        return command.LearnedColumns
            .Select(item => new
            {
                item.TargetField,
                ColumnIndex = item.TargetField switch
                {
                    ColumnMappingTargetField.Project => command.ProjectColumnIndex,
                    ColumnMappingTargetField.Specification => command.SpecificationColumnIndex,
                    ColumnMappingTargetField.Acceptance => command.AcceptanceColumnIndex,
                    ColumnMappingTargetField.Remark => command.RemarkColumnIndex,
                    _ => null
                }
            })
            .Where(item => item.ColumnIndex.HasValue &&
                           item.ColumnIndex.Value >= 0 &&
                           item.ColumnIndex.Value < headers.Count &&
                           !string.IsNullOrWhiteSpace(headers[item.ColumnIndex.Value]))
            .GroupBy(item => item.TargetField)
            .Select(group => group.First())
            .Select(item => new SmartConfigurationLearnedColumn
            {
                Header = headers[item.ColumnIndex!.Value].Trim(),
                TargetField = item.TargetField
            })
            .ToList();
    }

    private static void ValidateOptionalColumnIndex(int? columnIndex, int headerCount, string fieldName)
    {
        if (columnIndex.HasValue)
        {
            ValidateColumnIndex(columnIndex.Value, headerCount, fieldName);
        }
    }

    private static void ValidateRequiredColumnIndex(int? columnIndex, int headerCount, string fieldName)
    {
        if (!columnIndex.HasValue)
        {
            throw new ApplicationServiceException(400, $"{fieldName}不能为空");
        }

        ValidateColumnIndex(columnIndex.Value, headerCount, fieldName);
    }

    private static void ValidateColumnIndex(int columnIndex, int headerCount, string fieldName)
    {
        if (columnIndex < 0 || columnIndex >= headerCount)
        {
            throw new ApplicationServiceException(400, $"{fieldName}索引超出表头范围");
        }
    }

    private async Task<SmartConfigurationRecognizedTable> RecognizeTableAsync(
        int? customerId,
        IDocumentParser parser,
        string absolutePath,
        TableInfo? tableInfo,
        TableData tableData,
        TableData fullTableData,
        HeaderProfile headerProfile,
        HeaderKeywordMatcher headerKeywordMatcher,
        IReadOnlyList<ColumnHeaderMappingRule> columnHeaderRules,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        int? llmServiceId,
        Func<bool> tryConsumeStructureAdjudicationBudget,
        Func<bool> tryConsumeColumnSemanticRecallBudget,
        Action<Exception> onLlmFailure,
        Dictionary<string, DocumentStructureCandidate?> structureAdjudicationCache,
        Dictionary<string, IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>> columnSemanticRecallCache,
        CancellationToken cancellationToken)
    {
        var headers = tableData.Headers.ToList();
        SmartConfigurationRecognizedTable? degradedTemplateFallback = null;
        if (customerId.HasValue && headers.Count > 0)
        {
            var templateCandidates = await _templateService.FindMatchingTemplatesAsync(
                customerId.Value,
                headers,
                cancellationToken);
            var degradedTemplateCandidates =
                new List<(SmartConfigurationRecognizedTable Table, DocumentTemplate Template)>();
            foreach (var template in templateCandidates)
            {
                var alignSingleRegionToCurrentHeader =
                    HasExactSingleRegionTemplateHeaders(template, headers);
                var templateMapping = SmartConfigurationRecognizedTableFactory.ToColumnMappingResult(template);
                if (alignSingleRegionToCurrentHeader)
                {
                    templateMapping.Mapping.HeaderRowIndex = headerProfile.HeaderRowIndex;
                    templateMapping.Mapping.HeaderRowCount = headerProfile.HeaderRowCount;
                    templateMapping.Mapping.DataStartRowIndex =
                        headerProfile.HeaderRowIndex + headerProfile.HeaderRowCount;
                }
                var templateHealthCheck = DocumentStructureHealthCheck.Evaluate(
                    tableData,
                    templateMapping,
                    allowMissingProjectColumn: template.IsSpecificationOnly,
                    autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
                    minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
                var templateRecognized = SmartConfigurationRecognizedTableFactory.FromTemplate(
                    tableInfo,
                    tableData,
                    template,
                    headers,
                    templateHealthCheck);
                if (alignSingleRegionToCurrentHeader)
                {
                    templateRecognized = AlignSingleRegionTemplateToCurrentHeader(
                        templateRecognized,
                        template,
                        headerProfile,
                        tableInfo,
                        tableData);
                }
                var recognizedTemplate = SmartConfigurationTableRoutingService.Enrich(
                    tableInfo,
                    tableData,
                    templateRecognized,
                    templateHealthCheck,
                    routingRules,
                    referenceCaseScore: 1);
                var validatedTemplate = ValidateTemplateRegions(
                    fullTableData,
                    recognizedTemplate,
                    headerKeywordMatcher);
                if (!validatedTemplate.Regions.Any(HasErrorIssue))
                {
                    await _templateService.IncrementUsageAsync(template.Id, cancellationToken);
                    return validatedTemplate;
                }

                degradedTemplateCandidates.Add((validatedTemplate, template));
            }

            if (degradedTemplateCandidates.Count > 0)
            {
                var bestDegradedTemplate =
                    SelectBestDegradedTemplateCandidate(degradedTemplateCandidates);
                // 历史模板只在当前文件的区域坐标、列映射和数据健康检查都通过时
                // 才能成为执行真相。已降级模板仅保留为规则识别失败时的兜底，
                // 不能继续覆盖当前文件重新识别出的 A1 数据范围。
                degradedTemplateFallback = bestDegradedTemplate.Table;
            }
        }

        try
        {
            var mapping = await _intelligenceService.IdentifyColumnMappingAsync(
                tableData,
                columnHeaderRules,
                cancellationToken);
            mapping.Mapping.HeaderRowIndex = headerProfile.HeaderRowIndex;
            mapping.Mapping.HeaderRowCount = headerProfile.HeaderRowCount;
            mapping.Mapping.DataStartRowIndex = headerProfile.HeaderRowIndex + headerProfile.HeaderRowCount;

            var recognizedCurrentStructure = await BuildRecognizedTableFromMappingAsync(
                customerId,
                parser,
                absolutePath,
                tableInfo,
                tableData,
                mapping,
                headerKeywordMatcher,
                columnHeaderRules,
                routingRules,
                llmServiceId,
                tryConsumeStructureAdjudicationBudget,
                tryConsumeColumnSemanticRecallBudget,
                onLlmFailure,
                structureAdjudicationCache,
                columnSemanticRecallCache,
                cancellationToken);
            if (degradedTemplateFallback is null)
            {
                return recognizedCurrentStructure;
            }

            var currentIssues = recognizedCurrentStructure.Issues.ToList();
            if (!currentIssues.Any(issue => string.Equals(
                    issue.Code,
                    "TemplateRegionStructureChanged",
                    StringComparison.Ordinal)))
            {
                currentIssues.Add(new SmartConfigurationRecognitionIssue
                {
                    Code = "TemplateRegionStructureChanged",
                    Severity = "Error",
                    Message = "历史模板与当前文件结构不一致，已按当前文件重新识别，请确认范围"
                });
            }
            return recognizedCurrentStructure with
            {
                Decision = "NeedConfirm",
                Issues = currentIssues
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (degradedTemplateFallback is not null)
            {
                _logger.LogWarning(
                    ex,
                    "表格 {TableIndex} 当前结构重新识别失败，降级返回待确认的历史模板",
                    tableData.TableIndex);
                return degradedTemplateFallback;
            }

            _logger.LogWarning(
                ex,
                "表格 {TableIndex} 结构识别失败，返回待确认状态",
                tableData.TableIndex);
            var failed = SmartConfigurationRecognizedTableFactory.FromFailure(
                tableInfo,
                tableData,
                headers);
            return SmartConfigurationTableRoutingService.Enrich(
                tableInfo,
                tableData,
                failed,
                null,
                routingRules);
        }
    }

    private async Task<SmartConfigurationRecognizedTable> BuildRecognizedTableFromMappingAsync(
        int? customerId,
        IDocumentParser parser,
        string absolutePath,
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        HeaderKeywordMatcher headerKeywordMatcher,
        IReadOnlyList<ColumnHeaderMappingRule> columnHeaderRules,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        int? llmServiceId,
        Func<bool> tryConsumeStructureAdjudicationBudget,
        Func<bool> tryConsumeColumnSemanticRecallBudget,
        Action<Exception> onLlmFailure,
        Dictionary<string, DocumentStructureCandidate?> structureAdjudicationCache,
        Dictionary<string, IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>> columnSemanticRecallCache,
        CancellationToken cancellationToken)
    {
        var isSpecificationOnly = IsSpecificationOnlyCandidate(tableData, mapping, columnHeaderRules);
        var healthCheck = DocumentStructureHealthCheck.Evaluate(
            tableData,
            mapping,
            allowMissingProjectColumn: isSpecificationOnly,
            autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
            minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
        var referenceCaseScore = await CalculateReferenceCaseScoreAsync(
            customerId,
            tableInfo?.Name,
            tableData.Headers.ToList(),
            cancellationToken);
        var ruleRecognized = SmartConfigurationRecognizedTableFactory.FromMapping(
            tableInfo,
            tableData,
            mapping,
            healthCheck,
            isSpecificationOnly);
        ruleRecognized = await TryEnrichWithColumnSemanticRecallAsync(
            tableInfo,
            tableData,
            mapping,
            healthCheck,
            ruleRecognized,
            routingRules,
            llmServiceId,
            tryConsumeColumnSemanticRecallBudget,
            onLlmFailure,
            columnSemanticRecallCache,
            cancellationToken);
        if (!healthCheck.CanAutoApply &&
            SmartConfigurationTableRoutingService.ShouldUseStructureAdjudication(
                tableInfo,
                tableData,
                ruleRecognized,
                healthCheck,
                headerKeywordMatcher,
                routingRules))
        {
            var structureCacheKey = BuildStructureAdjudicationCacheKey(tableData, mapping);
            DocumentStructureCandidate? fused;
            if (structureAdjudicationCache.TryGetValue(structureCacheKey, out var cachedFused))
            {
                fused = cachedFused == null
                    ? null
                    : CopyStructureCandidateForTable(cachedFused, tableInfo, tableData);
            }
            else if (tryConsumeStructureAdjudicationBudget())
            {
                fused = await TryFuseWithLlmStructureAsync(
                    customerId,
                    tableInfo,
                    tableData,
                    mapping,
                    llmServiceId,
                    onLlmFailure,
                    cancellationToken);
                structureAdjudicationCache[structureCacheKey] = fused;
            }
            else
            {
                fused = null;
            }

            if (fused != null)
            {
                var forceNeedConfirm = fused.HeaderRowIndex != mapping.Mapping.HeaderRowIndex ||
                                       fused.HeaderRowCount != mapping.Mapping.HeaderRowCount ||
                                       fused.DataStartRowIndex != mapping.Mapping.DataStartRowIndex;
                var totalRowCount = GetTotalRowCount(tableInfo, tableData);
                var reextracted = await TryReextractWithStructureAsync(
                    parser,
                    absolutePath,
                    tableData.TableIndex,
                    fused,
                    totalRowCount,
                    cancellationToken);
                if (reextracted != null)
                {
                    var remapped = await _intelligenceService.IdentifyColumnMappingAsync(
                        reextracted,
                        columnHeaderRules,
                        cancellationToken);
                    remapped.Mapping.HeaderRowIndex = fused.HeaderRowIndex;
                    remapped.Mapping.HeaderRowCount = fused.HeaderRowCount;
                    remapped.Mapping.DataStartRowIndex = fused.DataStartRowIndex;
                    var reextractedRuleCandidate = SmartConfigurationRecognizedTableFactory.ToStructureCandidate(
                        tableInfo,
                        reextracted,
                        remapped);
                    var remerged = DocumentStructureFusion.Merge(
                        reextractedRuleCandidate,
                        fused,
                        allowLlmOverride: true);
                    return BuildFusedRecognizedTable(
                        tableInfo,
                        reextracted,
                        remerged,
                        routingRules,
                        referenceCaseScore,
                        ruleRecognized.SemanticRecallSuggestions,
                        forceNeedConfirm);
                }

                return BuildFusedRecognizedTable(
                    tableInfo,
                    tableData,
                    fused,
                    routingRules,
                    referenceCaseScore,
                    ruleRecognized.SemanticRecallSuggestions,
                    forceNeedConfirm);
            }
        }

        return SmartConfigurationTableRoutingService.Enrich(
            tableInfo,
            tableData,
            ruleRecognized,
            healthCheck,
            routingRules,
            referenceCaseScore);
    }

    private static string BuildStructureAdjudicationCacheKey(
        TableData tableData,
        ColumnMappingResult mapping)
    {
        var headersKey = string.Join(
            "\u001f",
            tableData.Headers.Select(NormalizeColumnSemanticRecallCacheText));
        var mappedFieldsKey = string.Join(
            "|",
            BuildMappedFields(mapping)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value?.ToString() ?? "-"}"));
        var rowRangeKey =
            $"{mapping.Mapping.HeaderRowIndex}:{mapping.Mapping.HeaderRowCount}:{mapping.Mapping.DataStartRowIndex}";

        return $"{headersKey}\u001e{mappedFieldsKey}\u001e{rowRangeKey}";
    }

    private static DocumentStructureCandidate CopyStructureCandidateForTable(
        DocumentStructureCandidate candidate,
        TableInfo? tableInfo,
        TableData tableData) =>
        new()
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name ?? candidate.TableName,
            HeaderRowIndex = candidate.HeaderRowIndex,
            HeaderRowCount = candidate.HeaderRowCount,
            DataStartRowIndex = candidate.DataStartRowIndex,
            DataEndRowIndex = null,
            ProjectColumnIndex = candidate.ProjectColumnIndex,
            SpecificationColumnIndex = candidate.SpecificationColumnIndex,
            AcceptanceColumnIndex = candidate.AcceptanceColumnIndex,
            RemarkColumnIndex = candidate.RemarkColumnIndex,
            IsSpecificationOnly = candidate.IsSpecificationOnly,
            Confidence = candidate.Confidence,
            Source = candidate.Source
        };

    private async Task<SmartConfigurationRecognizedTable> TryEnrichWithColumnSemanticRecallAsync(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        DocumentStructureHealthCheckResult healthCheck,
        SmartConfigurationRecognizedTable ruleRecognized,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        int? llmServiceId,
        Func<bool> tryConsumeColumnSemanticRecallBudget,
        Action<Exception> onLlmFailure,
        Dictionary<string, IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>> columnSemanticRecallCache,
        CancellationToken cancellationToken)
    {
        if (!ShouldUseColumnSemanticRecall(mapping, healthCheck, ruleRecognized))
        {
            return ruleRecognized;
        }

        var route = SmartConfigurationTableRoutingService.Route(
            tableInfo,
            tableData,
            ruleRecognized,
            healthCheck,
            routingRules);
        if (string.Equals(route.Recommendation, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            return ruleRecognized;
        }

        var unmappedHeaders = BuildUnmappedHeaderCandidates(tableData, mapping);
        if (unmappedHeaders.Count == 0)
        {
            return ruleRecognized;
        }

        var cacheKey = BuildColumnSemanticRecallCacheKey(tableData, mapping, unmappedHeaders);
        if (columnSemanticRecallCache.TryGetValue(cacheKey, out var cachedSuggestions))
        {
            return cachedSuggestions.Count == 0
                ? ruleRecognized
                : CopyWithSemanticRecallSuggestions(ruleRecognized, cachedSuggestions);
        }

        if (!tryConsumeColumnSemanticRecallBudget())
        {
            return ruleRecognized;
        }

        try
        {
            using var timeoutCts = CreateColumnSemanticRecallTimeout(cancellationToken);
            var result = await _columnSemanticRecallService.RecallAsync(
                new LlmColumnSemanticRecallRequest
                {
                    TableIndex = tableData.TableIndex,
                    TableName = tableInfo?.Name,
                    Headers = tableData.Headers.ToList(),
                    UnmappedHeaders = unmappedHeaders,
                    MappedFields = BuildMappedFields(mapping),
                    LlmServiceId = llmServiceId,
                    SampleRows = tableData.Rows
                        .Take(5)
                        .Select(row => (IReadOnlyList<string>)row.Cells
                            .OrderBy(cell => cell.ColumnIndex)
                            .Select(cell => cell.Value ?? string.Empty)
                            .ToList())
                        .ToList()
                },
                timeoutCts.Token);

            var suggestions = ValidateColumnSemanticRecallSuggestions(
                result?.Suggestions ?? [],
                unmappedHeaders,
                mapping);
            columnSemanticRecallCache[cacheKey] = suggestions;
            return suggestions.Count == 0
                ? ruleRecognized
                : CopyWithSemanticRecallSuggestions(ruleRecognized, suggestions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            onLlmFailure(ex);
            columnSemanticRecallCache[cacheKey] = [];
            _logger.LogWarning(
                ex,
                "表格 {TableIndex} 列语义召回失败，保留规则识别结果",
                tableData.TableIndex);
            return ruleRecognized;
        }
    }

    private static string BuildColumnSemanticRecallCacheKey(
        TableData tableData,
        ColumnMappingResult mapping,
        IReadOnlyList<ColumnSemanticRecallHeaderCandidate> unmappedHeaders)
    {
        var headersKey = string.Join(
            "\u001f",
            tableData.Headers.Select(NormalizeColumnSemanticRecallCacheText));
        var mappedFieldsKey = string.Join(
            "|",
            BuildMappedFields(mapping)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value?.ToString() ?? "-"}"));
        var unmappedKey = string.Join(
            "|",
            unmappedHeaders.Select(item =>
                $"{item.ColumnIndex}:{NormalizeColumnSemanticRecallCacheText(item.Header)}"));

        return $"{headersKey}\u001e{mappedFieldsKey}\u001e{unmappedKey}";
    }

    private static string NormalizeColumnSemanticRecallCacheText(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool ShouldUseColumnSemanticRecall(
        ColumnMappingResult mapping,
        DocumentStructureHealthCheckResult healthCheck,
        SmartConfigurationRecognizedTable ruleRecognized)
    {
        if (healthCheck.CanAutoApply)
        {
            return false;
        }

        if (!mapping.Mapping.SpecificationColumn.HasValue)
        {
            return true;
        }

        if (!ruleRecognized.IsSpecificationOnly && !mapping.Mapping.ProjectColumn.HasValue)
        {
            return true;
        }

        return !mapping.Mapping.AcceptanceColumn.HasValue;
    }

    private static IReadOnlyList<ColumnSemanticRecallHeaderCandidate> BuildUnmappedHeaderCandidates(
        TableData tableData,
        ColumnMappingResult mapping)
    {
        var mappedColumns = new[]
            {
                mapping.Mapping.ProjectColumn,
                mapping.Mapping.SpecificationColumn,
                mapping.Mapping.AcceptanceColumn,
                mapping.Mapping.RemarkColumn
            }
            .Where(column => column.HasValue)
            .Select(column => column!.Value)
            .ToHashSet();
        var knownDetails = mapping.Details.ToDictionary(detail => detail.ColumnIndex);

        return tableData.Headers
            .Select((header, index) => new { Header = header?.Trim() ?? string.Empty, ColumnIndex = index })
            .Where(item => item.Header.Length is > 0 and <= 40)
            .Where(item => !mappedColumns.Contains(item.ColumnIndex))
            .Where(item => !knownDetails.TryGetValue(item.ColumnIndex, out var detail) ||
                           detail.ColumnType == ColumnType.Unknown)
            .Select(item => new ColumnSemanticRecallHeaderCandidate
            {
                ColumnIndex = item.ColumnIndex,
                Header = item.Header
            })
            .ToList();
    }

    private static IReadOnlyDictionary<string, int?> BuildMappedFields(ColumnMappingResult mapping)
    {
        return new Dictionary<string, int?>
        {
            ["Project"] = mapping.Mapping.ProjectColumn,
            ["Specification"] = mapping.Mapping.SpecificationColumn,
            ["Acceptance"] = mapping.Mapping.AcceptanceColumn,
            ["Remark"] = mapping.Mapping.RemarkColumn
        };
    }

    private static List<SmartConfigurationColumnSemanticRecallSuggestion> ValidateColumnSemanticRecallSuggestions(
        IReadOnlyList<LlmColumnSemanticRecallSuggestion> suggestions,
        IReadOnlyList<ColumnSemanticRecallHeaderCandidate> unmappedHeaders,
        ColumnMappingResult mapping)
    {
        var unmappedByIndex = unmappedHeaders.ToDictionary(item => item.ColumnIndex);
        var occupiedFields = BuildMappedFields(mapping)
            .Where(pair => pair.Value.HasValue)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var acceptedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedColumns = new HashSet<int>();
        var valid = new List<SmartConfigurationColumnSemanticRecallSuggestion>();

        foreach (var suggestion in suggestions
                     .OrderByDescending(item => item.Confidence)
                     .ThenBy(item => item.ColumnIndex))
        {
            if (!unmappedByIndex.TryGetValue(suggestion.ColumnIndex, out var header))
            {
                continue;
            }

            var targetField = NormalizeSemanticRecallTargetField(suggestion.TargetField);
            if (targetField == null || occupiedFields.Contains(targetField))
            {
                continue;
            }

            if (targetField == "Acceptance" &&
                AcceptanceResultHeaderPolicy.IsAcceptanceMethodHeader(header.Header))
            {
                continue;
            }

            if (acceptedColumns.Contains(suggestion.ColumnIndex) ||
                (!string.Equals(targetField, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                 acceptedFields.Contains(targetField)))
            {
                continue;
            }

            acceptedColumns.Add(suggestion.ColumnIndex);
            if (!string.Equals(targetField, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                acceptedFields.Add(targetField);
            }

            valid.Add(new SmartConfigurationColumnSemanticRecallSuggestion
            {
                ColumnIndex = suggestion.ColumnIndex,
                Header = string.IsNullOrWhiteSpace(suggestion.Header) ? header.Header : suggestion.Header.Trim(),
                TargetField = targetField,
                Confidence = Math.Clamp(suggestion.Confidence, 0, 1),
                Reason = suggestion.Reason,
                Source = "SemanticRecall"
            });
        }

        return valid
            .OrderBy(item => item.ColumnIndex)
            .ToList();
    }

    private static string? NormalizeSemanticRecallTargetField(string? value)
    {
        return value?.Trim() switch
        {
            "Project" => "Project",
            "Specification" => "Specification",
            "Acceptance" => "Acceptance",
            "Remark" => "Remark",
            "Unknown" => "Unknown",
            _ => null
        };
    }

    private static SmartConfigurationRecognizedTable CopyWithSemanticRecallSuggestions(
        SmartConfigurationRecognizedTable table,
        IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion> suggestions,
        bool forceNeedConfirm = true)
    {
        return table with
        {
            Decision = forceNeedConfirm ? "NeedConfirm" : table.Decision,
            SemanticRecallSuggestions = suggestions.ToList()
        };
    }

    private async Task<DocumentStructureCandidate?> TryFuseWithLlmStructureAsync(
        int? customerId,
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        int? llmServiceId,
        Action<Exception> onLlmFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            var ruleCandidate = SmartConfigurationRecognizedTableFactory.ToStructureCandidate(
                tableInfo,
                tableData,
                mapping);
            using var timeoutCts = CreateStructureAdjudicationTimeout(cancellationToken);
            var adjudication = await _structureAdjudicationService.AdjudicateAsync(
                new LlmDocumentStructureAdjudicationRequest
                {
                    RuleCandidates = [ruleCandidate],
                    DocumentTablesJson = SerializeTableForStructureAdjudication(tableInfo, tableData, ruleCandidate),
                    ReferenceCases = await BuildReferenceCasesAsync(customerId, tableInfo?.Name, tableData.Headers.ToList(), cancellationToken),
                    LlmServiceId = llmServiceId
                },
                timeoutCts.Token);
            var llmCandidate = adjudication?.Tables.FirstOrDefault(table => table.TableIndex == tableData.TableIndex);
            if (llmCandidate != null && !IsValidHeaderStructure(llmCandidate, GetTotalRowCount(tableInfo, tableData)))
            {
                return null;
            }

            var fused = DocumentStructureFusion.Merge(ruleCandidate, llmCandidate, allowLlmOverride: true);
            if (fused.Source != DocumentStructureCandidateSource.Fused)
            {
                return null;
            }

            return fused;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            onLlmFailure(ex);
            _logger.LogWarning(
                ex,
                "表格 {TableIndex} LLM 结构裁决失败，保留规则识别待确认状态",
                tableData.TableIndex);
            return null;
        }
    }

    private async Task<TableData?> TryReextractWithStructureAsync(
        IDocumentParser parser,
        string absolutePath,
        int tableIndex,
        DocumentStructureCandidate candidate,
        int totalRowCount,
        CancellationToken cancellationToken)
    {
        if (!IsValidHeaderStructure(candidate, totalRowCount))
        {
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(absolutePath);
            return await parser.ExtractTableDataAsync(
                stream,
                tableIndex,
                new ColumnMapping
                {
                    HeaderRowIndex = candidate.HeaderRowIndex,
                    HeaderRowCount = candidate.HeaderRowCount,
                    DataStartRowIndex = candidate.DataStartRowIndex
                },
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "表格 {TableIndex} 按 LLM 表头结构重新提取失败，保留规则识别结果",
                tableIndex);
            return null;
        }
    }

    private static bool IsValidHeaderStructure(DocumentStructureCandidate candidate, int totalRowCount)
    {
        return totalRowCount > 0 &&
               candidate.HeaderRowIndex >= 0 &&
               candidate.HeaderRowCount > 0 &&
               candidate.HeaderRowIndex < totalRowCount &&
               candidate.HeaderRowIndex + candidate.HeaderRowCount <= totalRowCount &&
               candidate.DataStartRowIndex >= candidate.HeaderRowIndex + candidate.HeaderRowCount &&
               candidate.DataStartRowIndex < totalRowCount;
    }

    private static int GetTotalRowCount(TableInfo? tableInfo, TableData tableData)
    {
        return tableInfo?.RowCount > 0
            ? tableInfo.RowCount
            : tableData.TotalRowCount;
    }

    private CancellationTokenSource CreateStructureAdjudicationTimeout(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(_options.StructureAdjudicationTimeoutSeconds, 1, 300);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return cts;
    }

    private CancellationTokenSource CreateColumnSemanticRecallTimeout(CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Clamp(_options.ColumnSemanticRecallTimeoutSeconds, 1, 300);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return cts;
    }

    private double GetAutoApplyConfidenceThreshold()
    {
        return Math.Clamp(_options.AutoApplyConfidenceThreshold, 0, 1);
    }

    private double GetMinimumSpecificationNonEmptyRate()
    {
        return Math.Clamp(_options.MinimumSpecificationNonEmptyRate, 0, 1);
    }

    private SmartConfigurationRecognizedTable BuildFusedRecognizedTable(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentStructureCandidate candidate,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        double referenceCaseScore,
        IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>? semanticRecallSuggestions = null,
        bool forceNeedConfirm = false)
    {
        var healthCheck = DocumentStructureHealthCheck.Evaluate(
            tableData,
            SmartConfigurationRecognizedTableFactory.ToColumnMappingResult(candidate),
            allowMissingProjectColumn: candidate.IsSpecificationOnly,
            autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
            minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
        var recognized = SmartConfigurationTableRoutingService.Enrich(
            tableInfo,
            tableData,
            SmartConfigurationRecognizedTableFactory.FromCandidate(
                tableInfo,
                tableData,
                candidate,
                healthCheck),
            healthCheck,
            routingRules,
            referenceCaseScore);
        if (forceNeedConfirm)
        {
            // LLM 修改了表头或数据边界时必须让用户确认；只补齐列映射且健康检查通过时仍可自动应用。
            recognized = recognized with
            {
                Decision = "NeedConfirm",
                Regions = recognized.Regions.Select(region => region with { Decision = "NeedConfirm" }).ToList()
            };
        }
        return semanticRecallSuggestions is { Count: > 0 }
            ? CopyWithSemanticRecallSuggestions(recognized, semanticRecallSuggestions, forceNeedConfirm: false)
            : recognized;
    }

    private async Task<double> CalculateReferenceCaseScoreAsync(
        int? customerId,
        string? tableName,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue || headers.Count == 0)
        {
            return 0;
        }

        var referenceCases = await _templateService.FindReferenceCasesAsync(
            customerId.Value,
            headers,
            maxCount: 1,
            cancellationToken,
            tableName);
        return referenceCases.Count == 0
            ? 0
            : Math.Clamp(referenceCases[0].Similarity, 0, 1);
    }

    private static string SerializeTableForStructureAdjudication(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentStructureCandidate ruleCandidate)
    {
        var payload = new[]
        {
            new
            {
                tableIndex = tableData.TableIndex,
                tableName = tableInfo?.Name,
                rowCoordinateSystem = "zeroBasedOriginalTableRowIndex",
                totalRowCount = GetTotalRowCount(tableInfo, tableData),
                headerRows = new[]
                {
                    new
                    {
                        rowIndex = ruleCandidate.HeaderRowIndex,
                        rowSpan = ruleCandidate.HeaderRowCount,
                        cells = tableData.Headers
                    }
                },
                sampleRows = tableData.Rows
                    .Take(5)
                    .Select((row, index) => new
                    {
                        rowIndex = ruleCandidate.DataStartRowIndex + index,
                        cells = row.Cells
                            .OrderBy(cell => cell.ColumnIndex)
                            .Select(cell => cell.Value)
                            .ToArray()
                    })
                    .ToArray(),
                headers = tableData.Headers,
                rows = tableData.Rows
                    .Take(5)
                    .Select(row => row.Cells
                        .OrderBy(cell => cell.ColumnIndex)
                        .Select(cell => cell.Value)
                        .ToArray())
                    .ToArray()
            }
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private async Task<IReadOnlyList<DocumentStructureReferenceCase>> BuildReferenceCasesAsync(
        int? customerId,
        string? tableName,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue || headers.Count == 0)
        {
            return [];
        }

        return await _templateService.FindReferenceCasesAsync(
            customerId.Value,
            headers,
            maxCount: 3,
            cancellationToken,
            tableName);
    }

    private async Task<(
        IReadOnlyList<ColumnHeaderMappingRule> All,
        IReadOnlyList<ColumnHeaderMappingRule> ConflictEligible)> BuildColumnHeaderRuleSetsAsync(
        int? customerId,
        CancellationToken cancellationToken)
    {
        var rules = await _unitOfWork.ColumnMappingRules.GetEffectiveForCustomerAsync(customerId);
        var effectiveRules = rules
            .Where(rule => ToColumnType(rule.TargetField) != ColumnType.Unknown)
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();
        IReadOnlyList<ColumnHeaderMappingRule> MapRules(
            IEnumerable<ColumnMappingRule> source) =>
            source.Select(rule => new ColumnHeaderMappingRule(
                    ToColumnType(rule.TargetField),
                    ToColumnHeaderMatchMode(rule.MatchMode),
                    rule.Pattern.Trim(),
                    rule.Priority,
                    customerId.HasValue && rule.CustomerId == customerId.Value))
                .ToList();

        return (
            MapRules(effectiveRules),
            // 自动学习用于改善下一次映射，不能反过来增加人工确认项。
            MapRules(effectiveRules.Where(rule =>
                rule.Source != ColumnMappingRuleSource.Learned)));
    }

    private static ColumnType ToColumnType(ColumnMappingTargetField targetField) => targetField switch
    {
        ColumnMappingTargetField.Project => ColumnType.Project,
        ColumnMappingTargetField.Specification => ColumnType.Specification,
        ColumnMappingTargetField.Acceptance => ColumnType.Acceptance,
        ColumnMappingTargetField.Remark => ColumnType.Remark,
        _ => ColumnType.Unknown
    };

    private static ColumnHeaderMatchMode ToColumnHeaderMatchMode(ColumnMappingMatchMode matchMode) => matchMode switch
    {
        ColumnMappingMatchMode.Equals => ColumnHeaderMatchMode.Equals,
        ColumnMappingMatchMode.Regex => ColumnHeaderMatchMode.Regex,
        _ => ColumnHeaderMatchMode.Contains
    };

    private static bool TryConsumeLlmBudget(ref int remainingGlobalBudget, ref int remainingChannelBudget)
    {
        if (remainingGlobalBudget <= 0 || remainingChannelBudget <= 0)
        {
            return false;
        }

        remainingGlobalBudget--;
        remainingChannelBudget--;
        return true;
    }

    private static SmartConfigurationRecognizedTable AddLlmAssistanceIssue(
        SmartConfigurationRecognizedTable table,
        string? message)
    {
        if (string.IsNullOrWhiteSpace(message) ||
            table.Issues.Any(issue =>
                string.Equals(issue.Code, "LlmAssistanceUnavailable", StringComparison.Ordinal)))
        {
            return table;
        }

        return table with
        {
            Issues =
            [
                .. table.Issues,
                new SmartConfigurationRecognitionIssue
                {
                    Code = "LlmAssistanceUnavailable",
                    Severity = "Warning",
                    Message = message
                }
            ]
        };
    }

    private static string BuildLlmAssistanceFailureMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "所选 AI 增强服务响应超时，已停止本次文档后续 AI 调用并保留规则识别结果",
            AiServiceUnavailableException => "所选 AI 增强服务不可用，已停止本次文档后续 AI 调用并保留规则识别结果",
            _ => "所选 AI 增强服务调用失败，已停止本次文档后续 AI 调用并保留规则识别结果"
        };
    }

    private static bool IsSpecificationOnlyCandidate(
        TableData tableData,
        ColumnMappingResult mapping,
        IReadOnlyList<ColumnHeaderMappingRule> columnHeaderRules)
    {
        if (mapping.Mapping.ProjectColumn.HasValue || !mapping.Mapping.SpecificationColumn.HasValue)
        {
            return false;
        }

        var mappedColumns = mapping.Mapping.GetMappedColumns().ToHashSet();
        var hasUnmappedDataColumn = Enumerable.Range(0, tableData.ColumnCount)
            .Where(columnIndex => !mappedColumns.Contains(columnIndex))
            .Any(columnIndex => tableData.Rows.Any(row =>
                !string.IsNullOrWhiteSpace(row.GetValue(columnIndex))));
        if (hasUnmappedDataColumn)
        {
            return false;
        }

        var projectRules = columnHeaderRules
            .Where(rule => rule.ColumnType == ColumnType.Project)
            .ToList();
        return !tableData.Headers.Any(header =>
            ColumnHeaderRuleMatcher.TryNormalizeHeader(header, out var normalizedHeader) &&
            projectRules.Any(rule => ColumnHeaderRuleMatcher.MatchNormalizedHeader(normalizedHeader, rule).Matched));
    }

}

internal readonly record struct HeaderProfile(int HeaderRowIndex, int HeaderRowCount);

internal readonly record struct HeaderCandidateRank(
    double Confidence,
    bool IsCustomerSpecific,
    int Priority);

internal sealed class HeaderKeywordMatcher
{
    private readonly IReadOnlyList<ColumnHeaderMappingRule> _rules;

    private HeaderKeywordMatcher(IReadOnlyList<ColumnHeaderMappingRule> rules)
    {
        _rules = rules;
    }

    public static HeaderKeywordMatcher FromRules(IReadOnlyList<ColumnHeaderMappingRule> rules)
    {
        var effectiveRules = rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .ToList();

        return new HeaderKeywordMatcher(effectiveRules);
    }

    public bool Contains(string value)
    {
        return ColumnHeaderRuleMatcher.TryNormalizeHeader(value, out var normalizedHeader) &&
               _rules.Any(rule => ColumnHeaderRuleMatcher.MatchNormalizedHeader(normalizedHeader, rule).Matched);
    }
    public bool Matches(ColumnType columnType, string? value)
    {
        return ExpandCompositeHeaderValues(value)
            .Any(candidate =>
                ColumnHeaderRuleMatcher.TryNormalizeHeader(candidate, out var normalizedHeader) &&
                _rules
                    .Where(rule => rule.ColumnType == columnType)
                    .Any(rule => ColumnHeaderRuleMatcher.MatchNormalizedHeader(normalizedHeader, rule).Matched));
    }

    public HeaderCandidateRank GetRank(ColumnType columnType, string? value)
    {
        return ExpandCompositeHeaderValues(value)
            .Select(candidate =>
                ColumnHeaderRuleMatcher.TryNormalizeHeader(candidate, out var normalizedHeader)
                    ? _rules
                        .Where(rule => rule.ColumnType == columnType)
                        .Select(rule => new
                        {
                            Rule = rule,
                            Match = ColumnHeaderRuleMatcher.MatchNormalizedHeader(
                                normalizedHeader,
                                rule)
                        })
                        .Where(item => item.Match.Matched)
                        .Select(item => new HeaderCandidateRank(
                            item.Match.Confidence,
                            item.Rule.IsCustomerSpecific,
                            item.Rule.Priority))
                        .OrderByDescending(rank => rank.Confidence)
                        .ThenByDescending(rank => rank.IsCustomerSpecific)
                        .ThenByDescending(rank => rank.Priority)
                        .FirstOrDefault()
                    : default)
            .OrderByDescending(rank => rank.Confidence)
            .ThenByDescending(rank => rank.IsCustomerSpecific)
            .ThenByDescending(rank => rank.Priority)
            .FirstOrDefault();
    }

    private static IEnumerable<string> ExpandCompositeHeaderValues(string? value)
    {
        var header = value?.Trim() ?? string.Empty;
        if (header.Length == 0)
        {
            yield break;
        }

        yield return header;
        foreach (var segment in header.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.Equals(segment, header, StringComparison.Ordinal))
            {
                yield return segment;
            }
        }
    }

    public bool HasAmbiguousCustomerTypeEvidence(IEnumerable<string> values)
    {
        return values.Any(value =>
            ColumnHeaderRuleMatcher.TryNormalizeHeader(value, out var normalizedHeader) &&
            _rules
                .Where(rule => rule.IsCustomerSpecific)
                .Where(rule => ColumnHeaderRuleMatcher.MatchNormalizedHeader(normalizedHeader, rule).Matched)
                .Select(rule => rule.ColumnType)
                .Distinct()
                .Skip(1)
                .Any());
    }

    public bool IsCompleteRepeatedLeafHeader(RowData row)
    {
        var evidence = BuildEvidence(row);
        if (!HasIndependentRequiredTypes(evidence))
        {
            return false;
        }

        return evidence
            .Select(item => item.Value)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1 && Contains(group.Key));
    }

    public bool IsCompleteHeader(RowData row) => HasIndependentRequiredTypes(BuildEvidence(row));

    public bool IsCompleteHeader(IEnumerable<RowData> rows) =>
        HasIndependentRequiredTypes(rows.SelectMany(BuildEvidence).ToList());

    public bool HasProjectAndSpecificationEvidence(RowData row)
    {
        var evidence = BuildEvidence(row);
        return evidence.Any(item => item.MatchedTypes.Contains(ColumnType.Project)) &&
               evidence.Any(item => item.MatchedTypes.Contains(ColumnType.Specification));
    }

    private List<HeaderEvidence> BuildEvidence(RowData row)
    {
        return row.Cells
            .Select(cell => BuildEvidence(cell.ColumnIndex, cell.Value))
            .Where(item => item.Value.Length > 0 && item.MatchedTypes.Count > 0)
            .ToList();
    }

    private HeaderEvidence BuildEvidence(int columnIndex, string? value)
    {
        if (!ColumnHeaderRuleMatcher.TryNormalizeHeader(value, out var normalizedHeader))
        {
            return new HeaderEvidence(columnIndex, string.Empty, []);
        }

        var matchedTypes = _rules
            .Where(rule => ColumnHeaderRuleMatcher.MatchNormalizedHeader(normalizedHeader, rule).Matched)
            .Select(rule => rule.ColumnType)
            .Distinct()
            .ToList();
        return new HeaderEvidence(columnIndex, normalizedHeader, matchedTypes);
    }

    private static bool HasIndependentRequiredTypes(IReadOnlyList<HeaderEvidence> evidence)
    {
        var requiredTypes = new[]
        {
            ColumnType.Project,
            ColumnType.Specification,
            ColumnType.Acceptance,
            ColumnType.Remark
        };
        if (!CanAssignIndependentEvidence(
                requiredTypes,
                evidence,
                0,
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static bool CanAssignIndependentEvidence(
        IReadOnlyList<ColumnType> requiredTypes,
        IReadOnlyList<HeaderEvidence> evidence,
        int typeIndex,
        HashSet<int> usedColumns,
        HashSet<string> usedValues)
    {
        if (typeIndex >= requiredTypes.Count)
        {
            return true;
        }

        foreach (var item in evidence.Where(item => item.MatchedTypes.Contains(requiredTypes[typeIndex])))
        {
            if (usedColumns.Contains(item.ColumnIndex) || usedValues.Contains(item.Value))
            {
                continue;
            }

            usedColumns.Add(item.ColumnIndex);
            usedValues.Add(item.Value);
            if (CanAssignIndependentEvidence(requiredTypes, evidence, typeIndex + 1, usedColumns, usedValues))
            {
                return true;
            }

            usedColumns.Remove(item.ColumnIndex);
            usedValues.Remove(item.Value);
        }

        return false;
    }

    private sealed record HeaderEvidence(
        int ColumnIndex,
        string Value,
        IReadOnlyList<ColumnType> MatchedTypes);
}
