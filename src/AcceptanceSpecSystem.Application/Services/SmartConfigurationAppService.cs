using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
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

        var tables = new List<SmartConfigurationRecognizedTable>();
        for (var i = 0; i < tablesData.Count; i++)
        {
            var tableData = tablesData[i];
            var tableInfo = tablesInfo.FirstOrDefault(table => table.Index == tableData.TableIndex)
                ?? tablesInfo.ElementAtOrDefault(i);

            var headerRowIndex = _intelligenceService.DetectHeaderRowIndex(
                BuildHeaderDetectionTableData(tableData));
            if (headerRowIndex > 0)
            {
                await using var tableStream = File.OpenRead(absolutePath);
                tableData = await parser.ExtractTableDataAsync(
                    tableStream,
                    tableData.TableIndex,
                    new ColumnMapping
                    {
                        HeaderRowIndex = headerRowIndex,
                        HeaderRowCount = 1,
                        DataStartRowIndex = headerRowIndex + 1
                    });
            }

            tables.Add(await RecognizeTableAsync(
                command.CustomerId,
                tableInfo,
                tableData,
                headerRowIndex,
                cancellationToken));
        }

        return new SmartConfigurationRecognizeResult
        {
            FileId = command.FileId,
            Tables = tables
        };
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

        var learningResult = await _learningService.ApplyLearningAsync(
            command.CustomerId,
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
        TableInfo? tableInfo,
        TableData tableData,
        int headerRowIndex,
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
                return SmartConfigurationRecognizedTableFactory.FromTemplate(tableInfo, tableData, template, headers);
            }
        }

        try
        {
            var mapping = await _intelligenceService.IdentifyColumnMappingAsync(
                tableData,
                cancellationToken);
            if (headerRowIndex > 0)
            {
                mapping.Mapping.HeaderRowIndex = headerRowIndex;
                mapping.Mapping.DataStartRowIndex = headerRowIndex + mapping.Mapping.HeaderRowCount;
            }

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

    private async Task<SmartConfigurationRecognizedTable> BuildRecognizedTableFromMappingAsync(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        CancellationToken cancellationToken)
    {
        var healthCheck = DocumentStructureHealthCheck.Evaluate(
            tableData,
            mapping,
            allowMissingProjectColumn: !mapping.Mapping.ProjectColumn.HasValue,
            autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
            minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
        if (!healthCheck.CanAutoApply)
        {
            var fused = await TryFuseWithLlmStructureAsync(tableInfo, tableData, mapping, cancellationToken);
            if (fused != null)
            {
                return BuildFusedRecognizedTable(tableInfo, tableData, fused);
            }
        }

        return SmartConfigurationRecognizedTableFactory.FromMapping(tableInfo, tableData, mapping, healthCheck);
    }

    private async Task<DocumentStructureCandidate?> TryFuseWithLlmStructureAsync(
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
                    DocumentTablesJson = SerializeTableForStructureAdjudication(tableInfo, tableData)
                },
                timeoutCts.Token);
            var llmCandidate = adjudication?.Tables.FirstOrDefault(table => table.TableIndex == tableData.TableIndex);
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
        DocumentStructureCandidate candidate)
    {
        var healthCheck = DocumentStructureHealthCheck.Evaluate(
            tableData,
            SmartConfigurationRecognizedTableFactory.ToColumnMappingResult(candidate),
            allowMissingProjectColumn: candidate.IsSpecificationOnly,
            autoApplyConfidenceThreshold: GetAutoApplyConfidenceThreshold(),
            minimumSpecificationNonEmptyRate: GetMinimumSpecificationNonEmptyRate());
        return SmartConfigurationRecognizedTableFactory.FromCandidate(
            tableInfo,
            tableData,
            candidate,
            healthCheck);
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

}
