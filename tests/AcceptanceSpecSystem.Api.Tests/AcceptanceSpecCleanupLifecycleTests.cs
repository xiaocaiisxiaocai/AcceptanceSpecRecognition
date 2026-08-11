using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecCleanupLifecycleTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AcceptanceSpecCleanupLifecycleTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ScanQuarantineAndRestore_ShouldHideThenRestoreSpec()
    {
        var specId = await SeedOldUnusedSpecAsync("隔离恢复");
        var (scanId, scanItemId) = await StartAndCompleteScanAsync(specId);

        var quarantine = await _client.PostAsync(
            "/api/spec-cleanup/items/quarantine",
            ApiClientJson.ToJsonContent(new[] { new { scanItemId, reason = "确认清理" } }));
        var quarantineRaw = await quarantine.Content.ReadAsStringAsync();
        quarantine.StatusCode.Should().Be(HttpStatusCode.OK, quarantineRaw);
        var quarantineBody = await quarantine.ReadAsAsync<ApiResponse<JsonElement>>();
        quarantineBody.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        (await _client.GetAsync($"/api/specs/{specId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var quarantineList = await _client.GetAsync("/api/spec-cleanup/quarantine?page=1&pageSize=20");
        quarantineList.StatusCode.Should().Be(HttpStatusCode.OK);
        var quarantineListBody = await quarantineList.ReadAsAsync<ApiResponse<JsonElement>>();
        var quarantinedItem = quarantineListBody.Data.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == specId);
        quarantinedItem.GetProperty("acceptance").GetString().Should().Be("验收正文");
        quarantinedItem.GetProperty("remark").GetString().Should().Be("备注正文");

        var restore = await _client.PostAsync(
            "/api/spec-cleanup/quarantine/restore",
            ApiClientJson.ToJsonContent(new { specIds = new[] { specId } }));
        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/specs/{specId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        scanId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PermanentDelete_ShouldEnforceExpiryAndKeepBodyFreeTombstone()
    {
        var specId = await SeedOldUnusedSpecAsync("永久删除");
        var (_, scanItemId) = await StartAndCompleteScanAsync(specId);
        await _client.PostAsync(
            "/api/spec-cleanup/items/quarantine",
            ApiClientJson.ToJsonContent(new[] { new { scanItemId } }));

        long referenceVersion;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var spec = await db.AcceptanceSpecs.IgnoreQueryFilters().SingleAsync(item => item.Id == specId);
            referenceVersion = spec.ReferenceVersion;
        }

        var beforeExpiry = await PermanentlyDeleteAsync(specId, referenceVersion, confirm: true);
        var beforeExpiryBody = await beforeExpiry.ReadAsAsync<ApiResponse<JsonElement>>();
        beforeExpiryBody.Data.GetProperty("failedCount").GetInt32().Should().Be(1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var spec = await db.AcceptanceSpecs.IgnoreQueryFilters().SingleAsync(item => item.Id == specId);
            spec.QuarantineExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var deleted = await PermanentlyDeleteAsync(specId, referenceVersion, confirm: true);
        var deletedBody = await deleted.ReadAsAsync<ApiResponse<JsonElement>>();
        deletedBody.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AcceptanceSpecs.IgnoreQueryFilters().AnyAsync(item => item.Id == specId)).Should().BeFalse();
            var tombstone = await db.AcceptanceSpecCleanupDeletionRecords
                .SingleAsync(item => item.OriginalAcceptanceSpecId == specId);
            tombstone.CompanyId.Should().Be(1);
            tombstone.GetType().GetProperties().Select(property => property.Name)
                .Should().NotContain(new[] { "Project", "Specification", "Acceptance", "Remark" });
        }
    }

    [Fact]
    public async Task StartScan_WithoutScanPermission_ShouldReturnForbidden()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/spec-cleanup/scans")
        {
            Content = ApiClientJson.ToJsonContent(new { newItemGraceDays = 30, unusedDays = 365 })
        };
        request.Headers.Add("X-Test-Permissions", "api:spec-cleanup:read");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IgnoreAndUnignore_ShouldControlFutureScanEligibility()
    {
        var specId = await SeedOldUnusedSpecAsync("忽略恢复");
        var (_, scanItemId) = await StartAndCompleteScanAsync(specId);
        var ignored = await _client.PostAsync(
            "/api/spec-cleanup/items/ignore",
            ApiClientJson.ToJsonContent(new[] { new { scanItemId, reason = "业务保留" } }));
        ignored.StatusCode.Should().Be(HttpStatusCode.OK);

        var ignoredList = await _client.GetAsync("/api/spec-cleanup/ignored?page=1&pageSize=100");
        var ignoredBody = await ignoredList.ReadAsAsync<ApiResponse<JsonElement>>();
        var ignoredItem = ignoredBody.Data.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == specId);
        ignoredItem.GetProperty("acceptance").GetString().Should().Be("验收正文");
        ignoredItem.GetProperty("remark").GetString().Should().Be("备注正文");

        var restored = await _client.PostAsync(
            "/api/spec-cleanup/ignored/restore",
            ApiClientJson.ToJsonContent(new { specIds = new[] { specId } }));
        var restoredBody = await restored.ReadAsAsync<ApiResponse<JsonElement>>();
        restoredBody.Data.GetProperty("successCount").GetInt32().Should().Be(1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.AcceptanceSpecs.SingleAsync(item => item.Id == specId))
            .CleanupScanIgnored.Should().BeFalse();
    }

    [Fact]
    public async Task MaterialContentChange_ShouldAutomaticallyClearScanIgnore()
    {
        var specId = await SeedOldUnusedSpecAsync("内容变化解除忽略");
        var (_, scanItemId) = await StartAndCompleteScanAsync(specId);
        var ignored = await _client.PostAsync(
            "/api/spec-cleanup/items/ignore",
            ApiClientJson.ToJsonContent(new[] { new { scanItemId, reason = "暂时保留" } }));
        ignored.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var coordinator = scope.ServiceProvider
            .GetRequiredService<AcceptanceSpecContentVersionCoordinator>();
        var spec = await db.AcceptanceSpecs.SingleAsync(item => item.Id == specId);
        spec.CleanupScanIgnored.Should().BeTrue();

        var changed = await coordinator.ApplyChangeAsync(
            spec,
            spec.Project,
            $"{spec.Specification}-已更新",
            spec.Acceptance,
            spec.Remark,
            "test",
            changedByUserId: 1);
        await db.SaveChangesAsync();

        changed.Should().BeTrue();
        spec.CleanupScanIgnored.Should().BeFalse();
        spec.CleanupScanIgnoredAtUtc.Should().BeNull();
        spec.CleanupScanIgnoredByUserId.Should().BeNull();
        spec.CleanupScanIgnoreReason.Should().BeNull();
    }

    private async Task<(string ScanId, long ScanItemId)> StartAndCompleteScanAsync(int specId)
    {
        var start = await _client.PostAsync(
            "/api/spec-cleanup/scans",
            ApiClientJson.ToJsonContent(new { newItemGraceDays = 30, unusedDays = 365 }));
        var startRaw = await start.Content.ReadAsStringAsync();
        start.StatusCode.Should().Be(HttpStatusCode.OK, startRaw);
        var startBody = await start.ReadAsAsync<ApiResponse<JsonElement>>();
        var scanId = startBody.Data.GetProperty("id").GetString()!;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAcceptanceSpecCleanupAppService>();
            await service.ProcessNextScanBatchAsync();
            var status = await _client.GetAsync($"/api/spec-cleanup/scans/{scanId}");
            var statusBody = await status.ReadAsAsync<ApiResponse<JsonElement>>();
            if (statusBody.Data.GetProperty("status").GetInt32() ==
                (int)AcceptanceSpecCleanupScanStatus.Completed)
                break;
        }

        var itemsResponse = await _client.GetAsync(
            $"/api/spec-cleanup/scans/{scanId}/items?category=1&page=1&pageSize=100");
        var itemsRaw = await itemsResponse.Content.ReadAsStringAsync();
        itemsResponse.StatusCode.Should().Be(HttpStatusCode.OK, itemsRaw);
        var itemsBody = await itemsResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = itemsBody.Data.GetProperty("items").EnumerateArray()
            .Single(value => value.GetProperty("acceptanceSpecId").GetInt32() == specId);
        item.GetProperty("acceptance").GetString().Should().Be("验收正文");
        item.GetProperty("remark").GetString().Should().Be("备注正文");
        return (scanId, item.GetProperty("id").GetInt64());
    }

    private Task<HttpResponseMessage> PermanentlyDeleteAsync(int specId, long referenceVersion, bool confirm) =>
        _client.PostAsync(
            "/api/spec-cleanup/quarantine/permanent-delete",
            ApiClientJson.ToJsonContent(new
            {
                items = new[] { new { specId, referenceVersion } },
                confirmPermanentDelete = confirm
            }));

    private async Task<int> SeedOldUnusedSpecAsync(string label)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var customer = new Customer { Name = $"Cleanup-{label}-{suffix}" };
        var wordFile = new WordFile
        {
            CompanyId = 1,
            CreatedByUserId = 1,
            FileName = $"cleanup-{suffix}.docx",
            FileHash = suffix.PadRight(64, '0')[..64],
            FileContent = []
        };
        db.AddRange(customer, wordFile);
        await db.SaveChangesAsync();
        var importedAt = DateTime.UtcNow.AddDays(-500);
        var spec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            WordFileId = wordFile.Id,
            CreatedByUserId = 1,
            Project = $"{label}项目",
            Specification = $"{label}规格",
            Acceptance = "验收正文",
            Remark = "备注正文",
            ImportedAt = importedAt,
            ReferenceVersion = 1
        };
        db.AcceptanceSpecs.Add(spec);
        await db.SaveChangesAsync();
        db.AcceptanceSpecContentVersions.Add(new AcceptanceSpecContentVersion
        {
            AcceptanceSpecId = spec.Id,
            Version = 1,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ChangedAtUtc = importedAt,
            ChangeSource = "test"
        });
        await db.SaveChangesAsync();
        return spec.Id;
    }
}
