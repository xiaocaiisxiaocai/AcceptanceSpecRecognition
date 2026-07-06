using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 文档模板应用服务
/// </summary>
public sealed class DocumentTemplateAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DocumentTemplateAppService> _logger;

    public DocumentTemplateAppService(
        IUnitOfWork unitOfWork,
        ILogger<DocumentTemplateAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 查找匹配的模板
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <param name="headers">表头列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的模板，如果没有找到返回 null</returns>
    public async Task<DocumentTemplate?> FindMatchingTemplateAsync(
        int customerId,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken = default)
    {
        // 生成表头指纹
        var fingerprint = GenerateHeadersFingerprint(headers);

        // 1. 精确匹配：相同的表头指纹
        var exactMatch = await _unitOfWork.DocumentTemplates
            .Query()
            .Where(t => t.CustomerId == customerId && t.HeadersFingerprint == fingerprint)
            .OrderByDescending(t => t.UsageCount)
            .FirstOrDefaultAsync(cancellationToken);

        if (exactMatch != null)
        {
            _logger.LogInformation(
                "找到精确匹配的模板（ID: {TemplateId}，名称: {TemplateName}）",
                exactMatch.Id,
                exactMatch.TemplateName);
            return exactMatch;
        }

        // 2. 模糊匹配：相似的表头（编辑距离）
        var templates = await _unitOfWork.DocumentTemplates
            .Query()
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.UsageCount)
            .Take(10) // 只取最常用的 10 个模板
            .ToListAsync(cancellationToken);

        foreach (var template in templates)
        {
            var templateHeaders = JsonSerializer.Deserialize<List<string>>(template.HeadersJson) ?? new List<string>();
            var similarity = CalculateHeadersSimilarity(headers, templateHeaders);

            if (similarity > 0.9) // 90% 相似度阈值
            {
                _logger.LogInformation(
                    "找到模糊匹配的模板（ID: {TemplateId}，名称: {TemplateName}，相似度: {Similarity:P0}）",
                    template.Id,
                    template.TemplateName,
                    similarity);
                return template;
            }
        }

        _logger.LogInformation("客户 {CustomerId} 没有找到匹配的模板", customerId);
        return null;
    }

    public async Task<IReadOnlyList<DocumentStructureReferenceCase>> FindReferenceCasesAsync(
        int customerId,
        IReadOnlyList<string> headers,
        int maxCount = 3,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0 || headers.Count == 0)
        {
            return [];
        }

        var templates = await _unitOfWork.DocumentTemplates
            .Query()
            .Where(t => t.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        return templates
            .Select(template => ToReferenceCase(template, headers))
            .Where(item => item != null)
            .Select(item => item!)
            .Where(item => item.Similarity > 0)
            .OrderByDescending(item => item.Similarity)
            .ThenByDescending(item => item.UsageCount)
            .ThenByDescending(item => item.UpdatedAt)
            .Take(maxCount)
            .ToList();
    }

    /// <summary>
    /// 保存模板
    /// </summary>
    public async Task<DocumentTemplate> SaveTemplateAsync(
        int customerId,
        string templateName,
        IReadOnlyList<string> headers,
        ColumnMapping columnMapping,
        int? dataEndRowIndex = null,
        bool isSpecificationOnly = false,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = GenerateHeadersFingerprint(headers);
        var headersJson = JsonSerializer.Serialize(headers);

        // 检查是否已存在相同指纹的模板
        var existing = await _unitOfWork.DocumentTemplates
            .Query(asNoTracking: false)
            .FirstOrDefaultAsync(t =>
                t.CustomerId == customerId &&
                t.HeadersFingerprint == fingerprint,
                cancellationToken);

        if (existing != null)
        {
            // 更新现有模板
            existing.TemplateName = templateName;
            existing.ProjectColumnIndex = columnMapping.ProjectColumn ?? -1;
            existing.SpecificationColumnIndex = columnMapping.SpecificationColumn ?? -1;
            existing.AcceptanceColumnIndex = columnMapping.AcceptanceColumn ?? -1;
            existing.RemarkColumnIndex = columnMapping.RemarkColumn;
            existing.HeaderRowIndex = columnMapping.HeaderRowIndex;
            existing.HeaderRowCount = columnMapping.HeaderRowCount;
            existing.DataStartRowIndex = columnMapping.DataStartRowIndex;
            existing.DataEndRowIndex = dataEndRowIndex;
            existing.IsSpecificationOnly = isSpecificationOnly;
            existing.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "更新模板（ID: {TemplateId}，客户: {CustomerId}）",
                existing.Id,
                customerId);

            return existing;
        }

        // 创建新模板
        var template = new DocumentTemplate
        {
            CustomerId = customerId,
            TemplateName = templateName,
            HeadersFingerprint = fingerprint,
            HeadersJson = headersJson,
            ProjectColumnIndex = columnMapping.ProjectColumn ?? -1,
            SpecificationColumnIndex = columnMapping.SpecificationColumn ?? -1,
            AcceptanceColumnIndex = columnMapping.AcceptanceColumn ?? -1,
            RemarkColumnIndex = columnMapping.RemarkColumn,
            HeaderRowIndex = columnMapping.HeaderRowIndex,
            HeaderRowCount = columnMapping.HeaderRowCount,
            DataStartRowIndex = columnMapping.DataStartRowIndex,
            DataEndRowIndex = dataEndRowIndex,
            IsSpecificationOnly = isSpecificationOnly,
            UsageCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.DocumentTemplates.AddAsync(template);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "创建新模板（ID: {TemplateId}，客户: {CustomerId}，名称: {TemplateName}）",
            template.Id,
            customerId,
            templateName);

        return template;
    }

    /// <summary>
    /// 增加模板使用次数
    /// </summary>
    public async Task IncrementUsageAsync(
        int templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await _unitOfWork.DocumentTemplates
            .GetByIdAsync(templateId);

        if (template == null)
        {
            _logger.LogWarning("模板 {TemplateId} 不存在", templateId);
            return;
        }

        template.UsageCount++;
        template.LastUsedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "模板 {TemplateId} 使用次数增加到 {UsageCount}",
            templateId,
            template.UsageCount);
    }

    /// <summary>
    /// 生成表头指纹（用于快速匹配）
    /// </summary>
    private string GenerateHeadersFingerprint(IReadOnlyList<string> headers)
    {
        // 标准化：去除空格、转小写
        var normalized = headers
            .Select(h => h.Trim().ToLowerInvariant())
            .Where(h => !string.IsNullOrEmpty(h));

        return string.Join("|", normalized);
    }

    /// <summary>
    /// 计算两个表头列表的相似度（基于编辑距离）
    /// </summary>
    private double CalculateHeadersSimilarity(
        IReadOnlyList<string> headers1,
        IReadOnlyList<string> headers2)
    {
        if (headers1.Count != headers2.Count)
        {
            return 0.0;
        }

        if (headers1.Count == 0)
        {
            return 1.0;
        }

        var totalSimilarity = 0.0;
        for (int i = 0; i < headers1.Count; i++)
        {
            var h1 = headers1[i].Trim().ToLowerInvariant();
            var h2 = headers2[i].Trim().ToLowerInvariant();

            if (h1 == h2)
            {
                totalSimilarity += 1.0;
            }
            else
            {
                var distance = CalculateLevenshteinDistance(h1, h2);
                var maxLength = Math.Max(h1.Length, h2.Length);
                var similarity = maxLength > 0 ? 1.0 - (double)distance / maxLength : 0.0;
                totalSimilarity += similarity;
            }
        }

        return totalSimilarity / headers1.Count;
    }

    private DocumentStructureReferenceCase? ToReferenceCase(
        DocumentTemplate template,
        IReadOnlyList<string> currentHeaders)
    {
        var templateHeaders = JsonSerializer.Deserialize<List<string>>(template.HeadersJson) ?? [];
        var similarity = CalculateHeadersSimilarity(currentHeaders, templateHeaders);
        if (similarity <= 0)
        {
            return null;
        }

        return new DocumentStructureReferenceCase
        {
            TemplateName = template.TemplateName,
            Headers = templateHeaders,
            UsageCount = template.UsageCount,
            UpdatedAt = template.UpdatedAt,
            Similarity = Math.Round(similarity, 2),
            Mapping = new DocumentStructureCandidate
            {
                TableIndex = 0,
                HeaderRowIndex = template.HeaderRowIndex,
                HeaderRowCount = template.HeaderRowCount,
                DataStartRowIndex = template.DataStartRowIndex,
                DataEndRowIndex = template.DataEndRowIndex,
                ProjectColumnIndex = template.IsSpecificationOnly || template.ProjectColumnIndex < 0
                    ? null
                    : template.ProjectColumnIndex,
                SpecificationColumnIndex = template.SpecificationColumnIndex < 0
                    ? null
                    : template.SpecificationColumnIndex,
                AcceptanceColumnIndex = template.AcceptanceColumnIndex < 0
                    ? null
                    : template.AcceptanceColumnIndex,
                RemarkColumnIndex = template.RemarkColumnIndex,
                IsSpecificationOnly = template.IsSpecificationOnly,
                Confidence = 1,
                Source = DocumentStructureCandidateSource.Template
            }
        };
    }

    /// <summary>
    /// 计算编辑距离
    /// </summary>
    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
        if (string.IsNullOrEmpty(s2)) return s1.Length;

        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
            d[i, 0] = i;

        for (var j = 0; j <= n; j++)
            d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}
