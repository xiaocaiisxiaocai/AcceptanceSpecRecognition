namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 上传文档物理路径解析器。
/// </summary>
public interface IUploadedDocumentPathResolver
{
    string ResolveAbsolutePath(string relativePath);
}
