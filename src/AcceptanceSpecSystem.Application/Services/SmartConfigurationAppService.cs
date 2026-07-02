using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
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
}
