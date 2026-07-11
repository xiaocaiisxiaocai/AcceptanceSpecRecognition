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
        var tableCount = await _documentTableAccessService.CountTablesAsync(
            file.FileType,
            file.Content,
            cancellationToken);
        var session = await _batchReplySessionService.CreateSourceSessionAsync(
            owner.UserId,
            owner.CompanyId,
            file.FileName,
            file.FileType,
            file.Content,
            cancellationToken);

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

        var session = GetSourceSessionForApplication(user, sessionId);
        var uploadedTargets = new List<BatchReplyTargetFile>();
        foreach (var targetFile in targetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (targetFile.Content.Length == 0)
            {
                throw new ApplicationServiceException(400, "目标文件不能为空");
            }

            if (targetFile.FileType != session.SourceFileType)
            {
                throw new ApplicationServiceException(400, "目标文件必须与来源文件保持同格式");
            }

            var relativePath = await _batchReplySessionService.SaveTargetFileAsync(
                targetFile.FileName,
                targetFile.FileType,
                targetFile.Content,
                cancellationToken);

            uploadedTargets.Add(new BatchReplyTargetFile
            {
                TargetId = Guid.NewGuid().ToString("N"),
                FileName = targetFile.FileName,
                FileType = targetFile.FileType,
                RelativePath = relativePath,
                TableCount = await _documentTableAccessService.CountTablesAsync(
                    targetFile.FileType,
                    targetFile.Content,
                    cancellationToken)
            });
        }

        var updatedSession = await _batchReplySessionService.AddTargetFilesAsync(
            owner.UserId,
            owner.CompanyId,
            sessionId,
            uploadedTargets,
            cancellationToken);
        if (updatedSession == null)
        {
            throw new ApplicationServiceException(404, "来源会话不存在或已过期");
        }

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
