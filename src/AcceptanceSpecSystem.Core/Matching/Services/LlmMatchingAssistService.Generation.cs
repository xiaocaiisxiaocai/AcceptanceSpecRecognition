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
        CancellationToken cancellationToken)
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
                var settings = CreatePromptExecutionSettings(cfg);

                var message = await chat.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken);
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

                // 服务已成功返回内容，说明端点和模型可用；这里只是本次输出未满足
                // 场景 JSON 契约，不能污染运行时可用性缓存。
                _runtimeStatusReporter.ReportAvailableIfCurrent(
                    cfg.Id,
                    AiServicePurpose.Llm,
                    readinessGeneration);
                errors.Add($"{cfg.Name}: invalid_payload");
                _logger.LogWarning("LLM 输出未通过解析校验，尝试下一个服务: {Name}; 输出摘要: {Summary}",
                    cfg.Name,
                    SensitiveLogFormatter.DescribePayload(raw));
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
