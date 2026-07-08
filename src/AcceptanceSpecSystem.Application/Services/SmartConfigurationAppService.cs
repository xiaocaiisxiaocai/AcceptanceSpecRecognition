using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
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

        var extraSynonyms = await BuildExtraSynonymsAsync(command.CustomerId, cancellationToken);
        var routingRules = await _unitOfWork.SmartStructureRoutingRules.GetEffectiveForCustomerAsync(
            command.CustomerId,
            cancellationToken);
        var headerKeywordMatcher = HeaderKeywordMatcher.FromExtraSynonyms(extraSynonyms);
        var tables = new List<SmartConfigurationRecognizedTable>();
        var structureAdjudicationBudget = Math.Max(0, _options.MaxStructureAdjudicationCallsPerDocument);
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
                extraSynonyms,
                routingRules,
                () => TryConsumeStructureAdjudicationBudget(ref structureAdjudicationBudget),
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
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>> extraSynonyms,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        Func<bool> tryConsumeStructureAdjudicationBudget,
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
                extraSynonyms,
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
                extraSynonyms,
                routingRules,
                tryConsumeStructureAdjudicationBudget,
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
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>> extraSynonyms,
        IReadOnlyList<SmartStructureRoutingRule> routingRules,
        Func<bool> tryConsumeStructureAdjudicationBudget,
        CancellationToken cancellationToken)
    {
        var isSpecificationOnly = IsSpecificationOnlyCandidate(tableData, mapping, extraSynonyms);
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
        if (!healthCheck.CanAutoApply &&
            SmartConfigurationTableRoutingService.ShouldUseStructureAdjudication(
                tableInfo,
                tableData,
                ruleRecognized,
                healthCheck,
                headerKeywordMatcher,
                routingRules))
        {
            var fused = tryConsumeStructureAdjudicationBudget()
                ? await TryFuseWithLlmStructureAsync(customerId, tableInfo, tableData, mapping, cancellationToken)
                : null;
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
                        extraSynonyms,
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
                    return BuildFusedRecognizedTable(tableInfo, reextracted, remerged, routingRules, referenceCaseScore);
                }

                return BuildFusedRecognizedTable(tableInfo, tableData, fused, routingRules, referenceCaseScore);
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
        double referenceCaseScore)
    {
        var healthCheck = DocumentStructureHealthCheck.Evaluate(
            tableData,
            SmartConfigurationRecognizedTableFactory.ToColumnMappingResult(candidate),
            allowMissingProjectColumn: candidate.IsSpecificationOnly,
            autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
            minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
        return SmartConfigurationTableRoutingService.Enrich(
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

    private async Task<IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>> BuildExtraSynonymsAsync(
        int? customerId,
        CancellationToken cancellationToken)
    {
        var rules = await _unitOfWork.ColumnMappingRules.GetEffectiveForCustomerAsync(customerId);
        return rules
            .GroupBy(rule => ToColumnType(rule.TargetField))
            .Where(group => group.Key != ColumnType.Unknown)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderByDescending(rule => customerId.HasValue && rule.CustomerId == customerId.Value)
                    .ThenByDescending(rule => rule.Priority)
                    .Select(rule => rule.Pattern)
                    .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static ColumnType ToColumnType(ColumnMappingTargetField targetField) => targetField switch
    {
        ColumnMappingTargetField.Project => ColumnType.Project,
        ColumnMappingTargetField.Specification => ColumnType.Specification,
        ColumnMappingTargetField.Acceptance => ColumnType.Acceptance,
        ColumnMappingTargetField.Remark => ColumnType.Remark,
        _ => ColumnType.Unknown
    };

    private static bool TryConsumeStructureAdjudicationBudget(ref int remainingBudget)
    {
        if (remainingBudget <= 0)
        {
            return false;
        }

        remainingBudget--;
        return true;
    }

    private static bool IsSpecificationOnlyCandidate(
        TableData tableData,
        ColumnMappingResult mapping,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>> extraSynonyms)
    {
        if (mapping.Mapping.ProjectColumn.HasValue || !mapping.Mapping.SpecificationColumn.HasValue)
        {
            return false;
        }

        var projectKeywords = extraSynonyms.TryGetValue(ColumnType.Project, out var words)
            ? words
            : [];
        return !tableData.Headers.Any(header =>
            projectKeywords.Any(keyword =>
                !string.IsNullOrWhiteSpace(keyword) &&
                header.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
    }

}

internal readonly record struct HeaderProfile(int HeaderRowIndex, int HeaderRowCount);

internal sealed class HeaderKeywordMatcher
{
    private readonly IReadOnlyList<string> _keywords;

    private HeaderKeywordMatcher(IReadOnlyList<string> keywords)
    {
        _keywords = keywords;
    }

    public static HeaderKeywordMatcher FromExtraSynonyms(
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>> extraSynonyms)
    {
        var keywords = extraSynonyms.Values
            .SelectMany(words => words)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HeaderKeywordMatcher(keywords);
    }

    public bool Contains(string value)
    {
        return _keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
