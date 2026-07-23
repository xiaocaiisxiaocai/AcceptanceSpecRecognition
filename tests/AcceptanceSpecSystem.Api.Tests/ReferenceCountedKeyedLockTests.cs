using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class ReferenceCountedKeyedLockTests
{
    [Fact]
    public async Task SameKeyConcurrentLeases_ShouldRemainMutuallyExclusiveAndRecycleEntry()
    {
        var keyedLock = new ReferenceCountedKeyedLock<string>(StringComparer.Ordinal);
        var activeCount = 0;
        var maxActiveCount = 0;

        var tasks = Enumerable.Range(0, 64)
            .Select(async _ =>
            {
                using var lease = await keyedLock.AcquireAsync("same-session");
                var current = Interlocked.Increment(ref activeCount);
                UpdateMaximum(ref maxActiveCount, current);
                await Task.Yield();
                Interlocked.Decrement(ref activeCount);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        maxActiveCount.Should().Be(1);
        activeCount.Should().Be(0);
        keyedLock.Count.Should().Be(0);
    }

    [Fact]
    public async Task WaitingLease_ShouldKeepSameEntryAliveUntilLastLeaseExits()
    {
        var keyedLock = new ReferenceCountedKeyedLock<string>(StringComparer.Ordinal);
        var firstLease = await keyedLock.AcquireAsync("waiting-session");
        var waitingLeaseTask = keyedLock.AcquireAsync("waiting-session").AsTask();

        waitingLeaseTask.IsCompleted.Should().BeFalse();
        keyedLock.Count.Should().Be(1);

        firstLease.Dispose();
        using var waitingLease = await waitingLeaseTask.WaitAsync(TimeSpan.FromSeconds(5));
        var thirdLeaseTask = keyedLock.AcquireAsync("waiting-session").AsTask();

        thirdLeaseTask.IsCompleted.Should().BeFalse(
            "等待者取得租约后，同一会话的新请求必须继续等待同一个锁对象");
        keyedLock.Count.Should().Be(1);

        waitingLease.Dispose();
        using var thirdLease = await thirdLeaseTask.WaitAsync(TimeSpan.FromSeconds(5));
        keyedLock.Count.Should().Be(1);

        thirdLease.Dispose();
        keyedLock.Count.Should().Be(0);
    }

    [Fact]
    public async Task CancelledWaiter_ShouldReleaseReferenceWithoutReplacingCurrentLock()
    {
        var keyedLock = new ReferenceCountedKeyedLock<string>(StringComparer.Ordinal);
        using var holder = await keyedLock.AcquireAsync("cancelled-session");
        using var cancellation = new CancellationTokenSource();
        var waiter = keyedLock.AcquireAsync("cancelled-session", cancellation.Token).AsTask();

        cancellation.Cancel();

        Func<Task> waitForCancelledLease = async () => await waiter;
        await waitForCancelledLease.Should().ThrowAsync<OperationCanceledException>();
        keyedLock.Count.Should().Be(1);

        holder.Dispose();
        keyedLock.Count.Should().Be(0);
    }

    [Fact]
    public async Task ManyDistinctSessions_ShouldRecycleAllEntriesAfterLeasesExit()
    {
        var keyedLock = new ReferenceCountedKeyedLock<string>(StringComparer.Ordinal);

        await Task.WhenAll(Enumerable.Range(0, 1_000).Select(async index =>
        {
            using var lease = await keyedLock.AcquireAsync($"session-{index}");
            await Task.Yield();
        }));

        keyedLock.Count.Should().Be(0);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (candidate <= current || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
            {
                return;
            }
        }
    }
}
