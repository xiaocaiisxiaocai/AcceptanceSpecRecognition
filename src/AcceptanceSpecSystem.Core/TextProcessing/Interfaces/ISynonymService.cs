namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface ISynonymService
{
    /// <summary>
    /// 获取“词 -> 标准词”的映射（包含标准词自身映射到自身）
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetWordToStandardMapAsync(CancellationToken cancellationToken = default);
}

