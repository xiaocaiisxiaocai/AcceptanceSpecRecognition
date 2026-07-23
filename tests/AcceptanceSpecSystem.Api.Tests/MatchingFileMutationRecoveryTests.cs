using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class MatchingFileMutationRecoveryTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MatchingFileMutationRecoveryTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recovery_ShouldRestorePendingWorkbookAndRemoveJournalArtifact()
    {
        var original = ExcelFillFlowTests.CreateExcelBytes(
        [
            ["项目", "规格", "验收"],
            ["P1", "S1", ""]
        ]);
        var mutated = ExcelFillFlowTests.CreateExcelBytes(
        [
            ["项目", "规格", "验收"],
            ["P1", "S1", "MUTATED"]
        ]);

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(original);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        uploadContent.Add(fileContent, "file", "pending-recovery.xlsx");
        var upload = await _client.PostAsync("/api/documents/upload", uploadContent);
        var fileId = (await upload.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("fileId").GetInt32();

        string rollbackPath;
        string taskId = Guid.NewGuid().ToString("N");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var documentAccess = scope.ServiceProvider.GetRequiredService<IDocumentFileAccessService>();
            var wordFile = await db.WordFiles.SingleAsync(item => item.Id == fileId);
            var originalPath = wordFile.FilePath;
            var originalHash = wordFile.FileHash;
            rollbackPath = await storage.SaveFilledWordAsync($"rollback-{taskId}.xlsx", original);

            await documentAccess.PersistUpdatedFileContentAsync(wordFile, mutated);
            await db.SaveChangesAsync();

            var taskResult = new FillTaskResult
            {
                TaskId = taskId,
                SourceFileId = fileId,
                RequestFingerprint = "recovery-fingerprint",
                CreatedAt = DateTime.UtcNow,
                FileMutationPending = true,
                SourceRollbackArtifactRelativePath = rollbackPath,
                SourceOriginalFilePath = originalPath,
                SourceOriginalFileHash = originalHash
            };
            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = taskId,
                SourceFileId = fileId,
                CreatedByUserId = 1,
                CompanyId = 1,
                PayloadJson = JsonSerializer.Serialize(taskResult, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var snapshotService = scope.ServiceProvider.GetRequiredService<MatchingTaskSnapshotService>();
            await snapshotService.RecoverAllPendingFileMutationsAsync();
        }

        var preview = await _client.GetAsync(
            $"/api/documents/{fileId}/tables/0/preview?previewRows=500&headerRowIndex=0&headerRowCount=1&dataStartRowIndex=1");
        var previewBody = await preview.ReadAsAsync<ApiResponse<JsonElement>>();
        previewBody.Data.GetProperty("rows")[0][2].GetString().Should().BeEmpty();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.MatchingFillTasks.CountAsync(item => item.TaskId == taskId)).Should().Be(0);
        var verifyStorage = verifyScope.ServiceProvider.GetRequiredService<IFileStorageService>();
        File.Exists(verifyStorage.GetAbsolutePath(rollbackPath)).Should().BeFalse();
    }
}
