using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class BatchReplyAppService
{
    public async Task<BatchReplySourceUploadResponse> UploadSourceAsync(
        ClaimsPrincipal user,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForApplication(user);
        var fileType = UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        byte[] content;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream, cancellationToken);
            content = memoryStream.ToArray();
        }

        var tableCount = await _documentTableAccessService.CountTablesAsync(fileType, content);
        var session = await _batchReplySessionService.CreateSourceSessionAsync(
            owner.UserId,
            owner.CompanyId,
            file.FileName,
            fileType,
            content,
            cancellationToken);

        return new BatchReplySourceUploadResponse
        {
            SessionId = session.SessionId,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            TableCount = tableCount
        };
    }

    public async Task<List<TableInfoDto>> GetSourceTablesAsync(ClaimsPrincipal user, string sessionId)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        return await _documentTableAccessService.GetTableInfoDtosAsync(sourceFile);
    }

    public async Task<TableDataDto> GetSourceTablePreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        return await _documentTableAccessService.GetTablePreviewAsync(
            sourceFile,
            tableIndex,
            previewRows,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex);
    }

    public async Task<BatchReplyTargetUploadResponse> UploadTargetsAsync(
        ClaimsPrincipal user,
        string sessionId,
        IReadOnlyCollection<IFormFile> targetFiles,
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
            if (targetFile == null || targetFile.Length == 0)
            {
                throw new ApplicationServiceException(400, "目标文件不能为空");
            }

            var fileType = UploadFileValidation.ValidateOfficeDocument(targetFile, allowExcel: true, allowWord: true);
            if (fileType != session.SourceFileType)
            {
                throw new ApplicationServiceException(400, "目标文件必须与来源文件保持同格式");
            }

            byte[] content;
            using (var memoryStream = new MemoryStream())
            {
                await targetFile.CopyToAsync(memoryStream, cancellationToken);
                content = memoryStream.ToArray();
            }

            var relativePath = await _batchReplySessionService.SaveTargetFileAsync(
                targetFile.FileName,
                fileType,
                content,
                cancellationToken);

            uploadedTargets.Add(new BatchReplyTargetFile
            {
                TargetId = Guid.NewGuid().ToString("N"),
                FileName = targetFile.FileName,
                FileType = fileType,
                RelativePath = relativePath,
                TableCount = await _documentTableAccessService.CountTablesAsync(fileType, content)
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

    public async Task<List<TableInfoDto>> GetTargetTablesAsync(ClaimsPrincipal user, string sessionId, string targetId)
    {
        var targetFile = GetTargetFileForApplication(user, sessionId, targetId);
        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, targetFile.FileType!.Value, targetFile.RelativePath!);
        return await _documentTableAccessService.GetTableInfoDtosAsync(targetWordFile);
    }

    public async Task<TableDataDto> GetTargetTablePreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        string targetId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex)
    {
        var targetFile = GetTargetFileForApplication(user, sessionId, targetId);
        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, targetFile.FileType!.Value, targetFile.RelativePath!);
        return await _documentTableAccessService.GetTablePreviewAsync(
            targetWordFile,
            tableIndex,
            previewRows,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex);
    }

}
