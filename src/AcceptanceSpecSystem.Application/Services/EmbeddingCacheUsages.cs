namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 向量缓存用途，避免不同业务文本互相命中旧向量。
/// </summary>
public static class EmbeddingCacheUsages
{
    public const string Matching = "matching";

    /// <summary>
    /// 仅规格匹配模式的候选向量：语料为纯规格文本（不含项目），
    /// 与 <see cref="Matching"/>（项目+规格语料）分开缓存，避免两种模式互相命中错误语料的向量。
    /// </summary>
    public const string MatchingSpecificationOnly = "matching-specification-only";

    public const string SemanticSearch = "semantic-search";
    public const string ImportDuplicateDetection = "import-duplicate-detection";
}
