using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Algorithms;

/// <summary>
/// Levenshtein距离相似度算法
/// 基于编辑距离计算文本相似度
/// </summary>
public class LevenshteinSimilarity : ISimilarityAlgorithm
{
    /// <summary>
    /// 算法名称标识。
    /// </summary>
    public string Name => "Levenshtein";

    /// <summary>
    /// 计算两个文本的相似度（0-1）。
    /// </summary>
    /// <param name="text1">文本1</param>
    /// <param name="text2">文本2</param>
    /// <returns>相似度（0-1）</returns>
    public double Calculate(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2))
            return 1.0;

        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;

        // 转换为小写进行比较
        text1 = text1.ToLowerInvariant();
        text2 = text2.ToLowerInvariant();

        if (text1 == text2)
            return 1.0;

        var distance = ComputeLevenshteinDistance(text1, text2);
        var maxLength = Math.Max(text1.Length, text2.Length);

        // 将距离转换为相似度（0-1）
        return 1.0 - (double)distance / maxLength;
    }

    /// <summary>
    /// 计算Levenshtein编辑距离
    /// </summary>
    private static int ComputeLevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;

        // 边界情况
        if (n == 0) return m;
        if (m == 0) return n;

        // 使用两行数组优化空间复杂度
        var previousRow = new int[m + 1];
        var currentRow = new int[m + 1];

        // 初始化第一行
        for (var j = 0; j <= m; j++)
        {
            previousRow[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            currentRow[0] = i;

            for (var j = 1; j <= m; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;

                currentRow[j] = Math.Min(
                    Math.Min(
                        currentRow[j - 1] + 1,      // 插入
                        previousRow[j] + 1),        // 删除
                    previousRow[j - 1] + cost);     // 替换
            }

            // 交换行
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[m];
    }
}
