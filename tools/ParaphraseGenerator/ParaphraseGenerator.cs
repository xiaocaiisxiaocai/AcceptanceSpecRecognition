using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ParaphraseGenerator;

/// <summary>
/// 生成语义等价但表达不同的改写文本
/// </summary>
public class ParaphraseGenerator
{
    private readonly IAiServiceFactory _aiServiceFactory;
    private readonly ILogger<ParaphraseGenerator> _logger;

    public ParaphraseGenerator(IAiServiceFactory aiServiceFactory, ILogger<ParaphraseGenerator> logger)
    {
        _aiServiceFactory = aiServiceFactory;
        _logger = logger;
    }

    public async Task<string> ParaphraseAsync(string originalText, CancellationToken cancellationToken = default)
    {
        var llmService = await _aiServiceFactory.GetActiveLlmServiceAsync(cancellationToken);
        if (llmService == null)
        {
            throw new InvalidOperationException("没有可用的 LLM 服务");
        }

        var kernel = llmService.CreateKernel();
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

        var prompt = BuildParaphrasePrompt(originalText);

        var history = new Microsoft.SemanticKernel.ChatMessageContent[]
        {
            new(Microsoft.SemanticKernel.AuthorRole.User, prompt)
        };

        var result = await chatCompletion.GetChatMessageContentAsync(
            history,
            cancellationToken: cancellationToken);

        return result.Content?.Trim() ?? string.Empty;
    }

    private static string BuildParaphrasePrompt(string originalText)
    {
        return $"""
你是一个专业的技术文档改写专家。

**任务**：对下面的验收规格文本进行同义改写（paraphrase），要求：

1. **语义完全等价**：改写后的文本必须表达与原文相同的技术要求和验收标准
2. **表达方式不同**：尽可能改变句式、用词、语序，但不改变意思
3. **保留关键信息**：数值、单位、品牌、型号必须保留（可以换单位，如 kW ↔ W）
4. **自然流畅**：改写后的文本要符合中文表达习惯

**改写示例**：
- 原文：机台需配备条码扫描装置
- 改写：设备应当装有条码识别系统

- 原文：伺服电机功率 7.5kW
- 改写：伺服马达额定功率 7500W

- 原文：触摸屏尺寸不小于 10 英寸
- 改写：人机界面显示器对角线长度应≥10英寸

**原文**：
{originalText}

**改写**（只输出改写后的文本，不要解释）：
""";
    }

    public async Task<List<ParaphraseResult>> BatchParaphraseAsync(
        List<SpecItem> specs,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ParaphraseResult>();

        for (var i = 0; i < Math.Min(specs.Count, maxCount); i++)
        {
            var spec = specs[i];

            try
            {
                _logger.LogInformation("正在改写 {Index}/{Total}: {Project}", i + 1, maxCount, spec.Project);

                var paraphrased = await ParaphraseAsync(spec.Specification, cancellationToken);

                // 跳过改写失败或与原文完全一致的
                if (string.IsNullOrWhiteSpace(paraphrased) ||
                    paraphrased.Trim() == spec.Specification.Trim())
                {
                    _logger.LogWarning("改写失败或未改变，跳过");
                    continue;
                }

                results.Add(new ParaphraseResult
                {
                    Project = spec.Project,
                    OriginalSpecification = spec.Specification,
                    ParaphrasedSpecification = paraphrased,
                    Acceptance = spec.Acceptance,
                    Remark = spec.Remark
                });

                // 避免频繁调用
                await Task.Delay(500, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "改写失败: {Project}", spec.Project);
            }
        }

        return results;
    }
}

public class SpecItem
{
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string? Acceptance { get; set; }
    public string? Remark { get; set; }
}

public class ParaphraseResult
{
    public string Project { get; set; } = string.Empty;
    public string OriginalSpecification { get; set; } = string.Empty;
    public string ParaphrasedSpecification { get; set; } = string.Empty;
    public string? Acceptance { get; set; }
    public string? Remark { get; set; }
}
