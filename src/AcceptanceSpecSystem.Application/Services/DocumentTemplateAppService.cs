using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
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
        CancellationToken cancellationToken = default,
        string? tableName = null)
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
            .Select(template => ToReferenceCase(template, headers, tableName))
            .Where(item => item != null)
            .Select(item => item!)
            .Where(item => item.Similarity > 0)
            .OrderByDescending(item => item.Similarity)
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
        string? tableKind = null,
        string? recommendation = null,
        bool userModifiedStructure = false,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = GenerateHeadersFingerprint(headers);
        var headersJson = JsonSerializer.Serialize(headers);
        var now = DateTime.UtcNow;
        var normalizedTableKind = NormalizeMetadata(tableKind, "Unknown");
        var normalizedRecommendation = NormalizeMetadata(recommendation, "NeedConfirm");

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
            existing.TableKind = normalizedTableKind;
            existing.Recommendation = normalizedRecommendation;
            existing.ConfirmedAt = now;
            existing.UserModifiedStructure = userModifiedStructure;
            existing.UpdatedAt = now;

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
            TableKind = normalizedTableKind,
            Recommendation = normalizedRecommendation,
            ConfirmedAt = now,
            UserModifiedStructure = userModifiedStructure,
            UsageCount = 0,
            CreatedAt = now,
            UpdatedAt = now
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

    private static string NormalizeMetadata(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized.Length > 50
                ? normalized[..50]
                : normalized;
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
            var rawHeader1 = headers1[i] ?? string.Empty;
            var rawHeader2 = headers2[i] ?? string.Empty;
            if (rawHeader1.Length > ColumnHeaderRuleMatcher.MaxHeaderInputLength ||
                rawHeader2.Length > ColumnHeaderRuleMatcher.MaxHeaderInputLength)
            {
                totalSimilarity += string.Equals(rawHeader1, rawHeader2, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                continue;
            }

            var h1 = rawHeader1.Trim().ToLowerInvariant();
            var h2 = rawHeader2.Trim().ToLowerInvariant();

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
        IReadOnlyList<string> currentHeaders,
        string? currentTableName)
    {
        var templateHeaders = JsonSerializer.Deserialize<List<string>>(template.HeadersJson) ?? [];
        var headerSimilarity = CalculateHeadersSimilarity(currentHeaders, templateHeaders);
        var similarity = CalculateCaseWeight(template, currentTableName, headerSimilarity);
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

    private double CalculateCaseWeight(
        DocumentTemplate template,
        string? currentTableName,
        double headerSimilarity)
    {
        if (string.IsNullOrWhiteSpace(currentTableName))
        {
            return headerSimilarity;
        }

        if (headerSimilarity <= 0)
        {
            return 0;
        }

        var tableNameSimilarity = CalculateTextSimilarity(currentTableName, template.TemplateName);
        var usageScore = Math.Clamp(Math.Log10(template.UsageCount + 1) / 2, 0, 1);
        var recencyScore = CalculateRecencyScore(template.ConfirmedAt ?? template.UpdatedAt);
        var correctionPenalty = template.UserModifiedStructure ? 0.1 : 0;

        return Math.Clamp(
            headerSimilarity * 0.85 +
            tableNameSimilarity * 0.03 +
            recencyScore * 0.08 +
            usageScore * 0.04 -
            correctionPenalty,
            0,
            1);
    }

    private static double CalculateRecencyScore(DateTime timestamp)
    {
        var ageDays = Math.Max(0, (DateTime.UtcNow - timestamp).TotalDays);
        return Math.Clamp(1 - ageDays / 180, 0, 1);
    }

    private double CalculateTextSimilarity(string? left, string? right)
    {
        if (left?.Length > ColumnHeaderRuleMatcher.MaxHeaderInputLength ||
            right?.Length > ColumnHeaderRuleMatcher.MaxHeaderInputLength)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        var l = left?.Trim().ToLowerInvariant() ?? string.Empty;
        var r = right?.Trim().ToLowerInvariant() ?? string.Empty;
        if (l.Length == 0 || r.Length == 0)
        {
            return 0;
        }

        if (l == r || l.Contains(r) || r.Contains(l))
        {
            return 1;
        }

        var distance = CalculateLevenshteinDistance(l, r);
        var maxLength = Math.Max(l.Length, r.Length);
        return maxLength == 0 ? 0 : Math.Clamp(1.0 - (double)distance / maxLength, 0, 1);
    }

    /// <summary>
    /// 计算编辑距离
    /// </summary>
    private static int CalculateLevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
        if (string.IsNullOrEmpty(s2)) return s1.Length;

        if (s1.Length > s2.Length)
        {
            (s1, s2) = (s2, s1);
        }

        var previous = new int[s1.Length + 1];
        var current = new int[s1.Length + 1];
        for (var index = 0; index <= s1.Length; index++)
        {
            previous[index] = index;
        }

        for (var rightIndex = 1; rightIndex <= s2.Length; rightIndex++)
        {
            current[0] = rightIndex;
            for (var leftIndex = 1; leftIndex <= s1.Length; leftIndex++)
            {
                var cost = s1[leftIndex - 1] == s2[rightIndex - 1] ? 0 : 1;
                current[leftIndex] = Math.Min(
                    Math.Min(previous[leftIndex] + 1, current[leftIndex - 1] + 1),
                    previous[leftIndex - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[s1.Length];
    }
}
