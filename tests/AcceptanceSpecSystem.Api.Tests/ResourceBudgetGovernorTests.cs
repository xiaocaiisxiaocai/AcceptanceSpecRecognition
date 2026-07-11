using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ResourceBudgetGovernorTests
{
    [Fact]
    public async Task ConcurrencyGate_ShouldQueueSecondLeaseAndExposeWaitMetric()
    {
        var measurements = new ConcurrentBag<(string Name, double Value)>();
        using var listener = CreateListener(measurements);
        using var governor = CreateGovernor(parseConcurrency: 1);
        using var first = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);

        var secondTask = governor.AcquireAsync(ResourceWorkload.DocumentParsing).AsTask();
        await Task.Delay(30);
        secondTask.IsCompleted.Should().BeFalse();

        first.Dispose();
        using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

        second.WaitDuration.Should().BeGreaterThan(TimeSpan.Zero);
        measurements.Should().Contain(item =>
            item.Name == "resource_wait_duration_ms" && item.Value > 0);
        measurements.Count(item => item.Name == "resource_acquired_total").Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task WaitingForGate_ShouldPropagateCancellationAndReleaseWaiterState()
    {
        using var governor = CreateGovernor(writeConcurrency: 1);
        using var holder = await governor.AcquireAsync(ResourceWorkload.DocumentWriting);
        using var cancellation = new CancellationTokenSource();
        var waiting = governor.AcquireAsync(ResourceWorkload.DocumentWriting, cancellation.Token).AsTask();

        cancellation.Cancel();
        Func<Task> waitForCancelledLease = async () => await waiting;
        await waitForCancelledLease.Should().ThrowAsync<OperationCanceledException>();

        holder.Dispose();
        using var next = await governor.AcquireAsync(ResourceWorkload.DocumentWriting)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void InputBudgets_ShouldFailFastWithDiagnosticLimitAndActualValues()
    {
        using var governor = new ResourceBudgetGovernor(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
        {
            MaxDocumentBytes = 10,
            MaxWriteOperations = 2,
            MaxMatchingItems = 3
        }));

        Action document = () => governor.ValidateDocumentSize(11);
        Action write = () => governor.ValidateWriteOperations(3);
        Action matching = () => governor.ValidateMatchingItems(4);

        document.Should().Throw<ResourceBudgetExceededException>()
            .Where(exception => exception.BudgetName == "document_bytes" && exception.Actual == 11 && exception.Limit == 10);
        write.Should().Throw<ResourceBudgetExceededException>()
            .Where(exception => exception.BudgetName == "write_operations" && exception.Actual == 3 && exception.Limit == 2);
        matching.Should().Throw<ResourceBudgetExceededException>()
            .Where(exception => exception.BudgetName == "matching_items" && exception.Actual == 4 && exception.Limit == 3);
    }

    [Fact]
    public async Task Metrics_ShouldReportCancelledWaitAndRejectedBudget()
    {
        var measurements = new ConcurrentBag<(string Name, double Value)>();
        using var listener = CreateListener(measurements);
        using var governor = CreateGovernor(matchingConcurrency: 1, maxMatchingItems: 1);
        using var holder = await governor.AcquireAsync(ResourceWorkload.HighCostMatching);
        using var cancellation = new CancellationTokenSource();
        var waiting = governor.AcquireAsync(ResourceWorkload.HighCostMatching, cancellation.Token).AsTask();
        cancellation.Cancel();
        try
        {
            await waiting;
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            governor.ValidateMatchingItems(2);
        }
        catch (ResourceBudgetExceededException)
        {
        }

        measurements.Should().Contain(item => item.Name == "resource_wait_cancelled_total" && item.Value == 1);
        measurements.Should().Contain(item => item.Name == "resource_budget_rejected_total" && item.Value == 1);
    }

    [Fact]
    public async Task MatchingDecorator_ShouldLimitConcurrencyAndCancelWaitingRequest()
    {
        using var governor = CreateGovernor(matchingConcurrency: 1);
        var inner = new BlockingMatchingService();
        var service = new ResourceGovernedMatchingService(inner, governor);
        var sources = new[] { new MatchSource { Project = "P", Specification = "S" } };
        var candidates = new[] { new MatchCandidate { SpecId = 1, Project = "P", Specification = "S" } };

        var first = service.BatchMatchAsync(sources, candidates);
        await inner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var second = service.BatchMatchAsync(sources, candidates, cancellationToken: cancellation.Token);
        cancellation.Cancel();

        Func<Task> waitForSecond = async () => await second;
        await waitForSecond.Should().ThrowAsync<OperationCanceledException>();
        inner.InvocationCount.Should().Be(1, "被取消的等待者不得进入高成本匹配实现");

        inner.Release();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MatchingDecorator_ShouldRejectOversizedInputBeforeInvokingInnerService()
    {
        using var governor = CreateGovernor(maxMatchingItems: 1);
        var inner = new BlockingMatchingService(releaseImmediately: true);
        var service = new ResourceGovernedMatchingService(inner, governor);

        Func<Task> match = async () => await service.BatchMatchAsync(
            [new MatchSource { Project = "P", Specification = "S" }],
            [new MatchCandidate { SpecId = 1, Project = "P", Specification = "S" }]);

        await match.Should().ThrowAsync<ResourceBudgetExceededException>();
        inner.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task MixedCancellationPressure_ShouldRecoverEveryPermitWithoutDeadlock()
    {
        using var governor = CreateGovernor(parseConcurrency: 3);
        var holders = new[]
        {
            await governor.AcquireAsync(ResourceWorkload.DocumentParsing),
            await governor.AcquireAsync(ResourceWorkload.DocumentParsing),
            await governor.AcquireAsync(ResourceWorkload.DocumentParsing)
        };
        var cancellations = Enumerable.Range(0, 24)
            .Select(_ => new CancellationTokenSource())
            .ToArray();
        var waiters = cancellations.Select(async (source, index) =>
        {
            try
            {
                using var lease = await governor.AcquireAsync(
                    ResourceWorkload.DocumentParsing,
                    source.Token);
                return true;
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                return false;
            }
        }).ToArray();

        waiters.Should().OnlyContain(task => !task.IsCompleted);
        foreach (var cancellation in cancellations.Where((_, index) => index % 2 == 0))
        {
            cancellation.Cancel();
        }

        var cancelledOutcomes = await Task.WhenAll(waiters.Where((_, index) => index % 2 == 0))
            .WaitAsync(TimeSpan.FromSeconds(5));
        cancelledOutcomes.Should().OnlyContain(value => !value);

        foreach (var holder in holders)
        {
            holder.Dispose();
        }

        var outcomes = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));
        outcomes.Count(value => value).Should().Be(12);
        outcomes.Count(value => !value).Should().Be(12);

        using var recovered1 = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);
        using var recovered2 = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);
        using var recovered3 = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);
        using var finalCancellation = new CancellationTokenSource();
        var blocked = governor.AcquireAsync(
            ResourceWorkload.DocumentParsing,
            finalCancellation.Token).AsTask();
        blocked.IsCompleted.Should().BeFalse();
        finalCancellation.Cancel();
        Func<Task> waitForCancellation = async () => await blocked;
        await waitForCancellation.Should().ThrowAsync<OperationCanceledException>();

        foreach (var cancellation in cancellations)
        {
            cancellation.Dispose();
        }
    }

    private static ResourceBudgetGovernor CreateGovernor(
        int parseConcurrency = 8,
        int writeConcurrency = 4,
        int matchingConcurrency = 8,
        int maxMatchingItems = 50_000)
    {
        return new ResourceBudgetGovernor(Microsoft.Extensions.Options.Options.Create(new ResourceBudgetOptions
        {
            MaxConcurrentDocumentParsers = parseConcurrency,
            MaxConcurrentDocumentWriters = writeConcurrency,
            MaxConcurrentHighCostMatching = matchingConcurrency,
            MaxMatchingItems = maxMatchingItems
        }));
    }

    private static MeterListener CreateListener(ConcurrentBag<(string Name, double Value)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ResourceBudgetGovernor.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.Add((instrument.Name, value)));
        listener.Start();
        return listener;
    }

    private sealed class BlockingMatchingService(bool releaseImmediately = false) : IMatchingService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InvocationCount { get; private set; }

        public Task<List<MatchResult>> FindMatchesAsync(
            MatchSource source,
            IEnumerable<MatchCandidate> candidates,
            MatchingConfig? config = null)
        {
            InvocationCount++;
            return Task.FromResult(new List<MatchResult>());
        }

        public async Task<BatchMatchResult> BatchMatchAsync(
            IEnumerable<MatchSource> sources,
            IEnumerable<MatchCandidate> candidates,
            MatchingConfig? config = null,
            IProgress<BatchMatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            Started.TrySetResult();
            if (!releaseImmediately)
            {
                await _release.Task.WaitAsync(cancellationToken);
            }

            return new BatchMatchResult();
        }

        public void Release() => _release.TrySetResult();
    }
}
