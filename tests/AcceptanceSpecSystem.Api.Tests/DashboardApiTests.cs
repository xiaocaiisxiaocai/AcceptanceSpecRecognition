using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class DashboardApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DashboardApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Summary_ShouldUseLast7DaysByDefault_AndExcludeBatchReply()
    {
        var now = DateTime.UtcNow;
        await SeedDashboardDataAsync(now);

        var response = await _client.GetAsync("/api/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var data = json.Data;
        data.GetProperty("periodPreset").GetString().Should().Be("last7");
        data.GetProperty("importedSpecCount").GetInt32().Should().Be(2);
        data.GetProperty("smartFillTaskCount").GetInt32().Should().Be(1);
        data.GetProperty("smartFillTotalRows").GetInt32().Should().Be(10);
        data.GetProperty("smartFillMatchedRows").GetInt32().Should().Be(6);
        data.GetProperty("smartFillAdoptedRows").GetInt32().Should().Be(6);
        data.GetProperty("matchingRate").GetDouble().Should().Be(0.6);
        data.GetProperty("adoptionRate").GetDouble().Should().Be(0.6);
    }

    [Fact]
    public async Task Summary_WithCustomPeriod_ShouldFilterByRequestedRange()
    {
        var now = DateTime.UtcNow;
        await SeedDashboardDataAsync(now);

        var from = Uri.EscapeDataString(now.AddDays(-20).ToString("O"));
        var to = Uri.EscapeDataString(now.AddDays(-10).ToString("O"));
        var response = await _client.GetAsync($"/api/dashboard/summary?range=custom&from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var data = json.Data;
        data.GetProperty("periodPreset").GetString().Should().Be("custom");
        data.GetProperty("importedSpecCount").GetInt32().Should().Be(1);
        data.GetProperty("smartFillTaskCount").GetInt32().Should().Be(1);
        data.GetProperty("smartFillTotalRows").GetInt32().Should().Be(5);
        data.GetProperty("smartFillMatchedRows").GetInt32().Should().Be(1);
        data.GetProperty("matchingRate").GetDouble().Should().Be(0.2);
    }

    [Fact]
    public async Task Summary_ShouldOnlyCountCompletedAndAdoptedSmartFillRows()
    {
        var now = DateTime.UtcNow;
        await SeedDashboardDataAsync(now);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ExecutionHistoryRecords.Add(CreateExecutionHistory(
                "smart-fill",
                now.AddDays(-1),
                totalRows: 20,
                matchedRows: 20,
                adoptedRows: 0,
                detailJson: ""));
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var data = json.Data;
        data.GetProperty("smartFillTaskCount").GetInt32().Should().Be(1);
        data.GetProperty("smartFillTotalRows").GetInt32().Should().Be(10);
        data.GetProperty("smartFillMatchedRows").GetInt32().Should().Be(6);
        data.GetProperty("matchingRate").GetDouble().Should().Be(0.6);
    }

    private async Task SeedDashboardDataAsync(DateTime now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ExecutionHistoryRecords.RemoveRange(db.ExecutionHistoryRecords.Where(record => record.SourceFileName.StartsWith("dashboard-test-")));
        db.AcceptanceSpecs.RemoveRange(db.AcceptanceSpecs.Where(spec => spec.Project.StartsWith("Dashboard-")));
        await db.SaveChangesAsync();

        var customer = new Customer { Name = $"首页客户-{Guid.NewGuid():N}", CreatedAt = now };
        var process = new Data.Entities.Process { Name = $"首页制程-{Guid.NewGuid():N}", CreatedAt = now };
        db.Customers.Add(customer);
        db.Processes.Add(process);
        await db.SaveChangesAsync();

        var wordFile = new WordFile
        {
            FileName = $"dashboard-{Guid.NewGuid():N}.docx",
            FilePath = "dashboard.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.WordDocx,
            FileContent = [1],
            CreatedByUserId = 1,
            CompanyId = 1,
            OwnerOrgUnitId = 1,
            UploadedAt = now
        };
        db.WordFiles.Add(wordFile);
        await db.SaveChangesAsync();

        db.AcceptanceSpecs.AddRange(
            CreateSpec(customer.Id, process.Id, wordFile.Id, "Dashboard-7-1", now.AddDays(-1)),
            CreateSpec(customer.Id, process.Id, wordFile.Id, "Dashboard-7-2", now.AddDays(-6)),
            CreateSpec(customer.Id, process.Id, wordFile.Id, "Dashboard-30", now.AddDays(-15)),
            CreateSpec(customer.Id, process.Id, wordFile.Id, "Dashboard-old", now.AddDays(-40)));

        db.ExecutionHistoryRecords.AddRange(
            CreateExecutionHistory("smart-fill", now.AddDays(-1), totalRows: 10, matchedRows: 8, adoptedRows: 6),
            CreateExecutionHistory("smart-fill", now.AddDays(-15), totalRows: 5, matchedRows: 2, adoptedRows: 1),
            CreateExecutionHistory("batch-reply", now.AddDays(-1), totalRows: 99, matchedRows: 99, adoptedRows: 99));

        await db.SaveChangesAsync();
    }

    private static AcceptanceSpec CreateSpec(
        int customerId,
        int processId,
        int wordFileId,
        string project,
        DateTime importedAt)
    {
        return new AcceptanceSpec
        {
            CustomerId = customerId,
            ProcessId = processId,
            Project = project,
            Specification = $"规格-{project}",
            Acceptance = "OK",
            Remark = "备注",
            WordFileId = wordFileId,
            CreatedByUserId = 1,
            OwnerOrgUnitId = 1,
            ImportedAt = importedAt
        };
    }

    private static ExecutionHistoryRecord CreateExecutionHistory(
        string taskType,
        DateTime createdAt,
        int totalRows,
        int matchedRows,
        int adoptedRows,
        string detailJson = "{}")
    {
        return new ExecutionHistoryRecord
        {
            TaskId = Guid.NewGuid().ToString("N"),
            TaskType = taskType,
            SourceFileName = $"dashboard-test-{taskType}.docx",
            SourceFileType = UploadedFileType.WordDocx,
            FileCount = 1,
            TotalRowCount = totalRows,
            MatchedRowCount = matchedRows,
            AdoptedRowCount = adoptedRows,
            UnmatchedRowCount = Math.Max(0, totalRows - matchedRows),
            SkippedRowCount = 0,
            NotAdoptedRowCount = Math.Max(0, matchedRows - adoptedRows),
            ManualSelectedRowCount = 0,
            DetailJson = detailJson,
            CreatedByUserId = 1,
            CompanyId = 1,
            CreatedAt = createdAt
        };
    }
}
