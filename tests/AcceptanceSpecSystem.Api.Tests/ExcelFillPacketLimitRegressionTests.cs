using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ExcelFillPacketLimitRegressionTests : IClassFixture<PacketLimitedExcelApiWebApplicationFactory>
{
    private const int SimulatedPacketLimitBytes = 8_000_000;

    private readonly HttpClient _client;

    public ExcelFillPacketLimitRegressionTests(PacketLimitedExcelApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Execute_ForExcel_WithLargePreviewArchive_ShouldSucceedWithinSimulatedPacketLimit()
    {
        const int rowCount = 180;
        var excelBytes = ExcelFillFlowTests.CreateExcelBytes(BuildExcelRows(rowCount));

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(excelBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        uploadContent.Add(fileContent, "file", "packet-limit-regression.xlsx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", uploadContent);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var customerId = (await (await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name = "PacketLimit-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes", ApiClientJson.ToJsonContent(new { name = "PacketLimit-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var longAcceptance = BuildLongText("验收标准", 280);
        var longRemark = BuildLongText("备注信息", 280);
        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = longAcceptance,
            remark = longRemark
        }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var executeResp = await _client.PostAsync("/api/matching/batch-execute", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId,
            processId,
            config = new { minScoreThreshold = 0.0, highConfidenceThreshold = 0.95 },
            previewTables = new[]
            {
                new
                {
                    tableIndex = 0,
                    items = Enumerable.Range(1, rowCount)
                        .Select(rowIndex => BuildHeavyExactPreviewItem(rowIndex, specId, longAcceptance, longRemark))
                        .ToArray()
                }
            },
            tables = new[]
            {
                new
                {
                    tableIndex = 0,
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    mappings = Enumerable.Range(1, rowCount)
                        .Select(rowIndex => new
                        {
                            rowIndex,
                            specId
                        })
                        .ToArray()
                }
            }
        }));

        var executeBody = await executeResp.Content.ReadAsStringAsync();
        executeResp.StatusCode.Should().Be(HttpStatusCode.OK, executeBody);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        var taskId = executeJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();
        executeJson.Data.GetProperty("filledCount").GetInt32().Should().Be(rowCount);

        var listResp = await _client.GetAsync("/api/execution-history?page=1&pageSize=20");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        listJson.Code.Should().Be(0);

        var record = listJson.Data.GetProperty("items").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("taskId").GetString() == taskId);
        record.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        record.GetProperty("totalRowCount").GetInt32().Should().Be(rowCount);
        record.GetProperty("smartFillSummary").GetProperty("hasPlaybackArchive").GetBoolean().Should().BeFalse();

        var detailResp = await _client.GetAsync($"/api/execution-history/{record.GetProperty("id").GetInt32()}");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailJson = await detailResp.ReadAsAsync<ApiResponse<JsonElement>>();
        detailJson.Code.Should().Be(0);
        detailJson.Data.GetProperty("files").GetArrayLength().Should().Be(0, "大批量执行记录应降级为汇总归档");
        detailJson.Data.GetProperty("smartFillPlayback").GetProperty("isLegacy").GetBoolean().Should().BeTrue();
        detailJson.Data.GetProperty("smartFillPlayback").GetProperty("legacyMessage").GetString().Should().Contain("自动压缩");
    }

    private static string[][] BuildExcelRows(int rowCount)
    {
        var rows = new List<string[]>
        {
            new[] { "项目", "规格", "验收", "备注" }
        };

        rows.AddRange(Enumerable.Range(1, rowCount)
            .Select(_ => new[] { "P1", "S1", string.Empty, string.Empty }));
        return rows.ToArray();
    }

    private static object BuildHeavyExactPreviewItem(int rowIndex, int specId, string acceptance, string remark)
    {
        var longEvidence = BuildLongText($"证据{rowIndex}", 220);
        var longIssueMessage = BuildLongText($"问题{rowIndex}", 160);
        var longEntity = BuildLongText($"实体{rowIndex}", 160);

        return new
        {
            rowIndex,
            sourceProject = "P1",
            sourceSpecification = "S1",
            confidenceLevel = "high",
            bestMatch = new
            {
                specId,
                project = "P1",
                specification = "S1",
                acceptance,
                remark,
                score = 1.0,
                embeddingScore = 1.0,
                scoreDetails = new { exact = 1.0 },
                decision = "autoApply",
                selectionMode = "exactShortcut",
                selectionSummary = "项目与规格完全一致",
                evidenceSummary = new[] { longEvidence },
                conflictSummary = Array.Empty<string>(),
                issues = new[]
                {
                    new
                    {
                        code = "exact-hit",
                        severity = "info",
                        fieldName = "specification",
                        sourceValue = "S1",
                        candidateValue = "S1",
                        message = longIssueMessage,
                        suggestedAction = "无需处理"
                    }
                },
                entities = new[]
                {
                    new
                    {
                        entityType = "brand",
                        sourceValue = longEntity,
                        candidateValue = longEntity,
                        normalizedSourceValue = "p1",
                        normalizedCandidateValue = "p1",
                        relation = "exact"
                    }
                },
                topCandidates = new[]
                {
                    new
                    {
                        rank = 1,
                        specId,
                        project = "P1",
                        specification = "S1",
                        acceptance,
                        remark,
                        score = 1.0,
                        embeddingScore = 1.0,
                        scoreDetails = new { exact = 1.0 },
                        decision = "autoApply",
                        selectionMode = "exactShortcut",
                        selectionSummary = "完全一致",
                        evidenceSummary = new[] { longEvidence },
                        conflictSummary = Array.Empty<string>(),
                        issues = new[]
                        {
                            new
                            {
                                code = "exact-hit",
                                severity = "info",
                                fieldName = "specification",
                                sourceValue = "S1",
                                candidateValue = "S1",
                                message = longIssueMessage,
                                suggestedAction = "无需处理"
                            }
                        },
                        entities = new[]
                        {
                            new
                            {
                                entityType = "brand",
                                sourceValue = longEntity,
                                candidateValue = longEntity,
                                normalizedSourceValue = "p1",
                                normalizedCandidateValue = "p1",
                                relation = "exact"
                            }
                        }
                    }
                },
                recalledCandidateCount = 1,
                isAmbiguous = false
            }
        };
    }

    private static string BuildLongText(string seed, int repeatCount)
    {
        var builder = new StringBuilder(seed.Length * repeatCount + repeatCount);
        for (var index = 0; index < repeatCount; index++)
        {
            builder.Append(seed);
            builder.Append('段');
        }

        return builder.ToString();
    }
}

public sealed class PacketLimitedExcelApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IUnitOfWork));
            services.AddScoped<IUnitOfWork>(sp => new ThrowOnLargePendingJsonUnitOfWork(
                new UnitOfWork(sp.GetRequiredService<AppDbContext>(), sp),
                sp.GetRequiredService<AppDbContext>(),
                ExcelFillPacketLimitRegressionTests_Shim.SimulatedPacketLimitBytes));
        });
    }
}

internal static class ExcelFillPacketLimitRegressionTests_Shim
{
    internal const int SimulatedPacketLimitBytes = 8_000_000;
}

internal sealed class ThrowOnLargePendingJsonUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly AppDbContext _dbContext;
    private readonly int _maxPendingJsonBytes;

    public ThrowOnLargePendingJsonUnitOfWork(IUnitOfWork inner, AppDbContext dbContext, int maxPendingJsonBytes)
    {
        _inner = inner;
        _dbContext = dbContext;
        _maxPendingJsonBytes = maxPendingJsonBytes;
    }

    public ICustomerRepository Customers => _inner.Customers;
    public IProcessRepository Processes => _inner.Processes;
    public IMachineModelRepository MachineModels => _inner.MachineModels;
    public IAcceptanceSpecRepository AcceptanceSpecs => _inner.AcceptanceSpecs;
    public IEmbeddingCacheRepository EmbeddingCaches => _inner.EmbeddingCaches;
    public IWordFileRepository WordFiles => _inner.WordFiles;
    public IAiServiceConfigRepository AiServiceConfigs => _inner.AiServiceConfigs;
    public IPromptTemplateRepository PromptTemplates => _inner.PromptTemplates;
    public IColumnMappingRuleRepository ColumnMappingRules => _inner.ColumnMappingRules;
    public ISystemUserRepository SystemUsers => _inner.SystemUsers;
    public IAuditLogRepository AuditLogs => _inner.AuditLogs;
    public IMatchingFillTaskRepository MatchingFillTasks => _inner.MatchingFillTasks;
    public IExecutionHistoryRecordRepository ExecutionHistoryRecords => _inner.ExecutionHistoryRecords;

    public Task<int> SaveChangesAsync()
    {
        ThrowIfPendingJsonTooLarge();
        return _inner.SaveChangesAsync();
    }

    public int SaveChanges()
    {
        ThrowIfPendingJsonTooLarge();
        return _inner.SaveChanges();
    }

    public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();
    public Task CommitTransactionAsync() => _inner.CommitTransactionAsync();
    public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();
    public void Dispose() => _inner.Dispose();

    private void ThrowIfPendingJsonTooLarge()
    {
        var pendingJsonBytes = _dbContext.ChangeTracker.Entries()
            .Where(entry => entry.State is Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified)
            .Sum(entry => entry.Entity switch
            {
                MatchingFillTask task => Encoding.UTF8.GetByteCount(task.PayloadJson ?? string.Empty),
                ExecutionHistoryRecord record => Encoding.UTF8.GetByteCount(record.DetailJson ?? string.Empty),
                _ => 0
            });

        if (pendingJsonBytes > _maxPendingJsonBytes)
        {
            throw new InvalidOperationException(
                $"模拟数据库 packet 限制：待提交 JSON {pendingJsonBytes} bytes，超过 {_maxPendingJsonBytes} bytes");
        }
    }
}
