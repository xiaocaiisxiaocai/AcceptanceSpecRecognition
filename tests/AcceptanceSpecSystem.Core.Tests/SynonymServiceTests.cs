using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class SynonymServiceTests
{
    [Fact]
    public async Task GetWordToStandardMapAsync_ShouldCollapseConcurrentCacheMisses()
    {
        var provider = new CountingSynonymDataProvider();
        var service = new SynonymService(provider);

        var tasks = Enumerable.Range(0, 6)
            .Select(_ => service.GetWordToStandardMapAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        provider.CallCount.Should().Be(1, "同一实例的并发缓存未命中应只触发一次底层加载");
        results.Should().OnlyContain(map => map["别名"] == "标准词");
    }

    private sealed class CountingSynonymDataProvider : ISynonymDataProvider
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<IReadOnlyList<SynonymGroupModel>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(50, cancellationToken);
            return
            [
                new SynonymGroupModel(
                [
                    new SynonymWordModel("标准词", true),
                    new SynonymWordModel("别名", false)
                ])
            ];
        }
    }
}
