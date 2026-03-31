using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.AI.SemanticKernel;

public class AiEndpointNormalizerTests
{
    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://10.0.0.5:8080")]
    [InlineData("http://192.168.1.20:8080")]
    public void NormalizeRequiredEndpoint_WhenEndpointTargetsLocalOrPrivateAddress_ShouldThrow(string endpoint)
    {
        var action = () => AiEndpointNormalizer.NormalizeRequiredEndpoint(endpoint);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*不允许使用本地或内网地址*");
    }

    [Fact]
    public void NormalizeRequiredEndpoint_WhenEndpointIsPublicHttps_ShouldReturnNormalizedAddress()
    {
        var normalized = AiEndpointNormalizer.NormalizeRequiredEndpoint("https://api.openai.com/");

        normalized.Should().Be("https://api.openai.com");
    }
}
