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
}
