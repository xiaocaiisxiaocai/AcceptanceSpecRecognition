using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class LlmMatchingAssistService
{
    private static OpenAIPromptExecutionSettings CreatePromptExecutionSettings(AiServiceConfigModel config)
    {
        // temperature=0 贪心解码 + 固定 seed，最大化裁决结果可复现性。
        // 注意：Ollama 原生路径仅在请求体显式带上 options.temperature/seed 时才生效。
        return new OpenAIPromptExecutionSettings
        {
            Temperature = 0,
            Seed = 42
        };
    }

    // ── 模板管理 ──

    /// <summary>
    /// 获取或创建 Prompt 模板；如果 DB 中存储的是旧版系统模板内容则自动升级
    /// </summary>
    private async Task<PromptTemplateModel> GetOrCreateTemplateAsync(
        SystemPromptTemplateDefinition definition,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetTemplateCacheKey(definition);
        if (_promptTemplateCache.TryGetValue(cacheKey, out var cachedTemplate))
        {
            return cachedTemplate;
        }

        await _dbBackedCacheInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_promptTemplateCache.TryGetValue(cacheKey, out cachedTemplate))
            {
                return cachedTemplate;
            }

            return await GetOrCreateTemplateCoreAsync(definition, cacheKey, cancellationToken);
        }
        finally
        {
            _dbBackedCacheInitializationLock.Release();
        }
    }

    private async Task<PromptTemplateModel> GetOrCreateTemplateCoreAsync(
        SystemPromptTemplateDefinition definition,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        await _promptTemplateCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_promptTemplateCache.TryGetValue(cacheKey, out var cachedTemplate))
            {
                return cachedTemplate;
            }

            var loadedTemplate = await LoadTemplateAsync(definition, cancellationToken);
            _promptTemplateCache[cacheKey] = loadedTemplate;
            return loadedTemplate;
        }
        finally
        {
            _promptTemplateCacheLock.Release();
        }
    }

    private async Task<PromptTemplateModel> LoadTemplateAsync(
        SystemPromptTemplateDefinition definition,
        CancellationToken cancellationToken)
    {
        var template = await _promptTemplateProvider.GetOrCreateSystemAsync(
            definition.Scene,
            definition.Name,
            definition.DisplayName,
            definition.DefaultContent,
            cancellationToken);

        var content = template.Content;
        var changed = false;

        // 与 SystemPromptTemplateInitializer 保持一致：历任旧版默认内容（LegacyDefaultContent
        // 与 AdditionalLegacyContents）都视为可自动升级，避免两条升级链行为不一致。
        var isLegacyContent =
            (definition.LegacyDefaultContent != null &&
             string.Equals(content.Trim(), definition.LegacyDefaultContent.Trim(), StringComparison.Ordinal)) ||
            (definition.AdditionalLegacyContents != null &&
             definition.AdditionalLegacyContents.Any(legacy =>
                 string.Equals(content.Trim(), legacy.Trim(), StringComparison.Ordinal)));

        if (isLegacyContent)
        {
            _logger.LogInformation("自动升级 LLM Prompt 模板 [{Name}]：检测到旧版默认内容，更新为新版", definition.Name);
            content = definition.DefaultContent;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = definition.DefaultContent;
            changed = true;
        }

        if (changed)
        {
            await _promptTemplateProvider.SaveContentAsync(template.Id, content, cancellationToken);
            template.Content = content;
        }

        _logger.LogInformation("确保系统 LLM Prompt 模板可用: {Name}", definition.Name);
        return template;
    }

    private async Task<IReadOnlyList<AiServiceConfigModel>> GetCachedCandidatesAsync(
        AiServicePurpose purpose,
        int? preferredId,
        CancellationToken cancellationToken)
    {
        if (!_aiServiceCandidateCache.TryGetValue(purpose, out var cachedCandidates))
        {
            await _dbBackedCacheInitializationLock.WaitAsync(cancellationToken);
            try
            {
                if (!_aiServiceCandidateCache.TryGetValue(purpose, out cachedCandidates))
                {
                    cachedCandidates = await GetCachedCandidatesCoreAsync(purpose, cancellationToken);
                }
            }
            finally
            {
                _dbBackedCacheInitializationLock.Release();
            }
        }

        return FilterCandidatesForPreferredService(cachedCandidates, preferredId);
    }

    private async Task<IReadOnlyList<AiServiceConfigModel>> GetCachedCandidatesCoreAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken)
    {
        await _aiServiceCandidateCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!_aiServiceCandidateCache.TryGetValue(purpose, out var cachedCandidates))
            {
                cachedCandidates = await _selector.GetCandidatesAsync(purpose, preferredId: null, cancellationToken);
                _aiServiceCandidateCache[purpose] = cachedCandidates;
            }

            return cachedCandidates;
        }
        finally
        {
            _aiServiceCandidateCacheLock.Release();
        }
    }

    private static IReadOnlyList<AiServiceConfigModel> FilterCandidatesForPreferredService(
        IReadOnlyList<AiServiceConfigModel> candidates,
        int? preferredId)
    {
        if (!preferredId.HasValue)
        {
            return candidates;
        }

        var preferred = candidates.FirstOrDefault(candidate => candidate.Id == preferredId.Value);
        if (preferred == null)
        {
            return [];
        }

        // 用户显式选择的 LLM 只使用该服务，避免高显存模型作为自动兜底触发 Ollama 驱逐。
        return [preferred];
    }

    private static string GetTemplateCacheKey(SystemPromptTemplateDefinition definition)
    {
        return $"{(int)definition.Scene}:{definition.Name}";
    }

    // ── 工具方法 ──

    private static string ApplyTemplate(string template, Dictionary<string, string> values)
    {
        return PromptTemplatePlaceholderRenderer.ReplacePlaceholders(template, values);
    }

}
