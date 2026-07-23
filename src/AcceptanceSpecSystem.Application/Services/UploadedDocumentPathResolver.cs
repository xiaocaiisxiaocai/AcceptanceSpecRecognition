using AcceptanceSpecSystem.Application.Services;

namespace AcceptanceSpecSystem.Application.Services;

public sealed class UploadedDocumentPathResolver : IUploadedDocumentPathResolver
{
    private readonly IFileStorageService _fileStorage;

    public UploadedDocumentPathResolver(IFileStorageService fileStorage)
    {
        _fileStorage = fileStorage;
    }

    public string ResolveAbsolutePath(string relativePath)
    {
        return _fileStorage.GetAbsolutePath(relativePath);
    }
}
