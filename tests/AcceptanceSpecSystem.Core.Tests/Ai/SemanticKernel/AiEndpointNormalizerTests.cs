using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.AI.SemanticKernel;

public class AiEndpointNormalizerTests
{
    [Theory]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://example.com/v1?api_key=secret")]
    [InlineData("https://example.com/v1#fragment")]
    [InlineData("https://example.com:0")]
    [InlineData("ftp://example.com/models")]
    [InlineData("http:127.0.0.1:11434")]
    [InlineData("https:///models")]
    public void 规范化端点_遇到非安全绝对地址组成时应拒绝(string endpoint)
    {
        var action = () => AiEndpointNormalizer.NormalizeRequiredEndpoint(endpoint);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*有效的 http/https 绝对地址*");
    }

    [Fact]
    public void 规范化端点_遇到尾点和国际化域名时应输出规范主机并保留路径()
    {
        var normalized = AiEndpointNormalizer.NormalizeRequiredEndpoint("HTTPS://例子.测试.:443/v1/models/");

        normalized.Should().Be("https://xn--fsqu00a.xn--0zwm56d/v1/models");
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434/api", "http://127.0.0.1:11434/api")]
    [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1")]
    [InlineData("https://api.openai.com/", "https://api.openai.com")]
    public void 规范化端点_不解析地址且仅规范URI结构(string endpoint, string expected)
    {
        var normalized = AiEndpointNormalizer.NormalizeRequiredEndpoint(endpoint);

        normalized.Should().Be(expected);
    }
}
