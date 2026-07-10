using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IDocumentIntelligenceService _intelligenceService;
    private readonly ILlmDocumentStructureAdjudicationService _structureAdjudicationService;
    private readonly ILlmColumnSemanticRecallService _columnSemanticRecallService;
    private readonly DocumentTemplateAppService _templateService;
    private readonly SmartConfigurationLearningService _learningService;
    private readonly IUploadedDocumentPathResolver _documentPathResolver;
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

        var file = await _unitOfWork.WordFiles.GetByIdAsync(command.FileId, cancellationToken);
        if (file == null)
        {
            throw new ApplicationServiceException(404, $"文件不存在：{command.FileId}");
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
        var tablesInfo = await parser.GetTablesAsync(absolutePath);
        await using var stream = File.OpenRead(absolutePath);
        var tablesData = await parser.ExtractAllTablesDataAsync(stream);

        var columnHeaderRules = await BuildColumnHeaderRulesAsync(command.CustomerId, cancellationToken);
        var routingRules = await _unitOfWork.SmartStructureRoutingRules.GetEffectiveForCustomerAsync(
            command.CustomerId,
            cancellationToken);
        var headerKeywordMatcher = HeaderKeywordMatcher.FromRules(columnHeaderRules);
        var tables = new List<SmartConfigurationRecognizedTable>();
        var llmCallBudget = Math.Max(0, _options.MaxLlmCallsPerRecognizeDocument);
        var structureAdjudicationBudget = Math.Max(0, _options.MaxStructureAdjudicationCallsPerDocument);
        var columnSemanticRecallBudget = Math.Max(0, _options.MaxColumnSemanticRecallCallsPerDocument);
        var structureAdjudicationCache =
            new Dictionary<string, DocumentStructureCandidate?>(
                StringComparer.Ordinal);
        var columnSemanticRecallCache =
            new Dictionary<string, IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>>(
                StringComparer.Ordinal);
        for (var i = 0; i < tablesData.Count; i++)
        {
            var tableData = tablesData[i];
            var tableInfo = tablesInfo.FirstOrDefault(table => table.Index == tableData.TableIndex)
                ?? tablesInfo.ElementAtOrDefault(i);

            var headerProfile = DetectHeaderProfile(tableData, headerKeywordMatcher);
            if (headerProfile.HeaderRowIndex > 0 || headerProfile.HeaderRowCount > 1)
            {
                await using var tableStream = File.OpenRead(absolutePath);
                tableData = await parser.ExtractTableDataAsync(
                    tableStream,
                    tableData.TableIndex,
                    new ColumnMapping
                    {
                        HeaderRowIndex = headerProfile.HeaderRowIndex,
                        HeaderRowCount = headerProfile.HeaderRowCount,
                        DataStartRowIndex = headerProfile.HeaderRowIndex + headerProfile.HeaderRowCount
                    });
            }

            tables.Add(await RecognizeTableAsync(
                command.CustomerId,
                parser,
                absolutePath,
                tableInfo,
                tableData,
                headerProfile,
                headerKeywordMatcher,
                columnHeaderRules,
                routingRules,
                () => TryConsumeLlmBudget(ref llmCallBudget, ref structureAdjudicationBudget),
                () => TryConsumeLlmBudget(ref llmCallBudget, ref columnSemanticRecallBudget),
                structureAdjudicationCache,
                columnSemanticRecallCache,
                cancellationToken));
        }

        return new SmartConfigurationRecognizeResult
        {
            FileId = command.FileId,
            Tables = tables
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
        var anchorRowIndex = _intelligenceService.DetectHeaderRowIndex(detectionTable, scanRowLimit);
        var headerRowIndex = ExpandHeaderStart(detectionTable, anchorRowIndex, maxHeaderRowCount, headerKeywordMatcher);
        var headerRowCount = DetectHeaderRowCount(detectionTable, headerRowIndex, maxHeaderRowCount, headerKeywordMatcher);
        return new HeaderProfile(headerRowIndex, headerRowCount);
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
            TotalDataRowCount = tableData.TotalDataRowCount
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
            if (!LooksLikeAdditionalHeaderRow(tableData.Rows[rowIndex], headerKeywordMatcher))
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
        if (keywordCount == 0)
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

        if (command.Headers.Count == 0)
        {
            throw new ApplicationServiceException(400, "表头不能为空");
        }

        var template = await _templateService.SaveTemplateAsync(
            command.CustomerId,
            string.IsNullOrWhiteSpace(command.TemplateName)
                ? $"客户{command.CustomerId}-结构模板"
                : command.TemplateName.Trim(),
            command.Headers,
            new ColumnMapping
            {
                ProjectColumn = command.ProjectColumnIndex,
                SpecificationColumn = command.SpecificationColumnIndex,
                AcceptanceColumn = command.AcceptanceColumnIndex,
                RemarkColumn = command.RemarkColumnIndex,
                HeaderRowIndex = command.HeaderRowIndex,
                HeaderRowCount = command.HeaderRowCount,
                DataStartRowIndex = command.DataStartRowIndex
            },
            command.DataEndRowIndex,
            command.IsSpecificationOnly,
            command.TableKind,
            command.Recommendation,
            command.UserModifiedStructure,
            cancellationToken);

        var learningResult = await _learningService.ApplyLearningAsync(
            command.CustomerId,
            command.TemplateName,
            command.TableKind,
            command.Recommendation,
            command.LearnedColumns,
            cancellationToken);

        return new SmartConfigurationConfirmResult
        {
            TemplateSaved = true,
            TemplateId = template.Id,
            LearnedRuleCount = learningResult.LearnedRuleCount,
            PromotedGlobalRuleCount = learningResult.PromotedGlobalRuleCount,
            LearningSucceeded = true
        };
    }

    private async Task<SmartConfigurationRecognizedTable> RecognizeTableAsync(
        int? customerId,
        IDocumentParser parser,
        string absolutePath,
        TableInfo? tableInfo,
        TableData tableData,
        HeaderProfile headerProfile,
        HeaderKeywordMatcher headerKeywordMatcher,
        IReadOnlyList<ColumnHeaderMappingRule> columnHeaderRules,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        Func<bool> tryConsumeStructureAdjudicationBudget,
        Func<bool> tryConsumeColumnSemanticRecallBudget,
        Dictionary<string, DocumentStructureCandidate?> structureAdjudicationCache,
        Dictionary<string, IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>> columnSemanticRecallCache,
        CancellationToken cancellationToken)
    {
        var headers = tableData.Headers.ToList();
        if (customerId.HasValue && headers.Count > 0)
        {
            var template = await _templateService.FindMatchingTemplateAsync(
                customerId.Value,
                headers,
                cancellationToken);
            if (template != null)
            {
                var templateHealthCheck = DocumentStructureHealthCheck.Evaluate(
                    tableData,
                    SmartConfigurationRecognizedTableFactory.ToColumnMappingResult(template),
                    allowMissingProjectColumn: template.IsSpecificationOnly,
                    autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
                    minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
                await _templateService.IncrementUsageAsync(template.Id, cancellationToken);
                return SmartConfigurationTableRoutingService.Enrich(
                    tableInfo,
                    tableData,
                    SmartConfigurationRecognizedTableFactory.FromTemplate(
                        tableInfo,
                        tableData,
                        template,
                        headers,
                        templateHealthCheck),
                    templateHealthCheck,
                    routingRules,
                    referenceCaseScore: 1);
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

            return await BuildRecognizedTableFromMappingAsync(
                customerId,
                parser,
                absolutePath,
                tableInfo,
                tableData,
                mapping,
                headerKeywordMatcher,
                columnHeaderRules,
                routingRules,
                tryConsumeStructureAdjudicationBudget,
                tryConsumeColumnSemanticRecallBudget,
                structureAdjudicationCache,
                columnSemanticRecallCache,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "表格 {TableIndex} 结构识别失败，返回待确认状态",
                tableData.TableIndex);
            var failed = new SmartConfigurationRecognizedTable
            {
                TableIndex = tableData.TableIndex,
                TableName = tableInfo?.Name,
                Headers = headers,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1,
                DataEndRowIndex = tableData.TotalRowCount > 0 ? tableData.TotalRowCount - 1 : null,
                Confidence = 0,
                Source = "Failed",
                Decision = "NeedConfirm"
            };
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
        Func<bool> tryConsumeStructureAdjudicationBudget,
        Func<bool> tryConsumeColumnSemanticRecallBudget,
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
            tryConsumeColumnSemanticRecallBudget,
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
                fused = await TryFuseWithLlmStructureAsync(customerId, tableInfo, tableData, mapping, cancellationToken);
                structureAdjudicationCache[structureCacheKey] = fused;
            }
            else
            {
                fused = null;
            }

            if (fused != null)
            {
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
                        ruleRecognized.SemanticRecallSuggestions);
                }

                return BuildFusedRecognizedTable(
                    tableInfo,
                    tableData,
                    fused,
                    routingRules,
                    referenceCaseScore,
                    ruleRecognized.SemanticRecallSuggestions);
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
        Func<bool> tryConsumeColumnSemanticRecallBudget,
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
        catch (Exception ex)
        {
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
                    ReferenceCases = await BuildReferenceCasesAsync(customerId, tableInfo?.Name, tableData.Headers.ToList(), cancellationToken)
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
        catch (Exception ex)
        {
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
            await using var stream = File.OpenRead(absolutePath);
            return await parser.ExtractTableDataAsync(
                stream,
                tableIndex,
                new ColumnMapping
                {
                    HeaderRowIndex = candidate.HeaderRowIndex,
                    HeaderRowCount = candidate.HeaderRowCount,
                    DataStartRowIndex = candidate.DataStartRowIndex
                });
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
        IReadOnlyList<SmartConfigurationColumnSemanticRecallSuggestion>? semanticRecallSuggestions = null)
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

    private async Task<IReadOnlyList<ColumnHeaderMappingRule>> BuildColumnHeaderRulesAsync(
        int? customerId,
        CancellationToken cancellationToken)
    {
        var rules = await _unitOfWork.ColumnMappingRules.GetEffectiveForCustomerAsync(customerId);
        return rules
            .Where(rule => ToColumnType(rule.TargetField) != ColumnType.Unknown)
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern))
            .Select(rule => new ColumnHeaderMappingRule(
                ToColumnType(rule.TargetField),
                ToColumnHeaderMatchMode(rule.MatchMode),
                rule.Pattern.Trim(),
                rule.Priority,
                customerId.HasValue && rule.CustomerId == customerId.Value))
            .ToList();
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
            projectRules.Any(rule => ColumnHeaderRuleMatcher.IsMatch(header, rule)));
    }

}

internal readonly record struct HeaderProfile(int HeaderRowIndex, int HeaderRowCount);

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
        return _rules.Any(rule => ColumnHeaderRuleMatcher.IsMatch(value, rule));
    }
}
