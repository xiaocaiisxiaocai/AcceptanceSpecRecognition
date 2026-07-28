using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
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
    /// 分页查询已经学习的客户级文档结构模板。
    /// </summary>
    public async Task<PagedResult<DocumentTemplateListItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        int? customerId,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedKeyword = keyword?.Trim();

        var query = _unitOfWork.DocumentTemplates.Query();
        if (customerId.HasValue)
        {
            query = query.Where(template => template.CustomerId == customerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            query = query.Where(template =>
                template.TemplateName.Contains(normalizedKeyword) ||
                template.Customer.Name.Contains(normalizedKeyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(template => template.UpdatedAt)
            .ThenByDescending(template => template.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(template => new DocumentTemplateListItemDto
            {
                Id = template.Id,
                CustomerId = template.CustomerId,
                CustomerName = template.Customer.Name,
                TemplateName = template.TemplateName,
                TableKind = template.TableKind,
                Recommendation = template.Recommendation,
                RegionCount = template.Regions.Count == 0 ? 1 : template.Regions.Count,
                UsageCount = template.UsageCount,
                UserModifiedStructure = template.UserModifiedStructure,
                ConfirmedAt = template.ConfirmedAt,
                LastUsedAt = template.LastUsedAt,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentTemplateListItemDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// 获取模板详情；没有区域子记录的旧模板按主记录兼容为一个区域。
    /// </summary>
    public async Task<DocumentTemplateDetailDto?> GetDetailAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var template = await _unitOfWork.DocumentTemplates.Query()
            .Include(item => item.Customer)
            .Include(item => item.Regions)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return template == null ? null : ToDetailDto(template);
    }

    /// <summary>
    /// 删除模板及其级联区域。
    /// </summary>
    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var template = await _unitOfWork.DocumentTemplates.GetByIdAsync(id, cancellationToken);
        if (template == null)
        {
            return false;
        }

        _unitOfWork.DocumentTemplates.Remove(template);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "删除文档结构模板（ID: {TemplateId}，客户: {CustomerId}）",
            template.Id,
            template.CustomerId);
        return true;
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
        return (await FindMatchingTemplatesAsync(customerId, headers, cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<DocumentTemplate>> FindMatchingTemplatesAsync(
        int customerId,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken = default)
    {
        // 生成表头指纹
        var fingerprint = GenerateHeadersFingerprint(headers);

        var templates = await _unitOfWork.DocumentTemplates
            .Query()
            .Include(t => t.Regions)
            .Where(t => t.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        // HeadersFingerprint 对新模板保存完整结构指纹，因此通过 HeadersJson 计算主表头指纹，
        // 既兼容旧库，也允许相同首段表头持久化多个区域变体。
        var readableTemplates = templates
            .Select(template => new
            {
                Template = template,
                Headers = TryReadHeaders(template.HeadersJson)
            })
            .Where(item => item.Headers != null && HasReadableRegionHeaders(item.Template))
            .Select(item => new { item.Template, Headers = item.Headers! })
            .ToList();

        var exactMatches = readableTemplates
            .Where(item => GenerateHeadersFingerprint(item.Headers) == fingerprint)
            .Select(item => item.Template)
            .OrderByDescending(template => template.UsageCount)
            .ThenByDescending(template => template.LastUsedAt)
            .ThenByDescending(template => template.UpdatedAt)
            .ToList();

        if (exactMatches.Count > 0)
        {
            _logger.LogInformation(
                "找到 {TemplateCount} 个精确匹配的模板候选",
                exactMatches.Count);
            return exactMatches;
        }

        // 2. 模糊匹配：相似的表头（编辑距离）
        var fuzzyCandidates = readableTemplates
            .OrderByDescending(t => t.Template.UsageCount)
            .Take(10) // 只取最常用的 10 个模板
            .ToList();

        var fuzzyMatches = fuzzyCandidates
            .Select(item => new
            {
                item.Template,
                Similarity = CalculateHeadersSimilarity(headers, item.Headers)
            })
            .Where(item => item.Similarity > 0.9)
            .OrderByDescending(item => item.Similarity)
            .ThenByDescending(item => item.Template.UsageCount)
            .Select(item => item.Template)
            .ToList();
        if (fuzzyMatches.Count > 0)
        {
            _logger.LogInformation("找到 {TemplateCount} 个模糊匹配的模板候选", fuzzyMatches.Count);
            return fuzzyMatches;
        }

        _logger.LogInformation("客户 {CustomerId} 没有找到匹配的模板", customerId);
        return [];
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
        CancellationToken cancellationToken = default,
        IReadOnlyList<DocumentTemplateRegionInput>? regions = null,
        bool operationLockAlreadyHeld = false)
    {
        var headersJson = JsonSerializer.Serialize(headers);
        var now = DateTime.UtcNow;
        var normalizedTableKind = NormalizeMetadata(tableKind, "Unknown");
        var normalizedRecommendation = NormalizeMetadata(recommendation, "NeedConfirm");
        var effectiveRegions = regions is { Count: > 0 }
            ? regions.OrderBy(region => region.RegionIndex).ToList()
            :
            [
                new DocumentTemplateRegionInput
                {
                    RegionIndex = 0,
                    Headers = headers,
                    HeaderRowIndex = columnMapping.HeaderRowIndex,
                    HeaderRowCount = columnMapping.HeaderRowCount,
                    DataStartRowIndex = columnMapping.DataStartRowIndex,
                    DataEndRowIndex = dataEndRowIndex,
                    ProjectColumnIndex = columnMapping.ProjectColumn,
                    SpecificationColumnIndex = columnMapping.SpecificationColumn ?? -1,
                    AcceptanceColumnIndex = columnMapping.AcceptanceColumn,
                    RemarkColumnIndex = columnMapping.RemarkColumn,
                    IsSpecificationOnly = isSpecificationOnly
                }
            ];
        var primaryFingerprint = GenerateHeadersFingerprint(headers);
        var structureFingerprint = GenerateStructureFingerprint(effectiveRegions);
        await using var templateLock = operationLockAlreadyHeld
            ? NoopTemplateLock.Instance
            : await _unitOfWork.AcquireOperationLockAsync(
                $"document-template:{customerId}",
                cancellationToken);

        // 当前稳定指纹走唯一索引快路径；仅在兼容旧指纹时扫描该客户历史模板。
        var existing = await _unitOfWork.DocumentTemplates
            .Query(asNoTracking: false)
            .Include(t => t.Regions)
            .SingleOrDefaultAsync(template =>
                template.CustomerId == customerId &&
                template.HeadersFingerprint == structureFingerprint,
                cancellationToken);

        if (existing == null)
        {
            // 完整结构相同才更新；相同首段表头但区域位置/列不同的模板必须并存。
            // 旧记录可能仍使用历史指纹，按价值与时间确定性选择保留者。
            var customerTemplates = await _unitOfWork.DocumentTemplates
                .Query(asNoTracking: false)
                .Include(t => t.Regions)
                .Where(t => t.CustomerId == customerId)
                .OrderByDescending(t => t.UsageCount)
                .ThenByDescending(t => t.LastUsedAt)
                .ThenByDescending(t => t.UpdatedAt)
                .ThenByDescending(t => t.Id)
                .ToListAsync(cancellationToken);
            existing = customerTemplates.FirstOrDefault(template =>
                GenerateHeadersFingerprint(ReadHeaders(template.HeadersJson)) == primaryFingerprint &&
                HasEquivalentRegions(template, effectiveRegions));
            existing ??= customerTemplates.FirstOrDefault(template =>
                GenerateHeadersFingerprint(ReadHeaders(template.HeadersJson)) == primaryFingerprint &&
                effectiveRegions.Count == 1 &&
                GetPersistedRegionCount(template) <= 1);
        }

        if (existing != null)
        {
            // 更新现有模板
            existing.TemplateName = templateName;
            existing.HeadersFingerprint = structureFingerprint;
            existing.HeadersJson = headersJson;
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
            existing.Regions.Clear();
            foreach (var region in effectiveRegions)
            {
                existing.Regions.Add(ToRegionEntity(region));
            }

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
            HeadersFingerprint = structureFingerprint,
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
            UpdatedAt = now,
            Regions = effectiveRegions.Select(ToRegionEntity).ToList()
        };

        await _unitOfWork.DocumentTemplates.AddAsync(template);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 查询与插入之间可能有另一应用实例保存了同一结构。数据库唯一键是
            // 最终裁决者；丢弃本地 Added 实体并返回已持久化的并发赢家。
            _unitOfWork.DocumentTemplates.Remove(template);
            var concurrentWinner = await _unitOfWork.DocumentTemplates
                .Query(asNoTracking: false)
                .Include(item => item.Regions)
                .FirstOrDefaultAsync(item =>
                    item.CustomerId == customerId &&
                    item.HeadersFingerprint == structureFingerprint,
                    cancellationToken);
            if (concurrentWinner is null)
                throw;

            return concurrentWinner;
        }

        _logger.LogInformation(
            "创建新模板（ID: {TemplateId}，客户: {CustomerId}，名称: {TemplateName}）",
            template.Id,
            customerId,
            templateName);

        return template;
    }

    private static DocumentTemplateRegion ToRegionEntity(DocumentTemplateRegionInput input)
    {
        return new DocumentTemplateRegion
        {
            RegionIndex = input.RegionIndex,
            HeadersJson = JsonSerializer.Serialize(input.Headers),
            HeaderRowIndex = input.HeaderRowIndex,
            HeaderRowCount = input.HeaderRowCount,
            DataStartRowIndex = input.DataStartRowIndex,
            DataEndRowIndex = input.DataEndRowIndex,
            ProjectColumnIndex = input.ProjectColumnIndex,
            SpecificationColumnIndex = input.SpecificationColumnIndex,
            AcceptanceColumnIndex = input.AcceptanceColumnIndex,
            RemarkColumnIndex = input.RemarkColumnIndex,
            IsSpecificationOnly = input.IsSpecificationOnly
        };
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
    private static string GenerateHeadersFingerprint(IReadOnlyList<string> headers)
    {
        // 保留空列位置，并用定长哈希避免宽表超过数据库 128 字符限制。
        var normalized = headers
            .Select(h => h.Trim().ToLowerInvariant())
            .ToList();

        return ComputeSha256(JsonSerializer.Serialize(normalized));
    }

    private static string GenerateStructureFingerprint(IReadOnlyList<DocumentTemplateRegionInput> regions)
    {
        var normalized = regions
            .OrderBy(region => region.RegionIndex)
            .Select(region => new
            {
                region.RegionIndex,
                Headers = region.Headers.Select(header => header.Trim().ToLowerInvariant()).ToArray(),
                region.HeaderRowIndex,
                region.HeaderRowCount,
                region.DataStartRowIndex,
                ProjectColumnIndex = region.IsSpecificationOnly ? null : region.ProjectColumnIndex,
                region.SpecificationColumnIndex,
                region.AcceptanceColumnIndex,
                region.RemarkColumnIndex,
                region.IsSpecificationOnly
            })
            .ToList();
        return ComputeSha256(JsonSerializer.Serialize(normalized));
    }

    private static bool HasEquivalentRegions(
        DocumentTemplate template,
        IReadOnlyList<DocumentTemplateRegionInput> regions)
    {
        var persisted = template.Regions.Count > 0
            ? template.Regions.OrderBy(region => region.RegionIndex).Select(region => new DocumentTemplateRegionInput
            {
                RegionIndex = region.RegionIndex,
                Headers = ReadHeaders(region.HeadersJson),
                HeaderRowIndex = region.HeaderRowIndex,
                HeaderRowCount = region.HeaderRowCount,
                DataStartRowIndex = region.DataStartRowIndex,
                DataEndRowIndex = region.DataEndRowIndex,
                ProjectColumnIndex = NormalizeColumn(region.ProjectColumnIndex),
                SpecificationColumnIndex = NormalizeColumn(region.SpecificationColumnIndex) ?? -1,
                AcceptanceColumnIndex = NormalizeColumn(region.AcceptanceColumnIndex),
                RemarkColumnIndex = NormalizeColumn(region.RemarkColumnIndex),
                IsSpecificationOnly = region.IsSpecificationOnly
            }).ToList()
            :
            [
                new DocumentTemplateRegionInput
                {
                    RegionIndex = 0,
                    Headers = ReadHeaders(template.HeadersJson),
                    HeaderRowIndex = template.HeaderRowIndex,
                    HeaderRowCount = template.HeaderRowCount,
                    DataStartRowIndex = template.DataStartRowIndex,
                    DataEndRowIndex = template.DataEndRowIndex,
                    ProjectColumnIndex = NormalizeColumn(template.ProjectColumnIndex),
                    SpecificationColumnIndex = NormalizeColumn(template.SpecificationColumnIndex) ?? -1,
                    AcceptanceColumnIndex = NormalizeColumn(template.AcceptanceColumnIndex),
                    RemarkColumnIndex = NormalizeColumn(template.RemarkColumnIndex),
                    IsSpecificationOnly = template.IsSpecificationOnly
                }
            ];

        return string.Equals(
            GenerateStructureFingerprint(persisted),
            GenerateStructureFingerprint(regions),
            StringComparison.Ordinal);
    }

    private static List<string> ReadHeaders(string headersJson) => TryReadHeaders(headersJson) ?? [];

    private static List<string>? TryReadHeaders(string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
        {
            return null;
        }

        try
        {
            var headers = JsonSerializer.Deserialize<List<string>>(headersJson);
            return headers?.Any(header => header == null) == true ? null : headers;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasReadableRegionHeaders(DocumentTemplate template) =>
        template.Regions.All(region => TryReadHeaders(region.HeadersJson) != null);

    private static int GetPersistedRegionCount(DocumentTemplate template) =>
        template.Regions.Count > 0 ? template.Regions.Count : 1;

    private static int? NormalizeColumn(int? columnIndex) =>
        columnIndex.HasValue && columnIndex.Value >= 0 ? columnIndex.Value : null;

    private static DocumentTemplateDetailDto ToDetailDto(DocumentTemplate template)
    {
        var regions = template.Regions.Count > 0
            ? template.Regions
                .OrderBy(region => region.RegionIndex)
                .Select(region => new DocumentTemplateRegionDto
                {
                    RegionIndex = region.RegionIndex,
                    Headers = ReadHeaders(region.HeadersJson),
                    HeaderRowIndex = region.HeaderRowIndex,
                    HeaderRowCount = region.HeaderRowCount,
                    DataStartRowIndex = region.DataStartRowIndex,
                    DataEndRowIndex = region.DataEndRowIndex,
                    ProjectColumnIndex = NormalizeColumn(region.ProjectColumnIndex),
                    SpecificationColumnIndex = NormalizeColumn(region.SpecificationColumnIndex),
                    AcceptanceColumnIndex = NormalizeColumn(region.AcceptanceColumnIndex),
                    RemarkColumnIndex = NormalizeColumn(region.RemarkColumnIndex),
                    IsSpecificationOnly = region.IsSpecificationOnly
                })
                .ToList()
            :
            [
                new DocumentTemplateRegionDto
                {
                    RegionIndex = 0,
                    Headers = ReadHeaders(template.HeadersJson),
                    HeaderRowIndex = template.HeaderRowIndex,
                    HeaderRowCount = template.HeaderRowCount,
                    DataStartRowIndex = template.DataStartRowIndex,
                    DataEndRowIndex = template.DataEndRowIndex,
                    ProjectColumnIndex = NormalizeColumn(template.ProjectColumnIndex),
                    SpecificationColumnIndex = NormalizeColumn(template.SpecificationColumnIndex),
                    AcceptanceColumnIndex = NormalizeColumn(template.AcceptanceColumnIndex),
                    RemarkColumnIndex = NormalizeColumn(template.RemarkColumnIndex),
                    IsSpecificationOnly = template.IsSpecificationOnly
                }
            ];

        return new DocumentTemplateDetailDto
        {
            Id = template.Id,
            CustomerId = template.CustomerId,
            CustomerName = template.Customer.Name,
            TemplateName = template.TemplateName,
            TableKind = template.TableKind,
            Recommendation = template.Recommendation,
            UsageCount = template.UsageCount,
            UserModifiedStructure = template.UserModifiedStructure,
            ConfirmedAt = template.ConfirmedAt,
            LastUsedAt = template.LastUsedAt,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            Regions = regions
        };
    }

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeMetadata(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized.Length > 50
                ? normalized[..50]
                : normalized;
    }

    private sealed class NoopTemplateLock : IAsyncDisposable
    {
        public static NoopTemplateLock Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
        var templateHeaders = TryReadHeaders(template.HeadersJson);
        if (templateHeaders == null)
        {
            return null;
        }
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
                RemarkColumnIndex = template.RemarkColumnIndex < 0
                    ? null
                    : template.RemarkColumnIndex,
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

public sealed class DocumentTemplateRegionInput
{
    public int RegionIndex { get; init; }
    public IReadOnlyList<string> Headers { get; init; } = [];
    public int HeaderRowIndex { get; init; }
    public int HeaderRowCount { get; init; } = 1;
    public int DataStartRowIndex { get; init; } = 1;
    public int? DataEndRowIndex { get; init; }
    public int? ProjectColumnIndex { get; init; }
    public int SpecificationColumnIndex { get; init; }
    public int? AcceptanceColumnIndex { get; init; }
    public int? RemarkColumnIndex { get; init; }
    public bool IsSpecificationOnly { get; init; }
}
