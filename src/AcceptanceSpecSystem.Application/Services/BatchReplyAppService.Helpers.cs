using System.IO.Compression;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class BatchReplyAppService
{
    private BatchReplySourceSession GetSourceSessionForApplication(BatchReplyUserContext user, string sessionId)
    {
        var owner = ResolveOwnerForApplication(user);
        var session = _batchReplySessionService.GetSession(owner.UserId, owner.CompanyId, sessionId);
        if (session == null)
        {
            throw new ApplicationServiceException(404, "来源会话不存在或已过期");
        }

        return session;
    }

    private BatchReplySourceSession GetSourceSessionForMatching((int UserId, int CompanyId) owner, string sessionId)
    {
        var session = _batchReplySessionService.GetSession(owner.UserId, owner.CompanyId, sessionId);
        if (session == null)
        {
            throw NotFoundFailure("来源会话不存在或已过期");
        }

        return session;
    }

    private BatchReplyTargetFile GetTargetFileForApplication(BatchReplyUserContext user, string sessionId, string targetId)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var targetFile = session.TargetFiles.FirstOrDefault(file => string.Equals(file.TargetId, targetId, StringComparison.Ordinal));
        if (targetFile == null || string.IsNullOrWhiteSpace(targetFile.RelativePath) || !targetFile.FileType.HasValue)
        {
            throw new ApplicationServiceException(404, "目标文件不存在或已过期");
        }

        return targetFile;
    }

    private static BatchReplyTargetFile GetTargetFileForMatching(BatchReplySourceSession session, string targetId)
    {
        var targetFile = session.TargetFiles.FirstOrDefault(file => string.Equals(file.TargetId, targetId, StringComparison.Ordinal));
        if (targetFile == null || string.IsNullOrWhiteSpace(targetFile.RelativePath) || !targetFile.FileType.HasValue)
        {
            throw NotFoundFailure("目标文件不存在或已过期");
        }

        return targetFile;
    }


    private static WordFile CreateTemporaryWordFile(string fileName, UploadedFileType fileType, string relativePath)
    {
        return new WordFile
        {
            FileName = fileName,
            FileType = fileType,
            FilePath = relativePath,
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };
    }

    private static (int UserId, int CompanyId) ResolveOwnerForApplication(BatchReplyUserContext user)
    {
        return (user.UserId, user.CompanyId);
    }

    private static (int UserId, int CompanyId) ResolveOwnerForMatching(BatchReplyUserContext user)
    {
        return (user.UserId, user.CompanyId);
    }
}
