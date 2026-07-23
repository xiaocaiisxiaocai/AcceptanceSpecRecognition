using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class HealthTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LayeredHealthEndpoints_ShouldSeparateLiveReadyAndAiCapability()
    {
        (await _client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var capability = await _client.GetAsync("/health/capabilities/ai");
        capability.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = JsonSerializer.Deserialize<JsonElement>(await capability.Content.ReadAsStringAsync());
        payload.GetProperty("capability").GetString().Should().Be("ai");
        payload.GetProperty("runtimeStatus").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Ready_ShouldRejectMoreThanOneCompanyWithoutExposingCompanyData()
    {
        var unexpectedCode = $"unexpected-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OrgCompanies.Add(new OrgCompany
            {
                Code = unexpectedCode,
                Name = "不应泄露的公司名称",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var response = await _client.GetAsync("/health/ready");
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        raw.Should().Contain("singleCompany");
        raw.Should().Contain("expectedCount");
        raw.Should().NotContain("不应泄露的公司名称");

        using var cleanupScope = _factory.Services.CreateScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        cleanupDb.OrgCompanies.RemoveRange(cleanupDb.OrgCompanies.Where(company => company.Code == unexpectedCode));
        await cleanupDb.SaveChangesAsync();
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
