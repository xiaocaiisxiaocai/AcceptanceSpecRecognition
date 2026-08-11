using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 为已上传文档提供跨请求可复用的完整表格解析快照。
/// 调用方在传入 <see cref="WordFile"/> 前必须已完成文件与客户数据范围校验。
/// </summary>
public interface IUploadedDocumentSnapshotProvider
{
    /// <summary>
    /// 获取与直接解析一致的表格快照；返回值为调用方隔离的深复制结果。
    /// </summary>
    Task<DocumentTableSnapshot> GetSnapshotAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 按文件 ID 主动失效已缓存的解析快照。
/// </summary>
public interface IUploadedDocumentSnapshotInvalidator
{
    void Invalidate(int fileId);
}
