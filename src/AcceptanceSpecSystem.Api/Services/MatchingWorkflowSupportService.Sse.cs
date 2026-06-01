using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private static async Task WriteSseEventAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 安全写入 SSE 事件：连接已断开时静默忽略，不抛异常
    /// </summary>
    private static async Task WriteSseEventSafeAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        try
        {
            await WriteSseEventAsync(response, eventName, data, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // 让调用方的 catch(OperationCanceledException) 处理
        }
        catch (ObjectDisposedException)
        {
            // Response 已释放，连接已断开
        }
    }

    /// <summary>
    /// 线程安全的 SSE 写入：用信号量串行化并发写入（Parallel.ForEachAsync 场景）
    /// </summary>
    private static async Task WriteSseEventLockedAsync(
        HttpResponse response,
        SemaphoreSlim sseWriteLock,
        string eventName,
        object data,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        await sseWriteLock.WaitAsync(cancellationToken);
        try
        {
            await WriteSseEventAsync(response, eventName, data, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException) { /* Response 已释放 */ }
        finally
        {
            sseWriteLock.Release();
        }
    }
}
