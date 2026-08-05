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
    private async Task<string?> GenerateWithFallbackAsync(
        string prompt,
        int? serviceId,
        string errorMessage,
        Func<LlmMatchingAssistService, string, bool> isAcceptablePayload,
        CancellationToken cancellationToken,
        Type? responseFormat = null)
    {
        var candidates = await GetCachedCandidatesAsync(AiServicePurpose.Llm, serviceId, cancellationToken);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException(errorMessage);

        var errors = new List<string>();
        var sawRawResponse = false;
        foreach (var cfg in candidates)
        {
            var readinessGeneration = _runtimeStatusReporter.CaptureGeneration(cfg.Id);
            try
            {
                _logger.LogDebug("调用 LLM 服务: {Name} ({Model})", cfg.Name, cfg.LlmModel);
                var chat = _factory.CreateChatCompletionService(cfg);
                var history = new ChatHistory();
                history.AddUserMessage(prompt);
                var settings = CreatePromptExecutionSettings(cfg, responseFormat);
                var maxAttempts = responseFormat == null ? 1 : 2;

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var message = await chat.GetChatMessageContentAsync(
                        history,
                        settings,
                        cancellationToken: cancellationToken);
                    var raw = SanitizeLlmOutput(message.Content);
                    sawRawResponse = true;

                    if (isAcceptablePayload(this, raw))
                    {
                        _runtimeStatusReporter.ReportAvailableIfCurrent(
                            cfg.Id,
                            AiServicePurpose.Llm,
                            readinessGeneration);
                        return raw;
                    }

                    _runtimeStatusReporter.ReportAvailableIfCurrent(
                        cfg.Id,
                        AiServicePurpose.Llm,
                        readinessGeneration);
                    errors.Add($"{cfg.Name}: invalid_payload");
                    _logger.LogWarning(
                        "LLM 输出未通过解析校验: {Name}, attempt={Attempt}/{MaxAttempts}; 输出摘要: {Summary}",
                        cfg.Name,
                        attempt,
                        maxAttempts,
                        SensitiveLogFormatter.DescribePayload(raw));
                    if (attempt < maxAttempts)
                    {
                        history.AddAssistantMessage(raw);
                        history.AddUserMessage(
                            "上一次输出未通过结构化校验。请修正格式，只返回符合既定 JSON Schema 的 JSON 对象，不要附加解释或 Markdown。");
                    }
                }

                if (responseFormat == null)
                {
                    // 非结构化旧场景保持原有候选服务回退语义。
                    _runtimeStatusReporter.ReportAvailableIfCurrent(
                        cfg.Id,
                        AiServicePurpose.Llm,
                        readinessGeneration);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _runtimeStatusReporter.ReportUnavailableIfCurrent(
                    cfg.Id,
                    AiServicePurpose.Llm,
                    readinessGeneration);
                errors.Add($"{cfg.Name}: {ex.GetType().Name}");
                _logger.LogWarning(
                    "LLM 调用失败: {Name}, exceptionType={ExceptionType}, traceId={TraceId}",
                    cfg.Name,
                    ex.GetType().Name,
                    Activity.Current?.TraceId.ToString());
            }
        }

        if (sawRawResponse)
        {
            if (responseFormat != null)
                throw new AiStructuredOutputException($"{errorMessage}：结构化输出无效");
            return null;
        }

        throw new AiServiceUnavailableException(errorMessage, errors);
    }

    private async IAsyncEnumerable<string> GenerateStreamWithFallbackAsync(
        string prompt,
        int? serviceId,
        string errorMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = await GetCachedCandidatesAsync(AiServicePurpose.Llm, serviceId, cancellationToken);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException(errorMessage);

        var errors = new List<string>();
        foreach (var cfg in candidates)
        {
            var readinessGeneration = _runtimeStatusReporter.CaptureGeneration(cfg.Id);
            _logger.LogDebug("流式调用 LLM 服务: {Name} ({Model})", cfg.Name, cfg.LlmModel);
            var produced = false;
            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

            _ = Task.Run(async () =>
            {
                try
                {
                    var chat = _factory.CreateChatCompletionService(cfg);
                    var history = new ChatHistory();
                    history.AddUserMessage(prompt);
                    var settings = CreatePromptExecutionSettings(cfg);
                    var thinkFilter = new ThinkContentFilter();

                    await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken))
                    {
                        if (!string.IsNullOrWhiteSpace(chunk.Content))
                        {
                            var sanitized = thinkFilter.Push(chunk.Content);
                            if (!string.IsNullOrWhiteSpace(sanitized))
                            {
                                await channel.Writer.WriteAsync(sanitized, cancellationToken);
                            }
                        }
                    }

                    var tail = thinkFilter.Flush();
                    if (!string.IsNullOrWhiteSpace(tail))
                    {
                        await channel.Writer.WriteAsync(tail, cancellationToken);
                    }

                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, cancellationToken);

            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    if (!produced)
                        _runtimeStatusReporter.ReportAvailableIfCurrent(
                            cfg.Id,
                            AiServicePurpose.Llm,
                            readinessGeneration);
                    produced = true;
                    yield return item;
                }
            }

            try
            {
                await channel.Reader.Completion;
                if (produced)
                {
                    yield break;
                }

                errors.Add($"{cfg.Name}: empty_stream");
                // 空流属于本次生成结果不可采用，不代表服务端点不可访问。
                _runtimeStatusReporter.ReportAvailableIfCurrent(
                    cfg.Id,
                    AiServicePurpose.Llm,
                    readinessGeneration);
                _logger.LogWarning("LLM 流式调用返回空流，尝试下一个服务: {Name}", cfg.Name);
            }
            catch (Exception ex)
            {
                _runtimeStatusReporter.ReportUnavailableIfCurrent(
                    cfg.Id,
                    AiServicePurpose.Llm,
                    readinessGeneration);
                errors.Add($"{cfg.Name}: {ex.GetType().Name}");
                _logger.LogWarning(
                    "LLM 流式调用失败: {Name}, exceptionType={ExceptionType}, traceId={TraceId}",
                    cfg.Name,
                    ex.GetType().Name,
                    Activity.Current?.TraceId.ToString());
                if (produced)
                    throw new AiServiceUnavailableException(errorMessage, errors, ex);
            }
        }

        throw new AiServiceUnavailableException(errorMessage, errors);
    }

}
