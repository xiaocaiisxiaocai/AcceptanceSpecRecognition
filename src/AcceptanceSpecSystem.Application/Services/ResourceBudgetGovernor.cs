using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Application.Services;

public enum ResourceWorkload
{
    DocumentParsing,
    DocumentWriting,
    HighCostMatching
}

public sealed class ResourceBudgetOptions
{
    public const string SectionName = "ResourceBudgets";

    public int MaxConcurrentDocumentParsers { get; set; } = 8;
    public int MaxConcurrentDocumentWriters { get; set; } = 4;
    public int MaxConcurrentHighCostMatching { get; set; } = 8;
    public long MaxDocumentBytes { get; set; } = 50L * 1024 * 1024;
    public int MaxWriteOperations { get; set; } = 200_000;
    public int MaxMatchingItems { get; set; } = 50_000;
}

public sealed class ResourceBudgetExceededException : ApplicationServiceException
{
    public ResourceBudgetExceededException(string budgetName, long actual, long limit)
        : base(400, $"资源预算超限：{budgetName}，实际 {actual}，上限 {limit}")
    {
        BudgetName = budgetName;
        Actual = actual;
        Limit = limit;
    }

    public string BudgetName { get; }
    public long Actual { get; }
    public long Limit { get; }
}

public interface IResourceBudgetGovernor
{
    ValueTask<ResourceBudgetLease> AcquireAsync(
        ResourceWorkload workload,
        CancellationToken cancellationToken = default);

    void ValidateDocumentSize(long bytes);
    void ValidateWriteOperations(int operationCount);
    void ValidateMatchingItems(int itemCount);
}

public sealed class ResourceBudgetLease : IDisposable
{
    private SemaphoreSlim? _semaphore;

    internal ResourceBudgetLease(SemaphoreSlim? semaphore, TimeSpan waitDuration)
    {
        _semaphore = semaphore;
        WaitDuration = waitDuration;
    }

    public TimeSpan WaitDuration { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

/// <summary>
/// 进程内资源闸门。实例由 DI 以 Singleton 管理，不使用静态锁或静态信号量。
/// </summary>
public sealed class ResourceBudgetGovernor : IResourceBudgetGovernor, IDisposable
{
    public const string MeterName = "AcceptanceSpecSystem.ResourceBudgets";

    private readonly ResourceBudgetOptions _options;
    private readonly IReadOnlyDictionary<ResourceWorkload, SemaphoreSlim> _gates;
    private readonly Meter _meter = new(MeterName);
    private readonly Histogram<double> _waitDuration;
    private readonly Counter<long> _acquired;
    private readonly Counter<long> _cancelled;
    private readonly Counter<long> _rejected;
    private readonly UpDownCounter<long> _waiting;

    public ResourceBudgetGovernor(IOptions<ResourceBudgetOptions> options)
    {
        _options = options.Value;
        _gates = new Dictionary<ResourceWorkload, SemaphoreSlim>
        {
            [ResourceWorkload.DocumentParsing] = CreateGate(_options.MaxConcurrentDocumentParsers),
            [ResourceWorkload.DocumentWriting] = CreateGate(_options.MaxConcurrentDocumentWriters),
            [ResourceWorkload.HighCostMatching] = CreateGate(_options.MaxConcurrentHighCostMatching)
        };
        _waitDuration = _meter.CreateHistogram<double>("resource_wait_duration_ms", "ms");
        _acquired = _meter.CreateCounter<long>("resource_acquired_total");
        _cancelled = _meter.CreateCounter<long>("resource_wait_cancelled_total");
        _rejected = _meter.CreateCounter<long>("resource_budget_rejected_total");
        _waiting = _meter.CreateUpDownCounter<long>("resource_waiting");
    }

    public async ValueTask<ResourceBudgetLease> AcquireAsync(
        ResourceWorkload workload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _gates[workload];
        var tags = new TagList { { "workload", workload.ToString() } };
        var startedAt = Stopwatch.GetTimestamp();
        _waiting.Add(1, tags);
        try
        {
            await gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _cancelled.Add(1, tags);
            throw;
        }
        finally
        {
            _waiting.Add(-1, tags);
        }

        var waitDuration = Stopwatch.GetElapsedTime(startedAt);
        _waitDuration.Record(waitDuration.TotalMilliseconds, tags);
        _acquired.Add(1, tags);
        return new ResourceBudgetLease(gate, waitDuration);
    }

    public void ValidateDocumentSize(long bytes)
    {
        Validate("document_bytes", bytes, _options.MaxDocumentBytes);
    }

    public void ValidateWriteOperations(int operationCount)
    {
        Validate("write_operations", operationCount, _options.MaxWriteOperations);
    }

    public void ValidateMatchingItems(int itemCount)
    {
        Validate("matching_items", itemCount, _options.MaxMatchingItems);
    }

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }

        _meter.Dispose();
    }

    private void Validate(string budgetName, long actual, long limit)
    {
        if (limit <= 0 || actual <= limit)
        {
            return;
        }

        _rejected.Add(1, new TagList { { "budget", budgetName } });
        throw new ResourceBudgetExceededException(budgetName, actual, limit);
    }

    private static SemaphoreSlim CreateGate(int concurrency)
    {
        var normalized = concurrency <= 0 ? int.MaxValue : concurrency;
        return new SemaphoreSlim(normalized, normalized);
    }
}
