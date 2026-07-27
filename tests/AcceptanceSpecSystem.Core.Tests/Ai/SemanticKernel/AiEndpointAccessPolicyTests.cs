using System.Net;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Core.Tests.AI.SemanticKernel;

public class AiEndpointAccessPolicyTests
{
    [Theory]
    [InlineData("https://0.0.0.0")]
    [InlineData("https://10.0.0.1")]
    [InlineData("https://100.64.0.1")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://192.0.2.1")]
    [InlineData("https://198.18.0.1")]
    [InlineData("https://192.88.99.2")]
    [InlineData("https://224.0.0.1")]
    [InlineData("https://255.255.255.255")]
    [InlineData("https://[::]")]
    [InlineData("https://[::1]")]
    [InlineData("https://[fc00::1]")]
    [InlineData("https://[fe80::1]")]
    [InlineData("https://[ff02::1]")]
    [InlineData("https://[2001:db8::1]")]
    [InlineData("https://[2001:2::1]")]
    [InlineData("https://[2002::1]")]
    [InlineData("https://[100:0:0:1::1]")]
    [InlineData("https://[2001:10::1]")]
    [InlineData("https://[3fff::1]")]
    [InlineData("https://[5f00::1]")]
    [InlineData("https://[2d00::1]")]
    [InlineData("https://[2e00::1]")]
    [InlineData("https://[3000::1]")]
    [InlineData("https://[3ffe::1]")]
    [InlineData("https://[::ffff:10.0.0.1]")]
    public async Task 地址策略_遇到非全球单播地址时应稳定拒绝(string endpoint)
    {
        var policy = CreatePolicy(new FakeDnsResolver());

        var action = () => policy.ValidateAsync(
            new Uri(endpoint),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<AiEndpointAccessException>();
        exception.Which.Category.Should().Be(AiEndpointAccessFailureCategory.AddressBlocked);
        exception.Which.Message.Should().NotContain(new Uri(endpoint).Host);
    }

    [Fact]
    public async Task 地址策略_遇到IPv4映射IPv6公网地址时应按IPv4返回且不走DNS()
    {
        var resolver = new FakeDnsResolver();
        var policy = CreatePolicy(resolver);

        var result = await policy.ValidateAsync(
            new Uri("https://[::ffff:8.8.8.8]"),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None);

        result.Addresses.Should().Equal(IPAddress.Parse("8.8.8.8"));
        resolver.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("https://192.0.0.9")]
    [InlineData("https://[2001:1::1]")]
    [InlineData("https://[2001:20::1]")]
    [InlineData("https://[2001:4860:4860::8888]")]
    [InlineData("https://[2003::1]")]
    [InlineData("https://[2404:6800::1]")]
    [InlineData("https://[2606:4700::1111]")]
    [InlineData("https://[2a00:1450::1]")]
    [InlineData("https://[2c0f:f248::1]")]
    public async Task 地址策略_IANA广域协议块中的全球可达例外仍应允许HTTPS(string endpoint)
    {
        var result = await CreatePolicy(new FakeDnsResolver()).ValidateAsync(
            new Uri(endpoint),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None);

        result.Addresses.Should().ContainSingle();
    }

    [Fact]
    public async Task 地址策略_域名同时解析到公网和危险地址时应整体拒绝()
    {
        var resolver = new FakeDnsResolver(
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse("127.0.0.1"));
        var policy = CreatePolicy(resolver);

        var action = () => policy.ValidateAsync(
            new Uri("https://models.example.com"),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception => exception.Category == AiEndpointAccessFailureCategory.AddressBlocked);
    }

    [Theory]
    [InlineData(AiServiceType.Ollama, "http://10.10.1.8:11434")]
    [InlineData(AiServiceType.LMStudio, "http://10.10.1.8:1234")]
    [InlineData(AiServiceType.CustomOpenAICompatible, "http://10.10.1.8:8080")]
    public async Task 地址策略_显式CIDR和端口白名单应允许内网HTTP(
        AiServiceType serviceType,
        string endpoint)
    {
        var options = new AiEndpointSecurityOptions
        {
            PrivateNetworkAllowlist =
            [
                new AiEndpointPrivateNetworkRule
                {
                    Cidr = "10.10.0.0/16",
                    Ports = [11434, 1234, 8080]
                }
            ]
        };
        var policy = CreatePolicy(new FakeDnsResolver(), options);

        var result = await policy.ValidateAsync(
            new Uri(endpoint),
            serviceType,
            CancellationToken.None);

        result.Addresses.Should().Equal(IPAddress.Parse("10.10.1.8"));
    }

    [Theory]
    [InlineData(AiServiceType.Ollama, "http://127.0.0.1:11434")]
    [InlineData(AiServiceType.LMStudio, "http://[::1]:1234")]
    public async Task 地址策略_默认本地提供商端口应允许LoopbackHTTP(
        AiServiceType serviceType,
        string endpoint)
    {
        var result = await CreatePolicy(new FakeDnsResolver()).ValidateAsync(
            new Uri(endpoint),
            serviceType,
            CancellationToken.None);

        result.Addresses.Should().ContainSingle();
        IPAddress.IsLoopback(result.Addresses[0]).Should().BeTrue();
    }

    [Theory]
    [InlineData(AiServiceType.Ollama, "http://127.0.0.1:1234")]
    [InlineData(AiServiceType.LMStudio, "http://127.0.0.1:11434")]
    [InlineData(AiServiceType.CustomOpenAICompatible, "http://127.0.0.1:8080")]
    [InlineData(AiServiceType.CustomOpenAICompatible, "http://8.8.8.8:8080")]
    [InlineData(AiServiceType.CustomOpenAICompatible, "http://10.10.1.8:8080")]
    public async Task 地址策略_未明确允许的HTTP地址或端口应拒绝(
        AiServiceType serviceType,
        string endpoint)
    {
        var policy = CreatePolicy(new FakeDnsResolver());

        var action = () => policy.ValidateAsync(
            new Uri(endpoint),
            serviceType,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception => exception.Category == AiEndpointAccessFailureCategory.AddressBlocked);
    }

    [Theory]
    [InlineData("http://169.254.169.254:8080")]
    [InlineData("http://[fe80::1]:8080")]
    public async Task 地址策略_链路本地地址即使命中显式白名单也应拒绝(string endpoint)
    {
        var options = new AiEndpointSecurityOptions
        {
            PrivateNetworkAllowlist =
            [
                new AiEndpointPrivateNetworkRule { Cidr = "0.0.0.0/0", Ports = [8080] },
                new AiEndpointPrivateNetworkRule { Cidr = "::/0", Ports = [8080] }
            ]
        };
        var policy = CreatePolicy(new FakeDnsResolver(), options);

        var action = () => policy.ValidateAsync(
            new Uri(endpoint),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception => exception.Category == AiEndpointAccessFailureCategory.AddressBlocked);
    }

    [Fact]
    public async Task 地址策略_元数据主机应在DNS前拒绝()
    {
        var resolver = new FakeDnsResolver(IPAddress.Parse("8.8.8.8"));
        var policy = CreatePolicy(resolver);

        var action = () => policy.ValidateAsync(
            new Uri("https://metadata.google.internal"),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None).AsTask();

        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception => exception.Category == AiEndpointAccessFailureCategory.AddressBlocked);
        resolver.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task 地址策略_DNS取消应原样传播且不包装()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var resolver = new CancellingDnsResolver();
        var policy = CreatePolicy(resolver);

        var action = () => policy.ValidateAsync(
            new Uri("https://models.example.com"),
            AiServiceType.CustomOpenAICompatible,
            cancellation.Token).AsTask();

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task 地址策略_DNS失败应返回稳定类别且不泄露远端详情()
    {
        var policy = CreatePolicy(new FailingDnsResolver("secret.example.com -> 10.0.0.8"));

        var action = () => policy.ValidateAsync(
            new Uri("https://secret.example.com/v1?ignored=true"),
            AiServiceType.CustomOpenAICompatible,
            CancellationToken.None).AsTask();

        var exception = await action.Should().ThrowAsync<AiEndpointAccessException>();
        exception.Which.Category.Should().Be(AiEndpointAccessFailureCategory.DnsFailed);
        exception.Which.Message.Should().NotContain("secret.example.com").And.NotContain("10.0.0.8");
        exception.Which.ToString().Should().NotContain("secret.example.com").And.NotContain("10.0.0.8");
    }

    [Fact]
    public async Task 地址策略_配置变化后应增加代次并拒绝旧代次校验()
    {
        var monitor = new MutableOptionsMonitor<AiEndpointSecurityOptions>(new());
        using var policy = new AiEndpointAccessPolicy(new FakeDnsResolver(), monitor);
        var firstGeneration = policy.Generation;

        monitor.Set(new AiEndpointSecurityOptions
        {
            PrivateNetworkAllowlist =
            [
                new AiEndpointPrivateNetworkRule { Cidr = "10.0.0.0/8", Ports = [8080] }
            ]
        });

        policy.Generation.Should().BeGreaterThan(firstGeneration);
        var action = () => policy.ValidateAsync(
            new Uri("https://8.8.8.8"),
            AiServiceType.CustomOpenAICompatible,
            firstGeneration,
            CancellationToken.None).AsTask();
        await action.Should().ThrowAsync<AiEndpointAccessException>()
            .Where(exception => exception.Category == AiEndpointAccessFailureCategory.PolicyChanged);
    }

    [Fact]
    public async Task 地址策略_新规则发布前并发校验不得在旧代次使用新规则()
    {
        var monitor = new MutableOptionsMonitor<AiEndpointSecurityOptions>(new());
        using var publication = new BlockingPublicationHook();
        using var policy = new AiEndpointAccessPolicy(
            new FakeDnsResolver(),
            monitor,
            publication);
        var oldGeneration = policy.Generation;
        var publishTask = Task.Run(() => monitor.Set(new AiEndpointSecurityOptions
        {
            PrivateNetworkAllowlist =
            [
                new AiEndpointPrivateNetworkRule { Cidr = "10.20.0.0/16", Ports = [8080] }
            ]
        }));

        await publication.WaitUntilRulesPreparedAsync();
        try
        {
            var action = () => policy.ValidateAsync(
                new Uri("http://10.20.1.9:8080"),
                AiServiceType.CustomOpenAICompatible,
                oldGeneration,
                CancellationToken.None).AsTask();

            await action.Should().ThrowAsync<AiEndpointAccessException>()
                .Where(exception => exception.Category == AiEndpointAccessFailureCategory.AddressBlocked);
        }
        finally
        {
            publication.AllowPublish();
            await publishTask;
        }

        policy.Generation.Should().BeGreaterThan(oldGeneration);
    }

    private static AiEndpointAccessPolicy CreatePolicy(
        IAiDnsResolver resolver,
        AiEndpointSecurityOptions? options = null)
    {
        return new AiEndpointAccessPolicy(
            resolver,
            new MutableOptionsMonitor<AiEndpointSecurityOptions>(options ?? new AiEndpointSecurityOptions()));
    }

    private sealed class FakeDnsResolver(params IPAddress[] addresses) : IAiDnsResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
        }
    }

    private sealed class CancellingDnsResolver : IAiDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromCanceled<IReadOnlyList<IPAddress>>(cancellationToken);
        }
    }

    private sealed class FailingDnsResolver(string message) : IAiDnsResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromException<IReadOnlyList<IPAddress>>(new InvalidOperationException(message));
        }
    }

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        private readonly List<Action<T, string?>> _listeners = [];

        public T CurrentValue { get; private set; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener)
        {
            _listeners.Add(listener);
            return new CallbackRegistration(() => _listeners.Remove(listener));
        }

        public void Set(T next)
        {
            CurrentValue = next;
            foreach (var listener in _listeners.ToArray())
                listener(next, null);
        }

        private sealed class CallbackRegistration(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    private sealed class BlockingPublicationHook : IAiEndpointPolicyPublicationHook, IDisposable
    {
        private readonly TaskCompletionSource _rulesPrepared =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _allowPublish = new(false);

        public void RulesPreparedBeforePublish(long nextGeneration)
        {
            _rulesPrepared.TrySetResult();
            _allowPublish.Wait();
        }

        public Task WaitUntilRulesPreparedAsync() =>
            _rulesPrepared.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void AllowPublish() => _allowPublish.Set();

        public void Dispose() => _allowPublish.Dispose();
    }
}
