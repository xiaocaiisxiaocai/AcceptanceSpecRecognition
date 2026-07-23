using System.Collections.Concurrent;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using Microsoft.Extensions.Options;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Application.Services;

public enum AiServiceReadinessState
{
    Unknown,
    Checking,
    Available,
    Unavailable
}

public sealed record AiServiceReadinessSnapshot(
    int ServiceId,
    CoreAiServicePurpose Purpose,
    AiServiceReadinessState State,
    DateTime? CheckedAt,
    DateTime ExpiresAt,
    string Message,
    long Generation);

/// <summary>
/// 进程内、按服务和用途隔离的短期运行状态注册表。
/// </summary>
public sealed class AiServiceReadinessRegistry : IAiServiceRuntimeStatusReporter, IAiServiceRuntimeAvailability
{
    private readonly ConcurrentDictionary<ReadinessKey, AiServiceReadinessSnapshot> _entries = new();
    private readonly ConcurrentDictionary<int, long> _serviceGenerations = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _statusTtl;
    private readonly TimeSpan _checkingTtl;
    private long _configurationVersion;

    public AiServiceReadinessRegistry(
        TimeProvider timeProvider,
        IOptions<AiServiceReadinessOptions> options)
    {
        _timeProvider = timeProvider;
        _statusTtl = TimeSpan.FromSeconds(Math.Clamp(options.Value.StatusTtlSeconds, 1, 3600));
        _checkingTtl = TimeSpan.FromSeconds(Math.Clamp(options.Value.ProbeTimeoutSeconds * 2, 2, 600));
    }

    public AiServiceReadinessSnapshot GetSnapshot(int serviceId, CoreAiServicePurpose purpose)
    {
        var key = new ReadinessKey(serviceId, purpose);
        if (!_entries.TryGetValue(key, out var snapshot))
            return Unknown(serviceId, purpose);

        if (snapshot.Generation != CaptureGeneration(serviceId))
        {
            _entries.TryRemove(new KeyValuePair<ReadinessKey, AiServiceReadinessSnapshot>(key, snapshot));
            return Unknown(serviceId, purpose);
        }

        if (snapshot.ExpiresAt > _timeProvider.GetUtcNow().UtcDateTime)
            return snapshot;

        _entries.TryRemove(new KeyValuePair<ReadinessKey, AiServiceReadinessSnapshot>(key, snapshot));
        return Unknown(serviceId, purpose);
    }

    public bool TryMarkChecking(
        int serviceId,
        CoreAiServicePurpose purpose,
        out long generation)
    {
        generation = CaptureGeneration(serviceId);
        var key = new ReadinessKey(serviceId, purpose);
        while (true)
        {
            var current = GetSnapshot(serviceId, purpose);
            if (current.State != AiServiceReadinessState.Unknown)
                return false;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var next = new AiServiceReadinessSnapshot(
                serviceId,
                purpose,
                AiServiceReadinessState.Checking,
                null,
                now.Add(_checkingTtl),
                "正在检测 AI 服务可用性",
                generation);
            if (_entries.TryAdd(key, next))
            {
                if (generation == CaptureGeneration(serviceId))
                    return true;

                _entries.TryRemove(new KeyValuePair<ReadinessKey, AiServiceReadinessSnapshot>(key, next));
                return false;
            }
        }
    }

    public bool TryMarkChecking(int serviceId, CoreAiServicePurpose purpose) =>
        TryMarkChecking(serviceId, purpose, out _);

    public bool ResetCheckingIfCurrent(
        int serviceId,
        CoreAiServicePurpose purpose,
        long expectedGeneration)
    {
        if (expectedGeneration != CaptureGeneration(serviceId))
            return false;

        var key = new ReadinessKey(serviceId, purpose);
        while (_entries.TryGetValue(key, out var snapshot))
        {
            if (snapshot.Generation != expectedGeneration ||
                snapshot.State != AiServiceReadinessState.Checking)
            {
                return false;
            }

            if (_entries.TryRemove(
                    new KeyValuePair<ReadinessKey, AiServiceReadinessSnapshot>(key, snapshot)))
            {
                return true;
            }
        }

        return false;
    }

    public long CaptureGeneration(int serviceId) =>
        _serviceGenerations.GetOrAdd(serviceId, 0);

    public void ReportAvailable(int serviceId, CoreAiServicePurpose purpose)
    {
        SetTerminal(serviceId, purpose, AiServiceReadinessState.Available, "AI 服务当前可用");
    }

    public void ReportUnavailable(int serviceId, CoreAiServicePurpose purpose, string? message = null)
    {
        // 外部异常正文不得进入面向客户端的 readiness 缓存。
        SetTerminal(serviceId, purpose, AiServiceReadinessState.Unavailable, "AI 服务暂时不可用，请稍后重试或检查配置");
    }

    public void ReportAvailableIfCurrent(
        int serviceId,
        CoreAiServicePurpose purpose,
        long expectedGeneration)
    {
        SetTerminalIfCurrent(
            serviceId,
            purpose,
            AiServiceReadinessState.Available,
            "AI 服务当前可用",
            expectedGeneration);
    }

    public void ReportUnavailableIfCurrent(
        int serviceId,
        CoreAiServicePurpose purpose,
        long expectedGeneration,
        string? message = null)
    {
        SetTerminalIfCurrent(
            serviceId,
            purpose,
            AiServiceReadinessState.Unavailable,
            "AI 服务暂时不可用，请稍后重试或检查配置",
            expectedGeneration);
    }

    public bool IsAvailable(int serviceId, CoreAiServicePurpose purpose) =>
        GetSnapshot(serviceId, purpose).State == AiServiceReadinessState.Available;

    public long ConfigurationVersion => Interlocked.Read(ref _configurationVersion);

    public void Invalidate(int serviceId)
    {
        Interlocked.Increment(ref _configurationVersion);
        _serviceGenerations.AddOrUpdate(serviceId, 1, static (_, generation) => generation + 1);
        foreach (var key in _entries.Keys.Where(key => key.ServiceId == serviceId))
            _entries.TryRemove(key, out _);
    }

    public IReadOnlyList<AiServiceReadinessSnapshot> GetCurrentSnapshots()
    {
        return _entries.Keys
            .Select(key => GetSnapshot(key.ServiceId, key.Purpose))
            .Where(snapshot => snapshot.State != AiServiceReadinessState.Unknown)
            .OrderBy(snapshot => snapshot.ServiceId)
            .ThenBy(snapshot => snapshot.Purpose)
            .ToList();
    }

    private void SetTerminal(
        int serviceId,
        CoreAiServicePurpose purpose,
        AiServiceReadinessState state,
        string message)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _entries[new ReadinessKey(serviceId, purpose)] = new AiServiceReadinessSnapshot(
            serviceId,
            purpose,
            state,
            now,
            now.Add(_statusTtl),
            message,
            CaptureGeneration(serviceId));
    }

    private void SetTerminalIfCurrent(
        int serviceId,
        CoreAiServicePurpose purpose,
        AiServiceReadinessState state,
        string message,
        long expectedGeneration)
    {
        if (expectedGeneration != CaptureGeneration(serviceId))
            return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _entries[new ReadinessKey(serviceId, purpose)] = new AiServiceReadinessSnapshot(
            serviceId,
            purpose,
            state,
            now,
            now.Add(_statusTtl),
            message,
            expectedGeneration);
    }

    private AiServiceReadinessSnapshot Unknown(int serviceId, CoreAiServicePurpose purpose)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return new AiServiceReadinessSnapshot(
            serviceId,
            purpose,
            AiServiceReadinessState.Unknown,
            null,
            now,
            "尚未检测 AI 服务可用性",
            CaptureGeneration(serviceId));
    }

    private sealed record ReadinessKey(int ServiceId, CoreAiServicePurpose Purpose);
}
