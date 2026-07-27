namespace AcceptanceSpecSystem.Data.Entities;

public sealed class WordFileReferenceUnavailableException(int wordFileId)
    : InvalidOperationException($"源文件 {wordFileId} 已进入删除流程或不存在，禁止创建新的业务引用")
{
    public int WordFileId { get; } = wordFileId;
}
