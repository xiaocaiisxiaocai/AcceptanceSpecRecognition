using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Algorithms;

/// <summary>
/// Jaccard相似度算法
/// 基于词集合的交集与并集比率计算相似度
/// </summary>
public class JaccardSimilarity : ISimilarityAlgorithm
{
    /// <summary>
    /// 算法名称标识。
    /// </summary>
    public string Name => "Jaccard";

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

        // 分词并转换为小写
        var tokens1 = Tokenize(text1);
        var tokens2 = Tokenize(text2);

        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 1.0;

        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;

        // 计算交集和并集
        var intersection = tokens1.Intersect(tokens2).Count();
        var union = tokens1.Union(tokens2).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    /// <summary>
    /// 分词处理
    /// 支持中文和英文混合文本
    /// </summary>
    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 转换为小写
        text = text.ToLowerInvariant();

        // 按空格和标点分词（处理英文）
        var words = text.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}', '/', '\\', '-', '_', '=', '+', '"', '\'', '。', '，', '！', '？', '；', '：', '（', '）', '【', '】', '、', '·' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;

            // 添加整个词
            tokens.Add(word.Trim());

            // 对于包含中文的词，进行字符级分割
            if (ContainsChinese(word))
            {
                // 中文字符级别分词
                foreach (var ch in word)
                {
                    if (IsChinese(ch))
                    {
                        tokens.Add(ch.ToString());
                    }
                }

                // 中文bigram
                for (var i = 0; i < word.Length - 1; i++)
                {
                    if (IsChinese(word[i]) && IsChinese(word[i + 1]))
                    {
                        tokens.Add(word.Substring(i, 2));
                    }
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// 检查文本是否包含中文字符
    /// </summary>
    private static bool ContainsChinese(string text)
    {
        return text.Any(IsChinese);
    }

    /// <summary>
    /// 检查字符是否为中文
    /// </summary>
    private static bool IsChinese(char c)
    {
        return c >= 0x4E00 && c <= 0x9FFF;
    }
}
