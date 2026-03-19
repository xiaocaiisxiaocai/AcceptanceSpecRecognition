using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuditLogsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuditLogsTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateCustomer_ShouldGenerateControllerAuditLog()
    {
        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        createReq.Headers.Add("X-Client-Trace-Id", "trace-test-1001");
        createReq.Headers.Add("X-Client-Id", "client-test-1001");
        createReq.Headers.Add("X-Frontend-Route", "/base-data/customers");
        createReq.Content = ApiClientJson.ToJsonContent(new
        {
            name = "审计测试客户_" + Guid.NewGuid().ToString("N")[..6]
        });

        using var createResp = await _client.SendAsync(createReq);
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResp = await _client.GetAsync("/api/audit-logs?page=1&pageSize=50&keyword=controller.create");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await listResp.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        body.Code.Should().Be(0);
        body.Data.Should().NotBeNull();

        body.Data!.Items.Should().Contain(x =>
            x.GetProperty("eventType").GetString() == "controller.create" &&
            x.GetProperty("requestMethod").GetString() == "POST" &&
            x.GetProperty("requestPath").GetString() == "/api/customers");
    }

    [Fact]
    public async Task QueryAction_ShouldNotGenerateAuditLog()
    {
        var queryResp = await _client.GetAsync("/api/customers?page=1&pageSize=1");
        queryResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var auditResp = await _client.GetAsync("/api/audit-logs?page=1&pageSize=50&requestMethod=GET&keyword=/api/customers");
        auditResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await auditResp.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        body.Code.Should().Be(0);
        body.Data.Should().NotBeNull();
        body.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteByRange_ShouldReturnSuccess()
    {
        var from = DateTime.Now.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss");
        var to = DateTime.Now.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss");

        var resp = await _client.DeleteAsync($"/api/audit-logs/range?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("deletedCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetAuditLogs_WhenRoleCommon_ShouldReturnForbidden()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/audit-logs?page=1&pageSize=20");
        req.Headers.Add("X-Test-Role", "common");

        using var resp = await _client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
