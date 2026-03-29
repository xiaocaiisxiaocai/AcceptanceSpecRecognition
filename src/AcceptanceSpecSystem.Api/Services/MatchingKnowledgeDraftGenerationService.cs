using System.Text;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class MatchingKnowledgeDraftGenerationService
{
    public const string CategoryEntityAliases = "entityAliases";
    public const string CategoryUnitAliases = "unitAliases";
    public const string CategoryFieldAliases = "fieldAliases";
    public const string CategoryConflictPairs = "conflictPairs";

    public const string SourceTypeText = "text";
    public const string SourceTypeDocuments = "documents";

    private readonly IUnitOfWork _unitOfWork;
    private readonly MatchingKnowledgeBootstrapper _bootstrapper;
    private readonly MatchingKnowledgeOptions _defaultOptions;
    private readonly IMatchingKnowledgeDraftAiService _draftAiService;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuthDataScopeService _authDataScopeService;

    public MatchingKnowledgeDraftGenerationService(
        IUnitOfWork unitOfWork,
        MatchingKnowledgeBootstrapper bootstrapper,
        IOptions<MatchingKnowledgeOptions> defaultOptions,
        IMatchingKnowledgeDraftAiService draftAiService,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        IAuthDataScopeService authDataScopeService)
    {
        _unitOfWork = unitOfWork;
        _bootstrapper = bootstrapper;
        _defaultOptions = defaultOptions.Value ?? new MatchingKnowledgeOptions();
        _draftAiService = draftAiService;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _authDataScopeService = authDataScopeService;
    }

    public async Task<MatchingKnowledgeDraftResponseDto> GenerateAsync(
        ClaimsPrincipal user,
        GenerateMatchingKnowledgeDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = NormalizeCategory(request.Category);
        if (category == null)
        {
            throw new ArgumentException("不支持的匹配知识分类");
        }

        var scope = await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
        if (scope == null)
        {
            throw new UnauthorizedAccessException("会话缺少用户上下文");
        }

        var sourceText = await BuildSourceTextAsync(request, scope, cancellationToken);

        await _bootstrapper.EnsureInitializedAsync();
        var entity = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        var effective = MatchingKnowledgeComposition.BuildView(entity, _defaultOptions).Effective;

        var aiItems = await _draftAiService.GenerateAsync(new MatchingKnowledgeDraftAiRequest
        {
            Category = category,
            SourceText = sourceText,
            LlmServiceId = request.LlmServiceId
        }, cancellationToken);

        return new MatchingKnowledgeDraftResponseDto
        {
            Category = category,
            Items = category == CategoryConflictPairs
                ? MarkConflictPairDrafts(aiItems, effective)
                : MarkMappingDrafts(category, aiItems, effective)
        };
    }

    private async Task<string> BuildSourceTextAsync(
        GenerateMatchingKnowledgeDraftRequest request,
        DataScopeResult scope,
        CancellationToken cancellationToken)
    {
        var sourceType = request.SourceType?.Trim();
        if (string.Equals(sourceType, SourceTypeText, StringComparison.OrdinalIgnoreCase))
        {
            var text = request.InputText?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("请输入用于生成草稿的文本");
            }

            return text;
        }

        if (!string.Equals(sourceType, SourceTypeDocuments, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("不支持的输入来源");
        }

        var fileIds = (request.FileIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (fileIds.Count == 0)
        {
            throw new ArgumentException("请选择至少一个已上传文档");
        }

        var builder = new StringBuilder();
        foreach (var fileId in fileIds)
        {
            var file = await GetAccessibleWordFileAsync(fileId, scope, cancellationToken);
            if (file == null)
            {
                continue;
            }

            var extracted = await ExtractDocumentTextAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(extracted))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("---");
            }

            builder.AppendLine($"文件：{file.FileName}");
            builder.AppendLine(extracted.Trim());
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("未能从所选文档中提取可用文本");
        }

        return builder.ToString();
    }

    private async Task<WordFile?> GetAccessibleWordFileAsync(
        int id,
        DataScopeResult scope,
        CancellationToken cancellationToken)
    {
        var wordFile = await _unitOfWork.WordFiles.Query()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (wordFile == null)
        {
            return null;
        }

        if (WordFileDataScopeHelper.CanAccess(wordFile, scope))
        {
            return wordFile;
        }

        var hasScopedSpec = await SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(),
                scope)
            .AnyAsync(spec => spec.WordFileId == id, cancellationToken);

        return hasScopedSpec ? wordFile : null;
    }

    private async Task<string> ExtractDocumentTextAsync(WordFile wordFile, CancellationToken cancellationToken)
    {
        var parser = wordFile.FileType == UploadedFileType.ExcelXlsx
            ? _documentServiceFactory.GetParser(DocumentType.Excel)
            : _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return string.Empty;
        }

        using var stream = OpenWordFileReadStream(wordFile);
        var tables = await parser.GetTablesAsync(stream);
        if (tables.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var table in tables.Take(10))
        {
            if (!string.IsNullOrWhiteSpace(table.Name))
            {
                builder.AppendLine($"表名：{table.Name}");
            }

            if (table.Headers is { Count: > 0 })
            {
                builder.AppendLine($"表头：{string.Join(" | ", table.Headers.Where(header => !string.IsNullOrWhiteSpace(header)))}");
            }

            if (!string.IsNullOrWhiteSpace(table.PreviewText))
            {
                builder.AppendLine(table.PreviewText.Trim());
            }
        }

        return builder.ToString().Trim();
    }

    private Stream OpenWordFileReadStream(WordFile wordFile)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            if (File.Exists(fullPath))
            {
                return File.OpenRead(fullPath);
            }
        }

        if (wordFile.FileContent is { Length: > 0 })
        {
            return new MemoryStream(wordFile.FileContent);
        }

        throw new InvalidOperationException("文件内容不可用（未找到物理文件且数据库内容为空）");
    }

    private static string? NormalizeCategory(string? category)
    {
        return category?.Trim() switch
        {
            CategoryEntityAliases => CategoryEntityAliases,
            CategoryUnitAliases => CategoryUnitAliases,
            CategoryFieldAliases => CategoryFieldAliases,
            CategoryConflictPairs => CategoryConflictPairs,
            _ => null
        };
    }

    private static List<MatchingKnowledgeDraftItemDto> MarkMappingDrafts(
        string category,
        IReadOnlyList<MatchingKnowledgeDraftCandidate> aiItems,
        MatchingKnowledgeLayerDto effective)
    {
        var existing = category switch
        {
            CategoryEntityAliases => effective.EntityAliases,
            CategoryUnitAliases => effective.UnitAliases,
            CategoryFieldAliases => effective.FieldAliases,
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = new List<MatchingKnowledgeDraftItemDto>();
        var seenReady = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in aiItems)
        {
            var key = candidate.Key.Trim();
            var value = candidate.Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string status;
            string? statusMessage = null;

            if (existing.TryGetValue(key, out var existingValue))
            {
                if (string.Equals(existingValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    status = "duplicate";
                    statusMessage = "与当前生效规则重复，导入时会自动忽略";
                }
                else
                {
                    status = "conflict";
                    statusMessage = $"当前生效值为“{existingValue}”，需人工确认";
                }
            }
            else if (seenReady.TryGetValue(key, out var seenValue))
            {
                if (string.Equals(seenValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    status = "duplicate";
                    statusMessage = "与本次草稿中的其他候选重复";
                }
                else
                {
                    status = "conflict";
                    statusMessage = $"本次草稿中已存在“{key} -> {seenValue}”";
                }
            }
            else
            {
                status = "ready";
                seenReady[key] = value;
            }

            result.Add(new MatchingKnowledgeDraftItemDto
            {
                Key = key,
                Value = value,
                EvidenceSnippet = candidate.EvidenceSnippet,
                Reason = candidate.Reason,
                Status = status,
                StatusMessage = statusMessage
            });
        }

        return result;
    }

    private static List<MatchingKnowledgeDraftItemDto> MarkConflictPairDrafts(
        IReadOnlyList<MatchingKnowledgeDraftCandidate> aiItems,
        MatchingKnowledgeLayerDto effective)
    {
        var existingKeys = effective.ConflictPairs
            .Select(pair => BuildConflictKey(pair.Left, pair.Right))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<MatchingKnowledgeDraftItemDto>();
        foreach (var candidate in aiItems)
        {
            var left = candidate.Key.Trim();
            var right = candidate.Value.Trim();
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            var pairKey = BuildConflictKey(left, right);
            var status = "ready";
            string? statusMessage = null;

            if (existingKeys.Contains(pairKey))
            {
                status = "duplicate";
                statusMessage = "与当前生效冲突词对重复，导入时会自动忽略";
            }
            else if (!seen.Add(pairKey))
            {
                status = "duplicate";
                statusMessage = "与本次草稿中的其他候选重复";
            }

            result.Add(new MatchingKnowledgeDraftItemDto
            {
                Key = left,
                Value = right,
                EvidenceSnippet = candidate.EvidenceSnippet,
                Reason = candidate.Reason,
                Status = status,
                StatusMessage = statusMessage
            });
        }

        return result;
    }

    private static string BuildConflictKey(string left, string right)
    {
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{left.Trim()}__{right.Trim()}"
            : $"{right.Trim()}__{left.Trim()}";
    }
}
