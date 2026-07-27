namespace AcceptanceSpecSystem.Data.Entities;

public sealed class WordFileReferenceUnavailableException(int wordFileId)
    : InvalidOperationException("源文件状态已变化，请刷新后重试")
{
    public int WordFileId { get; } = wordFileId;
}
