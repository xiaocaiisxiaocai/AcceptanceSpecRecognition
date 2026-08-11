namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 测试或未接入快照缓存实现时使用的空失效器。
/// </summary>
internal sealed class NoOpUploadedDocumentSnapshotInvalidator : IUploadedDocumentSnapshotInvalidator
{
    public static NoOpUploadedDocumentSnapshotInvalidator Instance { get; } = new();

    private NoOpUploadedDocumentSnapshotInvalidator()
    {
    }

    public void Invalidate(int fileId)
    {
    }
}
