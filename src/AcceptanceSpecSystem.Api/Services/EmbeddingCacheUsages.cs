namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 向量缓存用途，避免不同业务文本互相命中旧向量。
/// </summary>
public static class EmbeddingCacheUsages
{
    public const string Matching = "matching";
    public const string SemanticSearch = "semantic-search";
    public const string ImportDuplicateDetection = "import-duplicate-detection";
}
