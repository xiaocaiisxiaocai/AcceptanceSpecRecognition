using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuditLogsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuditLogsTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAuditLogs_WhenPageSizeIsUnbounded_ShouldReturnBoundedPageContract()
    {
        var response = await _client.GetAsync("/api/audit-logs?page=1&pageSize=2147483647");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        body.Data!.PageSize.Should().Be(200);
        body.Data.Items.Should().HaveCountLessThanOrEqualTo(200);
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
            x.GetProperty("requestPath").GetString() == "/api/customers" &&
            x.GetProperty("clientTraceId").GetString() == "trace-test-1001");
    }

    [Fact]
    public async Task RequestTraceId_ShouldBeReturnedInResponseHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers?page=1&pageSize=1");
        request.Headers.Add("X-Client-Trace-Id", "trace-response-1001");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Trace-Id", out var values).Should().BeTrue();
        values.Should().Contain("trace-response-1001");
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
        var from = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ss");
        var to = DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss");

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

    [Fact]
    public async Task 已审计动作发生并发冲突时应记录最终409且不重放失败实体()
    {
        var traceId = $"audit-conflict-{Guid.NewGuid():N}";
        using var request = await BuildStaleAiConfigUpdateRequestAsync(_client, traceId);

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var audit = await FindAuditByTraceIdAsync(_client, traceId);
        audit.GetProperty("statusCode").GetInt32().Should().Be(409);
        audit.GetProperty("level").GetInt32().Should().Be((int)AuditLogLevel.Warning);
    }

    [Theory]
    [InlineData("argument", 500)]
    [InlineData("unauthorized", 401)]
    [InlineData("not-found", 404)]
    [InlineData("unexpected", 500)]
    public async Task 已审计动作抛出异常时应记录中间件映射后的最终状态(
        string exceptionKind,
        int expectedStatusCode)
    {
        var traceId = $"audit-exception-{expectedStatusCode}-{Guid.NewGuid():N}";
        using var baseFactory = new ApiWebApplicationFactory();
        using var exceptionFactory = CreateExceptionFactory(baseFactory, exceptionKind);
        using var client = exceptionFactory.CreateClient();
        using var request = AuthCookieTestHelper.CreateLoginRequest("audit-user", "audit-password");
        request.Headers.Add("X-Client-Trace-Id", traceId);

        using var response = await client.SendAsync(request);

        ((int)response.StatusCode).Should().Be(expectedStatusCode);
        var audit = await FindAuditByTraceIdAsync(client, traceId);
        audit.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
    }

    [Fact]
    public async Task 审计写入失败时不应覆盖并发冲突响应()
    {
        using var factory = new AuditWriteFailureApiWebApplicationFactory();
        using var client = factory.CreateClient();
        var traceId = $"audit-write-failure-conflict-{Guid.NewGuid():N}";
        using var request = await BuildStaleAiConfigUpdateRequestAsync(client, traceId);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("argument", 500)]
    [InlineData("unauthorized", 401)]
    [InlineData("not-found", 404)]
    [InlineData("unexpected", 500)]
    public async Task 审计写入失败时不应覆盖异常中间件生成的业务响应(
        string exceptionKind,
        int expectedStatusCode)
    {
        using var auditFailureFactory = new AuditWriteFailureApiWebApplicationFactory();
        using var exceptionFactory = CreateExceptionFactory(auditFailureFactory, exceptionKind);
        using var client = exceptionFactory.CreateClient();
        using var request = AuthCookieTestHelper.CreateLoginRequest("audit-user", "audit-password");
        request.Headers.Add(
            "X-Client-Trace-Id",
            $"audit-write-failure-{expectedStatusCode}-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        ((int)response.StatusCode).Should().Be(expectedStatusCode);
    }

    private static WebApplicationFactory<Program> CreateExceptionFactory(
        WebApplicationFactory<Program> factory,
        string exceptionKind)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthLoginAppService>();
                services.AddScoped<IAuthLoginAppService>(_ =>
                    new ThrowingAuthLoginAppService(CreateBusinessException(exceptionKind)));
            }));
    }

    private static Exception CreateBusinessException(string exceptionKind)
    {
        return exceptionKind switch
        {
            "argument" => new ArgumentException("模拟请求参数异常"),
            "unauthorized" => new UnauthorizedAccessException("模拟未授权访问"),
            "not-found" => new KeyNotFoundException("模拟业务资源不存在"),
            "unexpected" => new Exception("模拟未处理业务异常"),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionKind), exceptionKind, null)
        };
    }

    private static async Task<HttpRequestMessage> BuildStaleAiConfigUpdateRequestAsync(
        HttpClient client,
        string traceId)
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var createResponse = await client.PostAsync(
            "/api/ai-services",
            ApiClientJson.ToJsonContent(new
            {
                name = $"audit-row-version-{suffix}",
                serviceType = 2,
                purpose = 1,
                priority = 0,
                endpoint = "http://127.0.0.1:11434/api",
                apiKey = "",
                llmModel = "qwen3.5:35b"
            }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var id = created.Data.GetProperty("id").GetInt32();
        var staleRowVersion = created.Data.GetProperty("rowVersion").GetUInt32();
        var updatePayload = new
        {
            name = $"audit-row-version-updated-{suffix}",
            serviceType = 2,
            purpose = 1,
            priority = 1,
            endpoint = "http://127.0.0.1:11434/api",
            llmModel = "qwen3.5:35b",
            rowVersion = staleRowVersion
        };

        using var firstUpdate = await client.PutAsync(
            $"/api/ai-services/{id}",
            ApiClientJson.ToJsonContent(updatePayload));
        firstUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        var staleRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/ai-services/{id}")
        {
            Content = ApiClientJson.ToJsonContent(updatePayload)
        };
        staleRequest.Headers.Add("X-Client-Trace-Id", traceId);
        return staleRequest;
    }

    private static async Task<JsonElement> FindAuditByTraceIdAsync(HttpClient client, string traceId)
    {
        using var response = await client.GetAsync("/api/audit-logs?page=1&pageSize=200");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        var matches = body.Data!.Items
            .Where(item => item.GetProperty("clientTraceId").GetString() == traceId)
            .ToArray();
        matches.Should().ContainSingle();
        return matches[0];
    }

    private sealed class ThrowingAuthLoginAppService : IAuthLoginAppService
    {
        private readonly Exception _exception;

        public ThrowingAuthLoginAppService(Exception exception)
        {
            _exception = exception;
        }

        public Task<AuthLoginResult> AuthenticateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<AuthLoginResult>(_exception);
        }
    }
}
