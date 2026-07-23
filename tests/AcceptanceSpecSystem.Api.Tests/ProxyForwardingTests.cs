using System.Net;
using AcceptanceSpecSystem.Api.Options;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class ProxyForwardingTests
{
    [Fact]
    public void Create_WhenEnabledWithoutTrustedSource_ShouldFailClosed()
    {
        var action = () => ProxyForwardingConfiguration.Create(new ProxyForwardingOptions { Enabled = true });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*KnownProxies*KnownNetworks*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Create_WhenForwardLimitIsUnsafe_ShouldReject(int limit)
    {
        var action = () => ProxyForwardingConfiguration.Create(new ProxyForwardingOptions
        {
            Enabled = true,
            ForwardLimit = limit,
            KnownProxies = ["10.0.0.2"]
        });

        action.Should().Throw<InvalidOperationException>().WithMessage("*ForwardLimit*");
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("10.0.0.0/33")]
    [InlineData("not-a-network")]
    public void Create_WhenKnownNetworkIsInvalidOrGlobal_ShouldReject(string network)
    {
        var action = () => ProxyForwardingConfiguration.Create(new ProxyForwardingOptions
        {
            Enabled = true,
            KnownNetworks = [network]
        });

        action.Should().Throw<InvalidOperationException>().WithMessage("*KnownNetworks*");
    }

    [Fact]
    public async Task Middleware_WhenImmediatePeerIsTrusted_ShouldUseForwardedClientAddress()
    {
        var options = ProxyForwardingConfiguration.Create(new ProxyForwardingOptions
        {
            Enabled = true,
            ForwardLimit = 1,
            KnownNetworks = ["10.20.0.0/24"]
        });
        var context = CreateContext("10.20.0.8", "192.0.2.25");

        var middleware = CreateMiddleware(options);
        await middleware.Invoke(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("192.0.2.25"));
    }

    [Fact]
    public async Task Middleware_WhenImmediatePeerIsUntrusted_ShouldIgnoreSpoofedForwardedAddress()
    {
        var options = ProxyForwardingConfiguration.Create(new ProxyForwardingOptions
        {
            Enabled = true,
            ForwardLimit = 1,
            KnownNetworks = ["10.20.0.0/24"]
        });
        var context = CreateContext("203.0.113.9", "192.0.2.25");

        var middleware = CreateMiddleware(options);
        await middleware.Invoke(context);

        context.Connection.RemoteIpAddress.Should().Be(IPAddress.Parse("203.0.113.9"));
    }

    [Fact]
    public async Task RateLimitPartition_ThroughTrustedNginx_ShouldRemainPerClient()
    {
        var options = ProxyForwardingConfiguration.Create(new ProxyForwardingOptions
        {
            Enabled = true,
            ForwardLimit = 1,
            KnownNetworks = ["10.20.0.0/24"]
        });
        var firstClient = CreateContext("10.20.0.8", "192.0.2.25");
        var secondClient = CreateContext("10.20.0.8", "192.0.2.26");
        var middleware = CreateMiddleware(options);

        await middleware.Invoke(firstClient);
        await middleware.Invoke(secondClient);

        RateLimitPartitionKeyResolver.Resolve(firstClient).Should().Be("192.0.2.25");
        RateLimitPartitionKeyResolver.Resolve(secondClient).Should().Be("192.0.2.26");
    }

    [Fact]
    public async Task RateLimitPartition_FromUntrustedPeer_ShouldNotBeSplitBySpoofedHeaders()
    {
        var options = ProxyForwardingConfiguration.Create(new ProxyForwardingOptions
        {
            Enabled = true,
            ForwardLimit = 1,
            KnownNetworks = ["10.20.0.0/24"]
        });
        var firstRequest = CreateContext("203.0.113.9", "192.0.2.25");
        var secondRequest = CreateContext("203.0.113.9", "192.0.2.26");
        var middleware = CreateMiddleware(options);

        await middleware.Invoke(firstRequest);
        await middleware.Invoke(secondRequest);

        RateLimitPartitionKeyResolver.Resolve(firstRequest).Should().Be("203.0.113.9");
        RateLimitPartitionKeyResolver.Resolve(secondRequest).Should().Be("203.0.113.9");
    }

    private static DefaultHttpContext CreateContext(string peerAddress, string forwardedAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peerAddress);
        context.Request.Headers["X-Forwarded-For"] = forwardedAddress;
        context.Request.Headers["X-Forwarded-Proto"] = "http";
        return context;
    }

    private static ForwardedHeadersMiddleware CreateMiddleware(ForwardedHeadersOptions options) =>
        new(_ => Task.CompletedTask, NullLoggerFactory.Instance, Microsoft.Extensions.Options.Options.Create(options));
}
