namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 文本相似度计算接口（用于 Embedding 不可用时的降级方案）
/// </summary>
public interface ITextSimilarityService
{
    /// <summary>
    /// 计算两个文本的相似度（0~1）
    /// </summary>
    double ComputeSimilarity(string text1, string text2);
}

/// <summary>
/// 基于 Levenshtein 编辑距离的文本相似度服务
/// 时间复杂度 O(m*n)，适用于验收规格等短文本场景
/// </summary>
public class TextSimilarityService : ITextSimilarityService
{
    /// <summary>
    /// 计算两个文本的 Levenshtein 相似度：1 - (editDistance / maxLength)
    /// </summary>
    public double ComputeSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2))
            return 1.0;
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;

        var distance = ComputeLevenshteinDistance(text1, text2);
        var maxLength = Math.Max(text1.Length, text2.Length);
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// 计算 Levenshtein 编辑距离（Wagner-Fischer 动态规划）
    /// </summary>
    private static int ComputeLevenshteinDistance(string s, string t)
    {
        var m = s.Length;
        var n = t.Length;

        // 使用两行滚动数组优化空间至 O(min(m,n))
        if (m < n)
        {
            (s, t) = (t, s);
            (m, n) = (n, m);
        }

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (var j = 0; j <= n; j++)
            prev[j] = j;

        for (var i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= n; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
