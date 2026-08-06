using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecReferenceCountTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AcceptanceSpecReferenceCountTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SuccessfulFill_ShouldCountEachAdoptedRow_AndReplayShouldNotCountAgain()
    {
        var (customerId, processId) = await CreateBusinessScopeAsync("ReferenceCount");
        var specId = await CreateSpecAsync(
            customerId,
            processId,
            "重复项目",
            "重复规格",
            "验收内容",
            "备注内容");
        var fileId = await UploadExcelAsync(
        [
            ["项目", "规格", "验收", "备注"],
            ["重复项目", "重复规格", "", ""],
            ["重复项目", "重复规格", "", ""],
            ["重复项目", "重复规格", "", ""],
            ["重复项目", "重复规格", "", ""],
            ["重复项目", "重复规格", "", ""]
        ]);
        var executionRequestId = Guid.NewGuid().ToString("N");
        var payload = await BuildExecutePayloadAsync(
            fileId,
            customerId,
            processId,
            executionRequestId);

        var first = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(payload));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.ReadAsAsync<ApiResponse<JsonElement>>();
        firstBody.Data.GetProperty("filledCount").GetInt32().Should().Be(5);

        (await GetReferenceCountAsync(specId)).Should().Be(5);

        var replay = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(payload));
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetReferenceCountAsync(specId)).Should().Be(5);

        var detail = await _client.GetAsync($"/api/specs/{specId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await detail.ReadAsAsync<ApiResponse<JsonElement>>();
        detailBody.Data.GetProperty("referenceCount").GetInt64().Should().Be(5);
        detailBody.Data.GetProperty("referenceVersion").GetInt64().Should().Be(1);

        var list = await _client.GetAsync(
            $"/api/specs?page=1&pageSize=100&customerId={customerId}&processId={processId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        listBody.Data!.Items
            .Single(item => item.GetProperty("id").GetInt32() == specId)
            .GetProperty("referenceCount")
            .GetInt64()
            .Should()
            .Be(5);
        listBody.Data.Items
            .Single(item => item.GetProperty("id").GetInt32() == specId)
            .GetProperty("referenceVersion")
            .GetInt64()
            .Should()
            .Be(1);

        var history = await _client.GetAsync(
            $"/api/specs/{specId}/reference-history?page=1&pageSize=20&sort=oldest");
        history.StatusCode.Should().Be(HttpStatusCode.OK);
        var historyBody = await history.ReadAsAsync<ApiResponse<JsonElement>>();
        historyBody.Data.GetProperty("currentReferenceCount").GetInt64().Should().Be(5);
        historyBody.Data.GetProperty("recordedReferenceCount").GetInt64().Should().Be(5);
        historyBody.Data.GetProperty("untrackedReferenceCount").GetInt64().Should().Be(0);
        historyBody.Data.GetProperty("total").GetInt32().Should().Be(5);
        var historyItems = historyBody.Data.GetProperty("items").EnumerateArray().ToArray();
        historyItems.Select(item => item.GetProperty("referenceOrdinal").GetInt64())
            .Should().Equal(1, 2, 3, 4, 5);
        historyItems.Select(item => item.GetProperty("referencedAtUtc").GetDateTime())
            .Distinct()
            .Should().ContainSingle("同一执行中的逐次引用应共享成功提交时间");

        var replayHistory = await _client.GetAsync(
            $"/api/specs/{specId}/reference-history?page=1&pageSize=20&sort=oldest");
        var replayHistoryBody = await replayHistory.ReadAsAsync<ApiResponse<JsonElement>>();
        replayHistoryBody.Data.GetProperty("total").GetInt32().Should().Be(5);

        var update = await _client.PutAsync(
            $"/api/specs/{specId}",
            ApiClientJson.ToJsonContent(new
            {
                project = "重复项目-新版本",
                specification = "重复规格",
                acceptance = "验收内容",
                remark = "备注内容"
            }));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentVersionHistory = await _client.GetAsync(
            $"/api/specs/{specId}/reference-history?page=1&pageSize=20&sort=oldest");
        var currentVersionBody = await currentVersionHistory.ReadAsAsync<ApiResponse<JsonElement>>();
        currentVersionBody.Data.GetProperty("currentReferenceVersion").GetInt64().Should().Be(2);
        currentVersionBody.Data.GetProperty("currentReferenceCount").GetInt64().Should().Be(0);
        currentVersionBody.Data.GetProperty("total").GetInt32().Should().Be(0);

        var updatedDetail = await _client.GetAsync($"/api/specs/{specId}");
        var updatedDetailBody = await updatedDetail.ReadAsAsync<ApiResponse<JsonElement>>();
        updatedDetailBody.Data.GetProperty("referenceCount").GetInt64().Should().Be(0);
        updatedDetailBody.Data.GetProperty("referenceVersion").GetInt64().Should().Be(2);

        var allVersionHistory = await _client.GetAsync(
            $"/api/specs/{specId}/reference-history?page=1&pageSize=20&sort=oldest&includePreviousVersions=true");
        var allVersionBody = await allVersionHistory.ReadAsAsync<ApiResponse<JsonElement>>();
        allVersionBody.Data.GetProperty("total").GetInt32().Should().Be(5);
        allVersionBody.Data.GetProperty("recordedReferenceCount").GetInt64().Should().Be(5);
        allVersionBody.Data.GetProperty("untrackedReferenceCount").GetInt64().Should().Be(0);
        allVersionBody.Data.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(item =>
                item.GetProperty("referenceVersion").GetInt64() == 1 &&
                !item.GetProperty("isCurrentVersion").GetBoolean());
    }

    [Fact]
    public async Task ReferenceHistory_ShouldValidateSortAndMissingSpec()
    {
        var invalidSort = await _client.GetAsync(
            "/api/specs/1/reference-history?page=1&pageSize=20&sort=sideways");
        invalidSort.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missing = await _client.GetAsync(
            "/api/specs/2147483647/reference-history?page=1&pageSize=20&sort=oldest");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SuccessfulFill_WhenAcceptanceAndRemarkAreBothBlank_ShouldNotCount()
    {
        var (customerId, processId) = await CreateBusinessScopeAsync("BlankReference");
        var specId = await CreateSpecAsync(
            customerId,
            processId,
            "空内容项目",
            "空内容规格",
            "",
            "   ");
        var fileId = await UploadExcelAsync(
        [
            ["项目", "规格", "验收", "备注"],
            ["空内容项目", "空内容规格", "", ""]
        ]);
        var payload = await BuildExecutePayloadAsync(
            fileId,
            customerId,
            processId,
            Guid.NewGuid().ToString("N"));

        var response = await _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(payload));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetReferenceCountAsync(specId)).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentSuccessfulFills_ShouldNotLoseReferenceIncrements()
    {
        var (customerId, processId) = await CreateBusinessScopeAsync("ConcurrentReference");
        var specId = await CreateSpecAsync(
            customerId,
            processId,
            "并发项目",
            "并发规格",
            "并发验收",
            null);
        var rows = new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "并发项目", "并发规格", "", "" }
        };
        var firstFileId = await UploadExcelAsync(rows);
        var secondFileId = await UploadExcelAsync(rows);
        var firstPayload = await BuildExecutePayloadAsync(
            firstFileId,
            customerId,
            processId,
            Guid.NewGuid().ToString("N"));
        var secondPayload = await BuildExecutePayloadAsync(
            secondFileId,
            customerId,
            processId,
            Guid.NewGuid().ToString("N"));

        var firstExecution = _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(firstPayload));
        var secondExecution = _client.PostAsync(
            "/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(secondPayload));
        await Task.WhenAll(firstExecution, secondExecution);

        (await firstExecution).StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondExecution).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetReferenceCountAsync(specId)).Should().Be(2);
    }

    [Fact]
    public async Task Update_ShouldPreserveCountForIdenticalContent_AndResetForChangedContent()
    {
        var (customerId, processId) = await CreateBusinessScopeAsync("UpdateReference");
        var specId = await CreateSpecAsync(
            customerId,
            processId,
            "原项目",
            "原规格",
            "原验收",
            "原备注");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var spec = await db.AcceptanceSpecs.SingleAsync(item => item.Id == specId);
            spec.ReferenceCount = 7;
            db.AcceptanceSpecReferenceEvents.Add(new AcceptanceSpecReferenceEvent
            {
                AcceptanceSpecId = specId,
                ReferenceVersion = 1,
                OccurrenceCount = 7,
                ReferencedAtUtc = null
            });
            await db.SaveChangesAsync();
        }

        var identical = await _client.PutAsync(
            $"/api/specs/{specId}",
            ApiClientJson.ToJsonContent(new
            {
                project = "原项目",
                specification = "原规格",
                acceptance = "原验收",
                remark = "原备注"
            }));
        identical.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetReferenceCountAsync(specId)).Should().Be(7);

        var changed = await _client.PutAsync(
            $"/api/specs/{specId}",
            ApiClientJson.ToJsonContent(new
            {
                project = "新项目",
                specification = "原规格",
                acceptance = "原验收",
                remark = "原备注"
            }));
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetReferenceCountAsync(specId)).Should().Be(0);

        var allVersions = await _client.GetAsync(
            $"/api/specs/{specId}/reference-history?page=1&pageSize=20&sort=oldest&includePreviousVersions=true");
        var allVersionsBody = await allVersions.ReadAsAsync<ApiResponse<JsonElement>>();
        allVersionsBody.Data.GetProperty("currentReferenceVersion").GetInt64().Should().Be(2);
        allVersionsBody.Data.GetProperty("currentReferenceCount").GetInt64().Should().Be(0);
        allVersionsBody.Data.GetProperty("recordedReferenceCount").GetInt64().Should().Be(0);
        allVersionsBody.Data.GetProperty("untrackedReferenceCount").GetInt64().Should().Be(7);
        allVersionsBody.Data.GetProperty("total").GetInt32().Should().Be(0);
    }

    private async Task<(int CustomerId, int ProcessId)> CreateBusinessScopeAsync(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var customerResponse = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"{prefix}-C-{suffix}" }));
        var processResponse = await _client.PostAsync(
            "/api/processes",
            ApiClientJson.ToJsonContent(new { name = $"{prefix}-P-{suffix}" }));

        var customer = await customerResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var process = await processResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        return (
            customer.Data.GetProperty("id").GetInt32(),
            process.Data.GetProperty("id").GetInt32());
    }

    private async Task<int> CreateSpecAsync(
        int customerId,
        int processId,
        string project,
        string specification,
        string? acceptance,
        string? remark)
    {
        var businessOrgUnitId = await GetBusinessOrgUnitIdAsync();

        var response = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId,
                customerId,
                processId,
                project,
                specification,
                acceptance,
                remark
            }));
        var rawResponse = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, rawResponse);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("referenceCount").GetInt64().Should().Be(0);
        return body.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> UploadExcelAsync(string[][] rows)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(ExcelFillFlowTests.CreateExcelBytes(rows));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", $"reference-count-{Guid.NewGuid():N}.xlsx");
        content.Add(
            new StringContent((await GetBusinessOrgUnitIdAsync()).ToString()),
            "businessOrgUnitId");

        var response = await _client.PostAsync("/api/documents/upload", content);
        var rawResponse = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, rawResponse);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<object> BuildExecutePayloadAsync(
        int fileId,
        int customerId,
        int processId,
        string executionRequestId)
    {
        var config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 };
        var previewResponse = await _client.PostAsync(
            "/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3
                    }
                }
            }));
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewBody = await previewResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var mappings = previewBody.Data
            .GetProperty("tables")[0]
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => new
            {
                rowIndex = item.GetProperty("rowIndex").GetInt32(),
                specId = item.GetProperty("bestMatch").GetProperty("specId").GetInt32()
            })
            .ToArray();

        return new
        {
            executionRequestId,
            fileId,
            customerId,
            processId,
            config,
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings
                }
            }
        };
    }

    private async Task<long> GetReferenceCountAsync(int specId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AcceptanceSpecs
            .AsNoTracking()
            .Where(spec => spec.Id == specId)
            .Select(spec => spec.ReferenceCount)
            .SingleAsync();
    }

    private async Task<int> GetBusinessOrgUnitIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OrgUnits
            .Where(orgUnit =>
                orgUnit.ParentId != null &&
                !db.OrgUnits.Any(child => child.ParentId == orgUnit.Id))
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();
    }
}
