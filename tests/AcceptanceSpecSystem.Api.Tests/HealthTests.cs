using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class HealthTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_ShouldReturn200()
    {
        var resp = await _client.GetAsync("/health");
        resp.IsSuccessStatusCode.Should().BeTrue();
    }
}

