using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingTaskOwnershipTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MatchingTaskOwnershipTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(false, "completed", true)]
    [InlineData(true, "running", false)]
    public async Task Status_WhenTaskIsOwned_ShouldReturnOnlySafeSnapshotFields(
        bool fileMutationPending,
        string expectedStatus,
        bool expectedCanDownload)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var snapshotTime = new DateTime(2026, 7, 27, 1, 2, 3, DateTimeKind.Utc);
        await SeedStatusTaskAsync(
            taskId,
            createdByUserId: 1,
            companyId: 1,
            fileMutationPending,
            snapshotTime);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/matching/tasks/{taskId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(@"C:\sensitive\result.xlsx");
        var body = JsonSerializer.Deserialize<
            AcceptanceSpecSystem.Api.Models.ApiResponse<JsonElement>>(
            raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        body.Should().NotBeNull();
        body!.Code.Should().Be(0);
        var data = body.Data;
        data.GetProperty("taskId").GetString().Should().Be(taskId);
        data.GetProperty("status").GetString().Should().Be(expectedStatus);
        data.GetProperty("canDownload").GetBoolean().Should().Be(expectedCanDownload);
        data.GetProperty("updatedAt").GetDateTime().Should().Be(snapshotTime);
        data.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .BeEquivalentTo(["taskId", "status", "canDownload", "updatedAt"]);
        data.EnumerateObject().Should().HaveCount(4);
    }

    [Theory]
    [InlineData("2", "1")]
    [InlineData("1", "2")]
    public async Task Status_WhenTaskBelongsToAnotherOwner_ShouldReturnNotFound(
        string userId,
        string companyId)
    {
        var taskId = Guid.NewGuid().ToString("N");
        await SeedStatusTaskAsync(
            taskId,
            createdByUserId: 1,
            companyId: 1,
            fileMutationPending: false,
            DateTime.UtcNow);

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/matching/tasks/{taskId}/status");
        request.Headers.Add("X-Test-User-Id", userId);
        request.Headers.Add("X-Test-Company-Id", companyId);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.ReadAsAsync<ApiResponse>();
        body.Code.Should().Be(404);
        body.Message.Should().Be("任务不存在或已过期");
    }

    [Fact]
    public async Task Status_WhenTaskDoesNotExist_ShouldReturnNotFound()
    {
        using var client = _factory.CreateClient();
        var taskId = Guid.NewGuid().ToString("N");

        var response = await client.GetAsync($"/api/matching/tasks/{taskId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.ReadAsAsync<ApiResponse>();
        body.Code.Should().Be(404);
        body.Message.Should().Be("任务不存在或已过期");
    }

    [Fact]
    public async Task Status_WhenOwnedTaskPayloadIsInvalid_ShouldReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sourceFile = new WordFile
            {
                FileName = "invalid-status-source.xlsx",
                FileType = UploadedFileType.ExcelXlsx,
                FileContent = "invalid-status-source"u8.ToArray(),
                FileHash = "invalid-status-hash",
                CompanyId = 1,
                CreatedByUserId = 1,
                UploadedAt = DateTime.UtcNow
            };
            db.WordFiles.Add(sourceFile);
            await db.SaveChangesAsync();
            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = taskId,
                SourceFileId = sourceFile.Id,
                CreatedByUserId = 1,
                CompanyId = 1,
                PayloadJson = "{invalid-json",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/matching/tasks/{taskId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.ReadAsAsync<ApiResponse>();
        body.Code.Should().Be(404);
        body.Message.Should().Be("任务不存在或已过期");
    }

    [Fact]
    public async Task Status_WhenOwnedTaskPayloadIsEmptyObject_ShouldReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var original = await SeedRawStatusTaskAsync(taskId, _ => "{}");

        await AssertStatusNotFoundAndTaskUnchangedAsync(taskId, original);
    }

    [Fact]
    public async Task Status_WhenPayloadTaskIdDoesNotMatchEntity_ShouldReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var original = await SeedRawStatusTaskAsync(
            taskId,
            sourceFileId => JsonSerializer.Serialize(new
            {
                payloadVersion = 4,
                taskId = Guid.NewGuid().ToString("N"),
                sourceFileId,
                createdAt = DateTime.UtcNow
            }));

        await AssertStatusNotFoundAndTaskUnchangedAsync(taskId, original);
    }

    [Fact]
    public async Task Status_WhenPayloadSourceFileIdIsZero_ShouldReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var original = await SeedRawStatusTaskAsync(
            taskId,
            _ => JsonSerializer.Serialize(new
            {
                payloadVersion = 4,
                taskId,
                sourceFileId = 0,
                createdAt = DateTime.UtcNow
            }));

        await AssertStatusNotFoundAndTaskUnchangedAsync(taskId, original);
    }

    [Fact]
    public async Task Status_WhenPayloadSourceFileIdDoesNotMatchEntity_ShouldReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var original = await SeedRawStatusTaskAsync(
            taskId,
            sourceFileId => JsonSerializer.Serialize(new
            {
                payloadVersion = 4,
                taskId,
                sourceFileId = sourceFileId + 1,
                createdAt = DateTime.UtcNow
            }));

        await AssertStatusNotFoundAndTaskUnchangedAsync(taskId, original);
    }

    [Fact]
    public async Task Status_WhenLegacyTaskLacksOwnership_ShouldRemainReadOnlyAndReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sourceFile = new WordFile
            {
                FileName = "legacy-status-source.xlsx",
                FileType = UploadedFileType.ExcelXlsx,
                FileContent = "legacy-status-source"u8.ToArray(),
                FileHash = "legacy-status-hash",
                CompanyId = 1,
                CreatedByUserId = 1,
                UploadedAt = DateTime.UtcNow
            };
            db.WordFiles.Add(sourceFile);
            await db.SaveChangesAsync();
            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = taskId,
                SourceFileId = sourceFile.Id,
                CreatedByUserId = null,
                CompanyId = null,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    payloadVersion = 4,
                    taskId,
                    sourceFileId = sourceFile.Id,
                    createdAt = DateTime.UtcNow,
                    fileMutationPending = false
                }),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/matching/tasks/{taskId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshot = await verifyDb.MatchingFillTasks.SingleAsync(item => item.TaskId == taskId);
        snapshot.CreatedByUserId.Should().BeNull();
        snapshot.CompanyId.Should().BeNull();
    }

    [Fact]
    public async Task Status_WhenAnonymous_ShouldReturnUnauthorized()
    {
        using var client = _factory.CreateClient();
        var taskId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/matching/tasks/{taskId}/status");
        request.Headers.Add("X-Test-Auth", "anonymous");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Download_WhenTaskBelongsToAnotherUser_ShouldReturnNotFound()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var expectedContent = "ownership-check"u8.ToArray();
        const string fileName = "ownership-check.docx";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var artifactPath = await storage.SaveFilledWordAsync(fileName, expectedContent);
            var sourceFile = new WordFile
            {
                FileName = "ownership-source.docx",
                FileType = UploadedFileType.WordDocx,
                FileContent = expectedContent,
                FileHash = FileStorageService.ComputeSha256(expectedContent),
                UploadedAt = DateTime.UtcNow
            };

            db.WordFiles.Add(sourceFile);
            await db.SaveChangesAsync();

            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = taskId,
                SourceFileId = sourceFile.Id,
                CreatedByUserId = 1,
                CompanyId = 1,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    payloadVersion = 2,
                    taskId,
                    sourceFileId = sourceFile.Id,
                    createdAt = DateTime.UtcNow,
                    downloadArtifactRelativePath = artifactPath,
                    downloadArtifactFileName = fileName,
                    downloadArtifactContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                }),
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using var ownerClient = _factory.CreateClient();
        var ownerResponse = await ownerClient.GetAsync($"/api/matching/download/{taskId}");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ownerResponse.Content.ReadAsByteArrayAsync()).Should().Equal(expectedContent);

        using var otherUserClient = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/matching/download/{taskId}");
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "*:*:*");

        var otherUserResponse = await otherUserClient.SendAsync(request);
        otherUserResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await otherUserResponse.ReadAsAsync<ApiResponse>();
        body.Code.Should().Be(404);
        body.Message.Should().Be("任务不存在或已过期");
    }

    [Fact]
    public async Task Download_WhenLegacyTaskLacksOwnershipColumnsButSourceFileHasOwner_ShouldBackfillAndReturnFile()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var expectedContent = "legacy-ownership-backfill"u8.ToArray();
        const string fileName = "legacy-ownership-backfill.docx";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var artifactPath = await storage.SaveFilledWordAsync(fileName, expectedContent);
            var sourceFile = new WordFile
            {
                FileName = "legacy-owned-source.docx",
                FileType = UploadedFileType.WordDocx,
                FileContent = expectedContent,
                FileHash = FileStorageService.ComputeSha256(expectedContent),
                CompanyId = 1,
                CreatedByUserId = 1,
                UploadedAt = DateTime.UtcNow
            };

            db.WordFiles.Add(sourceFile);
            await db.SaveChangesAsync();

            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = taskId,
                SourceFileId = sourceFile.Id,
                CreatedByUserId = null,
                CompanyId = null,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    payloadVersion = 2,
                    taskId,
                    sourceFileId = sourceFile.Id,
                    createdAt = DateTime.UtcNow,
                    downloadArtifactRelativePath = artifactPath,
                    downloadArtifactFileName = fileName,
                    downloadArtifactContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                }),
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/matching/download/{taskId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(expectedContent);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var snapshot = await verifyDb.MatchingFillTasks.SingleAsync(item => item.TaskId == taskId);
        snapshot.CreatedByUserId.Should().Be(1);
        snapshot.CompanyId.Should().Be(1);
    }

    [Fact]
    public async Task Download_WhenLegacyTaskOwnershipCannotBeRecovered_ShouldReturnExplicitBusinessError()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var expectedContent = "legacy-ownership-missing"u8.ToArray();
        const string fileName = "legacy-ownership-missing.docx";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var artifactPath = await storage.SaveFilledWordAsync(fileName, expectedContent);
            var sourceFile = new WordFile
            {
                FileName = "legacy-anonymous-source.docx",
                FileType = UploadedFileType.WordDocx,
                FileContent = expectedContent,
                FileHash = FileStorageService.ComputeSha256(expectedContent),
                UploadedAt = DateTime.UtcNow
            };

            db.WordFiles.Add(sourceFile);
            await db.SaveChangesAsync();

            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = taskId,
                SourceFileId = sourceFile.Id,
                CreatedByUserId = null,
                CompanyId = null,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    payloadVersion = 2,
                    taskId,
                    sourceFileId = sourceFile.Id,
                    createdAt = DateTime.UtcNow,
                    downloadArtifactRelativePath = artifactPath,
                    downloadArtifactFileName = fileName,
                    downloadArtifactContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                }),
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/matching/download/{taskId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse>();
        body.Code.Should().Be(400);
        body.Message.Should().Be("历史任务缺少归属信息，请重新执行填充后再下载");
    }

    private async Task SeedStatusTaskAsync(
        string taskId,
        int createdByUserId,
        int companyId,
        bool fileMutationPending,
        DateTime snapshotTime)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceFile = new WordFile
        {
            FileName = "status-source.xlsx",
            FileType = UploadedFileType.ExcelXlsx,
            FileContent = "status-source"u8.ToArray(),
            FileHash = "status-hash",
            CompanyId = companyId,
            CreatedByUserId = createdByUserId,
            UploadedAt = snapshotTime
        };
        db.WordFiles.Add(sourceFile);
        await db.SaveChangesAsync();

        db.MatchingFillTasks.Add(new MatchingFillTask
        {
            TaskId = taskId,
            SourceFileId = sourceFile.Id,
            CreatedByUserId = createdByUserId,
            CompanyId = companyId,
            PayloadJson = JsonSerializer.Serialize(new
            {
                payloadVersion = 4,
                taskId,
                sourceFileId = sourceFile.Id,
                createdAt = snapshotTime,
                fileMutationPending,
                downloadArtifactRelativePath = @"C:\sensitive\result.xlsx",
                filledFilePath = @"C:\sensitive\source.xlsx"
            }),
            CreatedAt = snapshotTime
        });
        await db.SaveChangesAsync();
    }

    private async Task<MatchingFillTaskSnapshot> SeedRawStatusTaskAsync(
        string taskId,
        Func<int, string> buildPayload)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceFile = new WordFile
        {
            FileName = "raw-status-source.xlsx",
            FileType = UploadedFileType.ExcelXlsx,
            FileContent = "raw-status-source"u8.ToArray(),
            FileHash = $"raw-status-{taskId}",
            CompanyId = 1,
            CreatedByUserId = 1,
            UploadedAt = DateTime.UtcNow
        };
        db.WordFiles.Add(sourceFile);
        await db.SaveChangesAsync();
        var task = new MatchingFillTask
        {
            TaskId = taskId,
            SourceFileId = sourceFile.Id,
            CreatedByUserId = 1,
            CompanyId = 1,
            PayloadJson = buildPayload(sourceFile.Id),
            CreatedAt = DateTime.UtcNow
        };
        db.MatchingFillTasks.Add(task);
        await db.SaveChangesAsync();

        return new MatchingFillTaskSnapshot(
            task.PayloadJson,
            task.CreatedByUserId,
            task.CompanyId,
            task.SourceFileId,
            task.CreatedAt);
    }

    private async Task AssertStatusNotFoundAndTaskUnchangedAsync(
        string taskId,
        MatchingFillTaskSnapshot original)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/matching/tasks/{taskId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.ReadAsAsync<ApiResponse>();
        body.Message.Should().Be("任务不存在或已过期");

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verificationDb.MatchingFillTasks
            .AsNoTracking()
            .SingleAsync(task => task.TaskId == taskId);
        persisted.PayloadJson.Should().Be(original.PayloadJson);
        persisted.CreatedByUserId.Should().Be(original.CreatedByUserId);
        persisted.CompanyId.Should().Be(original.CompanyId);
        persisted.SourceFileId.Should().Be(original.SourceFileId);
        persisted.CreatedAt.Should().Be(original.CreatedAt);
    }

    private sealed record MatchingFillTaskSnapshot(
        string PayloadJson,
        int? CreatedByUserId,
        int? CompanyId,
        int SourceFileId,
        DateTime CreatedAt);
}
