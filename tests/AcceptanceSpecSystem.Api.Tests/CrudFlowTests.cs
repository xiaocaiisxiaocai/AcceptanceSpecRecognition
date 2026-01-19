using System.Net;
using System.Text.Json;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

public class CrudFlowTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CrudFlowTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Customer_Process_Spec_Crud_ShouldWork()
    {
        // create customer
        var createCustomerResp = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = "TestCustomer" }));
        createCustomerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var customer = await createCustomerResp.ReadAsAsync<ApiResponse<JsonElement>>();
        customer.Code.Should().Be(0);
        customer.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var customerId = customer.Data.GetProperty("id").GetInt32();
        customerId.Should().BeGreaterThan(0);

        // create process
        var createProcessResp = await _client.PostAsync(
            "/api/processes",
            ApiClientJson.ToJsonContent(new { name = "TestProcess" }));
        createProcessResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var process = await createProcessResp.ReadAsAsync<ApiResponse<JsonElement>>();
        process.Code.Should().Be(0);
        process.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var processId = process.Data.GetProperty("id").GetInt32();
        processId.Should().BeGreaterThan(0);

        // create spec
        var createSpecResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "P1",
                specification = "S1",
                acceptance = "OK",
                remark = "R1"
            }));
        createSpecResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec = await createSpecResp.ReadAsAsync<ApiResponse<JsonElement>>();
        spec.Code.Should().Be(0);
        spec.Data.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        spec.Data.GetProperty("id").GetInt32().Should().BeGreaterThan(0);

        // list specs
        var listSpecsResp = await _client.GetAsync($"/api/specs?page=1&pageSize=10&customerId={customerId}&processId={processId}");
        listSpecsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listSpecsResp.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        list.Code.Should().Be(0);
        list.Data!.Total.Should().BeGreaterThanOrEqualTo(1);
    }
}

