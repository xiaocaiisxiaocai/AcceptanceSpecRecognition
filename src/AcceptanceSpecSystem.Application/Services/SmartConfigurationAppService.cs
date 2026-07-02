using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 智能结构配置应用服务。
/// </summary>
public sealed class SmartConfigurationAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IDocumentIntelligenceService _intelligenceService;
    private readonly ILlmDocumentStructureAdjudicationService _structureAdjudicationService;
    private readonly DocumentTemplateAppService _templateService;
    private readonly IUploadedDocumentPathResolver _documentPathResolver;
    private readonly ILogger<SmartConfigurationAppService> _logger;

    public SmartConfigurationAppService(
        IUnitOfWork unitOfWork,
        DocumentServiceFactory documentServiceFactory,
        IDocumentIntelligenceService intelligenceService,
        ILlmDocumentStructureAdjudicationService structureAdjudicationService,
        DocumentTemplateAppService templateService,
        IUploadedDocumentPathResolver documentPathResolver,
        ILogger<SmartConfigurationAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _documentServiceFactory = documentServiceFactory;
        _intelligenceService = intelligenceService;
        _structureAdjudicationService = structureAdjudicationService;
        _templateService = templateService;
        _documentPathResolver = documentPathResolver;
        _logger = logger;
    }

    /// <summary>
    /// 归档自动配置兼容入口；后续 recognize 任务会替换为全文档识别。
    /// </summary>
    public async Task<AutoConfigResult> AutoConfigureAsync(
        int fileId,
        int? customerId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "开始自动识别配置：FileId={FileId}, CustomerId={CustomerId}",
            fileId,
            customerId);

        var file = await _unitOfWork.WordFiles.GetByIdAsync(fileId);
        if (file == null)
        {
            throw new ApplicationServiceException(404, $"文件不存在：{fileId}");
        }

        var documentType = file.FileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word;
        var parser = _documentServiceFactory.GetParser(documentType)
            ?? throw new ApplicationServiceException(400, "文档解析器不可用");

        if (string.IsNullOrWhiteSpace(file.FilePath))
        {
            throw new ApplicationServiceException(400, "文件路径为空");
        }

        var absolutePath = _documentPathResolver.ResolveAbsolutePath(file.FilePath);
        var tablesInfo = await parser.GetTablesAsync(absolutePath);
        if (tablesInfo.Count == 0)
        {
            throw new ApplicationServiceException(400, "文档中没有找到表格");
        }

        await using var stream = File.OpenRead(absolutePath);
        var tablesData = await parser.ExtractAllTablesDataAsync(stream);

        if (customerId.HasValue && tablesData.Count > 0)
        {
            var firstTable = tablesData[0];
            var template = await _templateService.FindMatchingTemplateAsync(
                customerId.Value,
                firstTable.Headers.ToList(),
                cancellationToken);

            if (template != null)
            {
                await _templateService.IncrementUsageAsync(template.Id, cancellationToken);
                return new AutoConfigResult
                {
                    TableIndex = 0,
                    ColumnMapping = new ColumnMapping
                    {
                        ProjectColumn = template.ProjectColumnIndex,
                        SpecificationColumn = template.SpecificationColumnIndex,
                        AcceptanceColumn = template.AcceptanceColumnIndex,
                        RemarkColumn = template.RemarkColumnIndex,
                        HeaderRowIndex = template.HeaderRowIndex,
                        HeaderRowCount = template.HeaderRowCount,
                        DataStartRowIndex = template.DataStartRowIndex
                    },
                    Confidence = 1.0,
                    Source = IdentificationSource.SavedTemplate,
                    NeedsManualReview = false,
                    Reasoning = $"套用历史模板：{template.TemplateName}（已使用 {template.UsageCount} 次）"
                };
            }
        }

        return await _intelligenceService.AutoConfigureAsync(
            tablesInfo,
            tablesData,
            cancellationToken);
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

        var tables = new List<SmartConfigurationRecognizedTable>();
        for (var i = 0; i < tablesData.Count; i++)
        {
            var tableData = tablesData[i];
            var tableInfo = tablesInfo.FirstOrDefault(table => table.Index == tableData.TableIndex)
                ?? tablesInfo.ElementAtOrDefault(i);

            tables.Add(await RecognizeTableAsync(
                command.CustomerId,
                tableInfo,
                tableData,
                cancellationToken));
        }

        return new SmartConfigurationRecognizeResult
        {
            FileId = command.FileId,
            Tables = tables
        };
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
            cancellationToken);

        var learnedRuleCount = 0;
        var promotedGlobalRuleCount = 0;
        foreach (var learnedColumn in command.LearnedColumns)
        {
            var pattern = learnedColumn.Header.Trim();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var added = await UpsertCustomerLearnedRuleAsync(
                command.CustomerId,
                pattern,
                learnedColumn.TargetField,
                cancellationToken);
            if (added)
            {
                learnedRuleCount++;
            }

            if (await PromoteGlobalRuleIfReadyAsync(
                pattern,
                learnedColumn.TargetField,
                cancellationToken))
            {
                promotedGlobalRuleCount++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SmartConfigurationConfirmResult
        {
            TemplateSaved = true,
            TemplateId = template.Id,
            LearnedRuleCount = learnedRuleCount,
            PromotedGlobalRuleCount = promotedGlobalRuleCount,
            LearningSucceeded = true
        };
    }

    private async Task<bool> UpsertCustomerLearnedRuleAsync(
        int customerId,
        string pattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.ColumnMappingRules.Query(asNoTracking: false)
            .FirstOrDefaultAsync(rule =>
                rule.CustomerId == customerId &&
                rule.TargetField == targetField &&
                rule.Pattern == pattern,
                cancellationToken);

        if (existing != null)
        {
            existing.Source = ColumnMappingRuleSource.Learned;
            existing.MatchMode = ColumnMappingMatchMode.Equals;
            existing.Enabled = true;
            existing.Priority = Math.Max(existing.Priority, 100);
            existing.UpdatedAt = DateTime.UtcNow;
            return false;
        }

        await _unitOfWork.ColumnMappingRules.AddAsync(new ColumnMappingRule
        {
            CustomerId = customerId,
            TargetField = targetField,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = pattern,
            Priority = 100,
            Enabled = true,
            Source = ColumnMappingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        return true;
    }

    private async Task<SmartConfigurationRecognizedTable> RecognizeTableAsync(
        int? customerId,
        TableInfo? tableInfo,
        TableData tableData,
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
                await _templateService.IncrementUsageAsync(template.Id, cancellationToken);
                return BuildRecognizedTableFromTemplate(tableInfo, tableData, template, headers);
            }
        }

        try
        {
            var mapping = await _intelligenceService.IdentifyColumnMappingAsync(
                tableData,
                cancellationToken);
            return await BuildRecognizedTableFromMappingAsync(
                tableInfo,
                tableData,
                mapping,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "表格 {TableIndex} 结构识别失败，返回待确认状态",
                tableData.TableIndex);
            return new SmartConfigurationRecognizedTable
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
        }
    }

    private static SmartConfigurationRecognizedTable BuildRecognizedTableFromTemplate(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentTemplate template,
        List<string> headers)
    {
        return new SmartConfigurationRecognizedTable
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            Headers = headers,
            HeaderRowIndex = template.HeaderRowIndex,
            HeaderRowCount = template.HeaderRowCount,
            DataStartRowIndex = template.DataStartRowIndex,
            DataEndRowIndex = template.DataEndRowIndex,
            ProjectColumnIndex = template.ProjectColumnIndex,
            SpecificationColumnIndex = template.SpecificationColumnIndex,
            AcceptanceColumnIndex = template.AcceptanceColumnIndex,
            RemarkColumnIndex = template.RemarkColumnIndex,
            IsSpecificationOnly = template.IsSpecificationOnly,
            Confidence = 1.0,
            Source = "Template",
            Decision = "AutoApply",
            Fields = BuildFields(
                headers,
                template.ProjectColumnIndex,
                template.SpecificationColumnIndex,
                template.AcceptanceColumnIndex,
                template.RemarkColumnIndex,
                1.0,
                "Template")
        };
    }

    private async Task<SmartConfigurationRecognizedTable> BuildRecognizedTableFromMappingAsync(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        CancellationToken cancellationToken)
    {
        var healthCheck = DocumentStructureHealthCheck.Evaluate(tableData, mapping);
        if (!healthCheck.CanAutoApply)
        {
            var fused = await TryFuseWithLlmStructureAsync(tableInfo, tableData, mapping, cancellationToken);
            if (fused != null)
            {
                return BuildRecognizedTableFromCandidate(tableInfo, tableData, fused);
            }
        }

        return BuildRecognizedTableFromMapping(tableInfo, tableData, mapping, healthCheck);
    }

    private async Task<DocumentStructureCandidate?> TryFuseWithLlmStructureAsync(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        CancellationToken cancellationToken)
    {
        try
        {
            var ruleCandidate = ToStructureCandidate(tableInfo, tableData, mapping);
            var adjudication = await _structureAdjudicationService.AdjudicateAsync(
                new LlmDocumentStructureAdjudicationRequest
                {
                    RuleCandidates = [ruleCandidate],
                    DocumentTablesJson = SerializeTableForStructureAdjudication(tableInfo, tableData)
                },
                cancellationToken);
            var llmCandidate = adjudication?.Tables.FirstOrDefault(table => table.TableIndex == tableData.TableIndex);
            var fused = DocumentStructureFusion.Merge(ruleCandidate, llmCandidate);
            if (fused.Source != DocumentStructureCandidateSource.Fused)
            {
                return null;
            }

            var fusedMapping = ToColumnMappingResult(fused);
            var fusedHealthCheck = DocumentStructureHealthCheck.Evaluate(tableData, fusedMapping);
            return fusedHealthCheck.CanAutoApply ? fused : null;
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

    private static SmartConfigurationRecognizedTable BuildRecognizedTableFromMapping(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        DocumentStructureHealthCheckResult healthCheck)
    {
        var headers = tableData.Headers.ToList();
        var columnMapping = mapping.Mapping;
        return new SmartConfigurationRecognizedTable
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            Headers = headers,
            HeaderRowIndex = columnMapping.HeaderRowIndex,
            HeaderRowCount = columnMapping.HeaderRowCount,
            DataStartRowIndex = columnMapping.DataStartRowIndex,
            DataEndRowIndex = tableData.TotalRowCount > 0 ? tableData.TotalRowCount - 1 : null,
            ProjectColumnIndex = columnMapping.ProjectColumn,
            SpecificationColumnIndex = columnMapping.SpecificationColumn,
            AcceptanceColumnIndex = columnMapping.AcceptanceColumn,
            RemarkColumnIndex = columnMapping.RemarkColumn,
            IsSpecificationOnly = !columnMapping.ProjectColumn.HasValue,
            Confidence = mapping.Confidence,
            Source = "RuleBased",
            Decision = healthCheck.CanAutoApply ? "AutoApply" : "NeedConfirm",
            Fields = BuildFields(
                headers,
                columnMapping.ProjectColumn,
                columnMapping.SpecificationColumn,
                columnMapping.AcceptanceColumn,
                columnMapping.RemarkColumn,
                mapping.Confidence,
                "RuleBased")
        };
    }

    private static SmartConfigurationRecognizedTable BuildRecognizedTableFromCandidate(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentStructureCandidate candidate)
    {
        var headers = tableData.Headers.ToList();
        return new SmartConfigurationRecognizedTable
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            Headers = headers,
            HeaderRowIndex = candidate.HeaderRowIndex,
            HeaderRowCount = candidate.HeaderRowCount,
            DataStartRowIndex = candidate.DataStartRowIndex,
            DataEndRowIndex = candidate.DataEndRowIndex ?? (tableData.TotalRowCount > 0 ? tableData.TotalRowCount - 1 : null),
            ProjectColumnIndex = candidate.ProjectColumnIndex,
            SpecificationColumnIndex = candidate.SpecificationColumnIndex,
            AcceptanceColumnIndex = candidate.AcceptanceColumnIndex,
            RemarkColumnIndex = candidate.RemarkColumnIndex,
            IsSpecificationOnly = candidate.IsSpecificationOnly,
            Confidence = candidate.Confidence,
            Source = "Fused",
            Decision = "AutoApply",
            Fields = BuildFields(
                headers,
                candidate.ProjectColumnIndex,
                candidate.SpecificationColumnIndex,
                candidate.AcceptanceColumnIndex,
                candidate.RemarkColumnIndex,
                candidate.Confidence,
                "Fused")
        };
    }

    private static DocumentStructureCandidate ToStructureCandidate(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping)
    {
        var columnMapping = mapping.Mapping;
        return new DocumentStructureCandidate
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            HeaderRowIndex = columnMapping.HeaderRowIndex,
            HeaderRowCount = columnMapping.HeaderRowCount,
            DataStartRowIndex = columnMapping.DataStartRowIndex,
            DataEndRowIndex = tableData.TotalRowCount > 0 ? tableData.TotalRowCount - 1 : null,
            ProjectColumnIndex = columnMapping.ProjectColumn,
            SpecificationColumnIndex = columnMapping.SpecificationColumn,
            AcceptanceColumnIndex = columnMapping.AcceptanceColumn,
            RemarkColumnIndex = columnMapping.RemarkColumn,
            IsSpecificationOnly = !columnMapping.ProjectColumn.HasValue,
            Confidence = mapping.Confidence,
            Source = DocumentStructureCandidateSource.Rule
        };
    }

    private static ColumnMappingResult ToColumnMappingResult(DocumentStructureCandidate candidate)
    {
        return new ColumnMappingResult
        {
            Confidence = candidate.Confidence,
            Mapping = new ColumnMapping
            {
                ProjectColumn = candidate.ProjectColumnIndex,
                SpecificationColumn = candidate.SpecificationColumnIndex,
                AcceptanceColumn = candidate.AcceptanceColumnIndex,
                RemarkColumn = candidate.RemarkColumnIndex,
                HeaderRowIndex = candidate.HeaderRowIndex,
                HeaderRowCount = candidate.HeaderRowCount,
                DataStartRowIndex = candidate.DataStartRowIndex
            }
        };
    }

    private static string SerializeTableForStructureAdjudication(TableInfo? tableInfo, TableData tableData)
    {
        var payload = new[]
        {
            new
            {
                tableIndex = tableData.TableIndex,
                tableName = tableInfo?.Name,
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

    private static List<SmartConfigurationRecognizedField> BuildFields(
        IReadOnlyList<string> headers,
        int? projectColumn,
        int? specificationColumn,
        int? acceptanceColumn,
        int? remarkColumn,
        double confidence,
        string source)
    {
        return
        [
            BuildField("Project", projectColumn, headers, confidence, source),
            BuildField("Specification", specificationColumn, headers, confidence, source),
            BuildField("Acceptance", acceptanceColumn, headers, confidence, source),
            BuildField("Remark", remarkColumn, headers, confidence, source)
        ];
    }

    private static SmartConfigurationRecognizedField BuildField(
        string field,
        int? columnIndex,
        IReadOnlyList<string> headers,
        double confidence,
        string source)
    {
        return new SmartConfigurationRecognizedField
        {
            Field = field,
            ColumnIndex = columnIndex,
            Header = columnIndex.HasValue &&
                     columnIndex.Value >= 0 &&
                     columnIndex.Value < headers.Count
                ? headers[columnIndex.Value]
                : null,
            Confidence = columnIndex.HasValue ? confidence : 0,
            Source = source
        };
    }

    private async Task<bool> PromoteGlobalRuleIfReadyAsync(
        string pattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var hasGlobal = await _unitOfWork.ColumnMappingRules.Query()
            .AnyAsync(rule =>
                rule.CustomerId == null &&
                rule.TargetField == targetField &&
                rule.Pattern == pattern &&
                rule.Enabled,
                cancellationToken);
        if (hasGlobal)
        {
            return false;
        }

        var learnedCustomerCount = await _unitOfWork.ColumnMappingRules.Query()
            .Where(rule =>
                rule.CustomerId != null &&
                rule.Source == ColumnMappingRuleSource.Learned &&
                rule.TargetField == targetField &&
                rule.Pattern == pattern &&
                rule.Enabled)
            .Select(rule => rule.CustomerId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);
        if (learnedCustomerCount < 2)
        {
            return false;
        }

        await _unitOfWork.ColumnMappingRules.AddAsync(new ColumnMappingRule
        {
            CustomerId = null,
            TargetField = targetField,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = pattern,
            Priority = 80,
            Enabled = true,
            Source = ColumnMappingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        return true;
    }
}
