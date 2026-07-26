using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

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

    [Fact]
    public async Task ReferenceDataDelete_WithRelatedSpec_ShouldReturnConflictAndKeepSpec()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customerId = await CreateReferenceDataAsync("customers", $"delete-customer-{suffix}");
        var processId = await CreateReferenceDataAsync("processes", $"delete-process-{suffix}");
        var machineModelId = await CreateReferenceDataAsync("machine-models", $"delete-model-{suffix}");

        var createSpecResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                machineModelId,
                project = "delete-protection",
                specification = "must remain",
                acceptance = "OK"
            }));
        createSpecResp.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.DeleteAsync($"/api/customers/{customerId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.DeleteAsync($"/api/processes/{processId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.DeleteAsync($"/api/machine-models/{machineModelId}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var listResp = await _client.GetAsync($"/api/specs?page=1&pageSize=10&customerId={customerId}");
        var list = await listResp.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        list.Data!.Total.Should().Be(1);

        var customerResp = await _client.GetAsync($"/api/customers/{customerId}");
        var customer = await customerResp.ReadAsAsync<ApiResponse<JsonElement>>();
        customer.Data.GetProperty("specCount").GetInt32().Should().Be(1);
    }

    private async Task<int> CreateReferenceDataAsync(string resource, string name)
    {
        var response = await _client.PostAsync(
            $"/api/{resource}",
            ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return payload.Data.GetProperty("id").GetInt32();
    }
}
