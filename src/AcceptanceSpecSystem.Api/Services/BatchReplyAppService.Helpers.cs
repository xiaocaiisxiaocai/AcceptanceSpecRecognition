using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class BatchReplyAppService
{
    private BatchReplySourceSession GetSourceSessionForApplication(ClaimsPrincipal user, string sessionId)
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

    private BatchReplyTargetFile GetTargetFileForApplication(ClaimsPrincipal user, string sessionId, string targetId)
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

    private static (int UserId, int CompanyId) ResolveOwnerForApplication(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw new ApplicationServiceException(401, "会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
    }

    private static (int UserId, int CompanyId) ResolveOwnerForMatching(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
    }

    private static MatchingOperationResult<T> Result<T>(T data, string message = "操作成功")
    {
        return new MatchingOperationResult<T>(data, message);
    }

    private static MatchingApiException Failure(int code, string message)
    {
        return new MatchingApiException(code, message);
    }

    private static MatchingApiException NotFoundFailure(string message)
    {
        return new MatchingApiException(404, message, isNotFound: true);
    }
}
