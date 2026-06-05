using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using System.Net;
using System.Text.Json;

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

    [Fact]
    public async Task Health_ShouldNotExposeDependencyEntries()
    {
        var resp = await _client.GetAsync("/health");
        var raw = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"返回内容: {raw}");
        var json = JsonSerializer.Deserialize<JsonElement>(raw);

        json.GetProperty("status").GetString().Should().Be("Healthy");
        json.TryGetProperty("entries", out _).Should().BeFalse("匿名健康检查不应暴露内部依赖项明细");
    }
}

