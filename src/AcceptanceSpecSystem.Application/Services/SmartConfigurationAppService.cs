using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
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
    private readonly DocumentTemplateAppService _templateService;
    private readonly ILogger<SmartConfigurationAppService> _logger;

    public SmartConfigurationAppService(
        IUnitOfWork unitOfWork,
        DocumentServiceFactory documentServiceFactory,
        IDocumentIntelligenceService intelligenceService,
        DocumentTemplateAppService templateService,
        ILogger<SmartConfigurationAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _documentServiceFactory = documentServiceFactory;
        _intelligenceService = intelligenceService;
        _templateService = templateService;
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

        var tablesInfo = await parser.GetTablesAsync(file.FilePath);
        if (tablesInfo.Count == 0)
        {
            throw new ApplicationServiceException(400, "文档中没有找到表格");
        }

        await using var stream = File.OpenRead(file.FilePath);
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
