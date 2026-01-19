using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Algorithms;

/// <summary>
/// 余弦相似度算法
/// 基于TF-IDF向量计算文本相似度
/// </summary>
public class CosineSimilarity : ISimilarityAlgorithm
{
    /// <summary>
    /// 算法名称标识。
    /// </summary>
    public string Name => "Cosine";

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

        // 分词
        var tokens1 = Tokenize(text1);
        var tokens2 = Tokenize(text2);

        if (tokens1.Count == 0 && tokens2.Count == 0)
            return 1.0;

        if (tokens1.Count == 0 || tokens2.Count == 0)
            return 0.0;

        // 构建词频向量
        var tf1 = GetTermFrequency(tokens1);
        var tf2 = GetTermFrequency(tokens2);

        // 获取所有唯一词
        var allTerms = tf1.Keys.Union(tf2.Keys).ToList();

        // 构建向量
        var vector1 = new double[allTerms.Count];
        var vector2 = new double[allTerms.Count];

        for (var i = 0; i < allTerms.Count; i++)
        {
            var term = allTerms[i];
            vector1[i] = tf1.GetValueOrDefault(term, 0);
            vector2[i] = tf2.GetValueOrDefault(term, 0);
        }

        // 计算余弦相似度
        return ComputeCosineSimilarity(vector1, vector2);
    }

    /// <summary>
    /// 计算余弦相似度
    /// </summary>
    private static double ComputeCosineSimilarity(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length)
            throw new ArgumentException("向量长度不一致");

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (var i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }

    /// <summary>
    /// 计算词频
    /// </summary>
    private static Dictionary<string, double> GetTermFrequency(List<string> tokens)
    {
        var tf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var totalTokens = tokens.Count;

        foreach (var token in tokens)
        {
            if (!tf.ContainsKey(token))
            {
                tf[token] = 0;
            }
            tf[token]++;
        }

        // 归一化
        foreach (var key in tf.Keys.ToList())
        {
            tf[key] /= totalTokens;
        }

        return tf;
    }

    /// <summary>
    /// 分词处理
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();

        // 转换为小写
        text = text.ToLowerInvariant();

        // 按空格和标点分词
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

                // 中文bigram（增强匹配效果）
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
