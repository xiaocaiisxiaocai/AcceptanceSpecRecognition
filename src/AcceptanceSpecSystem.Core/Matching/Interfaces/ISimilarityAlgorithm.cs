namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

/// <summary>
/// 相似度算法接口
/// </summary>
public interface ISimilarityAlgorithm
{
    /// <summary>
    /// 算法名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 计算两个文本的相似度
    /// </summary>
    /// <param name="text1">文本1</param>
    /// <param name="text2">文本2</param>
    /// <returns>相似度得分（0-1，1表示完全相同）</returns>
    double Calculate(string text1, string text2);
}
