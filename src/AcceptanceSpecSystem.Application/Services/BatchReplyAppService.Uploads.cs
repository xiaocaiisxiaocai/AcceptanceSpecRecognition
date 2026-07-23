using System.IO.Compression;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class BatchReplyAppService
{
    public async Task<BatchReplySourceUploadResponse> UploadSourceAsync(
        BatchReplyUserContext user,
        BatchReplyUploadDocument file,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForApplication(user);
        ValidateUploadBudget([file]);
        await using var sourceStream = file.OpenReadStream();
        var session = await _batchReplySessionService.CreateSourceSessionAsync(
            owner.UserId,
            owner.CompanyId,
            file.FileName,
            file.FileType,
            sourceStream,
            cancellationToken);

        int tableCount;
        try
        {
            var sourceFile = CreateTemporaryWordFile(
                session.SourceFileName,
                session.SourceFileType,
                session.SourceFileRelativePath);
            tableCount = (await _documentTableAccessService.GetTablesAsync(sourceFile, cancellationToken)).Count;
        }
        catch
        {
            // 会话尚未返回给客户端，取消后仍需完成临时文件与 manifest 补偿。
            await _batchReplySessionService.DeleteSessionAsync(
                owner.UserId,
                owner.CompanyId,
                session.SessionId,
                CancellationToken.None);
            throw;
        }

        return new BatchReplySourceUploadResponse
        {
            SessionId = session.SessionId,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            TableCount = tableCount
        };
    }

    public async Task<List<TableInfoDto>> GetSourceTablesAsync(
        BatchReplyUserContext user,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        return await _documentTableAccessService.GetTableInfoDtosAsync(sourceFile, cancellationToken);
    }

    public async Task<TableDataDto> GetSourceTablePreviewAsync(
        BatchReplyUserContext user,
        string sessionId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        CancellationToken cancellationToken = default)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        return await _documentTableAccessService.GetTablePreviewAsync(
            sourceFile,
            tableIndex,
            previewRows,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex,
            cancellationToken: cancellationToken);
    }

    public async Task<BatchReplyTargetUploadResponse> UploadTargetsAsync(
        BatchReplyUserContext user,
        string sessionId,
        IReadOnlyCollection<BatchReplyUploadDocument> targetFiles,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForApplication(user);
        if (targetFiles == null || targetFiles.Count == 0)
        {
            throw new ApplicationServiceException(400, "请至少上传一个目标文件");
        }

        ValidateUploadBudget(targetFiles);

        var session = GetSourceSessionForApplication(user, sessionId);
        var uploadedTargets = new List<BatchReplyTargetFile>();
        var pendingRelativePaths = new List<string>();
        try
        {
            foreach (var targetFile in targetFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targetFile.Length <= 0)
                    throw new ApplicationServiceException(400, "目标文件不能为空");

                if (targetFile.FileType != session.SourceFileType)
                    throw new ApplicationServiceException(400, "目标文件必须与来源文件保持同格式");

                await using var targetStream = targetFile.OpenReadStream();
                var relativePath = await _batchReplySessionService.SaveTargetFileAsync(
                    targetFile.FileName,
                    targetFile.FileType,
                    targetStream,
                    cancellationToken);
                pendingRelativePaths.Add(relativePath);

                var targetWordFile = CreateTemporaryWordFile(
                    targetFile.FileName,
                    targetFile.FileType,
                    relativePath);
                uploadedTargets.Add(new BatchReplyTargetFile
                {
                    TargetId = Guid.NewGuid().ToString("N"),
                    FileName = targetFile.FileName,
                    FileType = targetFile.FileType,
                    RelativePath = relativePath,
                    TableCount = (await _documentTableAccessService.GetTablesAsync(targetWordFile, cancellationToken)).Count
                });
            }

            var updatedSession = await _batchReplySessionService.AddTargetFilesAsync(
                owner.UserId,
                owner.CompanyId,
                sessionId,
                uploadedTargets,
                cancellationToken);
            if (updatedSession == null)
                throw new ApplicationServiceException(404, "来源会话不存在或已过期");

            pendingRelativePaths.Clear();
            return new BatchReplyTargetUploadResponse
            {
                SessionId = updatedSession.SessionId,
                Files = uploadedTargets.Select(file => new BatchReplyUploadedTargetFileDto
                {
                    TargetId = file.TargetId,
                    FileName = file.FileName,
                    FileType = file.FileType ?? session.SourceFileType,
                    TableCount = file.TableCount
                }).ToList()
            };
        }
        catch
        {
            // manifest 未提交前必须脱离请求取消完成已落盘文件补偿。
            await _batchReplySessionService.DeleteTemporaryFilesAsync(
                pendingRelativePaths,
                CancellationToken.None);
            throw;
        }
    }

    private static void ValidateUploadBudget(IReadOnlyCollection<BatchReplyUploadDocument> files)
    {
        if (files.Count == 0 || files.Count > BatchReplyUploadLimits.MaxFileCount)
            throw new ApplicationServiceException(400, $"单次最多上传 {BatchReplyUploadLimits.MaxFileCount} 个文件");

        long totalBytes = 0;
        foreach (var file in files)
        {
            if (file.Length <= 0)
                throw new ApplicationServiceException(400, "文件不能为空");
            if (file.Length > BatchReplyUploadLimits.MaxFileSizeBytes)
                throw new ApplicationServiceException(400, "单个文件大小不能超过 50MB");
            totalBytes = checked(totalBytes + file.Length);
        }

        if (totalBytes > BatchReplyUploadLimits.MaxBatchSizeBytes)
            throw new ApplicationServiceException(400, "单次上传文件总大小不能超过 100MB");
    }

    public async Task<List<TableInfoDto>> GetTargetTablesAsync(
        BatchReplyUserContext user,
        string sessionId,
        string targetId,
        CancellationToken cancellationToken = default)
    {
        var targetFile = GetTargetFileForApplication(user, sessionId, targetId);
        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, targetFile.FileType!.Value, targetFile.RelativePath!);
        return await _documentTableAccessService.GetTableInfoDtosAsync(targetWordFile, cancellationToken);
    }

    public async Task<TableDataDto> GetTargetTablePreviewAsync(
        BatchReplyUserContext user,
        string sessionId,
        string targetId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        CancellationToken cancellationToken = default)
    {
        var targetFile = GetTargetFileForApplication(user, sessionId, targetId);
        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, targetFile.FileType!.Value, targetFile.RelativePath!);
        return await _documentTableAccessService.GetTablePreviewAsync(
            targetWordFile,
            tableIndex,
            previewRows,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex,
            cancellationToken: cancellationToken);
    }

}
