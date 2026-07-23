using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private static async Task WriteSseEventAsync(IMatchingEventStream response, string eventName, object data, CancellationToken cancellationToken)
    {
        await response.WriteEventAsync(eventName, data, cancellationToken);
    }

    /// <summary>
    /// 安全写入 SSE 事件：连接已断开时静默忽略，不抛异常
    /// </summary>
    private static async Task WriteSseEventSafeAsync(IMatchingEventStream response, string eventName, object data, CancellationToken cancellationToken)
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
        IMatchingEventStream response,
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
