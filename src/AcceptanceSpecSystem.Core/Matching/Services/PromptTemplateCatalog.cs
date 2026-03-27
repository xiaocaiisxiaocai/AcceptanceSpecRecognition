using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed record SystemPromptTemplateDefinition(
    PromptTemplateScene Scene,
    string Name,
    string DisplayName,
    string UsageDescription,
    string DefaultContent,
    string? LegacyDefaultContent,
    IReadOnlyList<string> RequiredVariables,
    IReadOnlyList<string> AvailableVariables,
    IReadOnlyList<string> RequiredJsonKeys);

public static class PromptTemplateCatalog
{
    private static readonly SystemPromptTemplateDefinition[] Definitions =
    [
        new(
            PromptTemplateScene.MatchingReview,
            "matching-review",
            "智能填充复核",
            "用于智能填充流程中的 LLM 复核。",
            "你是验收规格匹配复核助手。系统通过 Embedding 向量相似度为源文档找到了最佳匹配的验收规格，请复核此匹配是否正确。\n\n" +
            "【任务】对比\"源文档\"与\"系统匹配结果\"的项目名称和规格描述，判断两者是否指向同一个验收项。\n\n" +
            "【源文档】\n" +
            "项目：{{sourceProject}}\n" +
            "规格：{{sourceSpecification}}\n\n" +
            "【系统匹配结果】\n" +
            "项目：{{bestMatchProject}}\n" +
            "规格：{{bestMatchSpecification}}\n" +
            "验收标准：{{bestMatchAcceptance}}\n" +
            "备注：{{bestMatchRemark}}\n\n" +
            "【Embedding 基础得分】{{baseScore}}\n" +
            "【得分明细】{{scoreDetailsJson}}\n\n" +
            "仅返回严格 JSON：\n" +
            "{\"score\":0,\"reason\":\"...\",\"commentary\":\"...\"}",
            "你是验收规格匹配评审助手。给定源项目/规格与系统最佳匹配结果，请复核评分并说明原因。\n" +
            "仅返回严格 JSON：\n" +
            "{\"score\":0,\"reason\":\"...\",\"commentary\":\"...\"}\n" +
            "要求：\n" +
            "- score 取值 0~100\n" +
            "- reason 解释为什么评分高/低（重点说明低分原因）\n" +
            "- commentary 简短描述评论过程（对比了哪些关键信息）\n" +
            "源项目：{{sourceProject}}\n" +
            "源规格：{{sourceSpecification}}\n" +
            "最佳匹配项目：{{bestMatchProject}}\n" +
            "最佳匹配规格：{{bestMatchSpecification}}\n" +
            "验收标准：{{bestMatchAcceptance}}\n" +
            "基础得分：{{baseScore}}\n" +
            "得分明细(JSON)：{{scoreDetailsJson}}",
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "baseScore", "scoreDetailsJson"],
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "bestMatchAcceptance", "bestMatchRemark", "baseScore", "scoreDetailsJson"],
            ["score", "reason", "commentary"]),
        new(
            PromptTemplateScene.ImportDuplicateReview,
            "import-duplicate-review",
            "导入重复复核",
            "用于导入疑似重复识别中的 LLM 复核。",
            "你是导入重复识别复核助手。系统基于 Embedding 找到了疑似重复的历史验收规格，请判断它们是否代表同一条验收规格。\n\n" +
            "【导入内容】\n" +
            "项目：{{sourceProject}}\n" +
            "规格：{{sourceSpecification}}\n\n" +
            "【历史验收规格】\n" +
            "项目：{{bestMatchProject}}\n" +
            "规格：{{bestMatchSpecification}}\n" +
            "验收标准：{{bestMatchAcceptance}}\n" +
            "备注：{{bestMatchRemark}}\n\n" +
            "【Embedding 基础得分】{{baseScore}}\n" +
            "【得分明细】{{scoreDetailsJson}}\n\n" +
            "仅返回严格 JSON：\n" +
            "{\"score\":0,\"reason\":\"...\",\"commentary\":\"...\"}",
            null,
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "baseScore", "scoreDetailsJson"],
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "bestMatchAcceptance", "bestMatchRemark", "baseScore", "scoreDetailsJson"],
            ["score", "reason", "commentary"]),
        new(
            PromptTemplateScene.MatchingGenerate,
            "matching-generate",
            "智能填充建议生成",
            "用于智能填充流程中的验收/备注建议生成。",
            "你是验收规格助手。请根据源文档信息整理验收标准与备注。\n\n" +
            "【源文档】\n" +
            "项目：{{sourceProject}}\n" +
            "规格：{{sourceSpecification}}\n\n" +
            "【参考数据】\n" +
            "{{referenceInfo}}\n\n" +
            "【核心约束】\n" +
            "1. 严禁编造任何数值、标准、检验方法或技术参数\n" +
            "2. 只能整理源文档中已经明确写出的信息\n" +
            "3. 信息不足时 acceptance 和 remark 必须返回空字符串\n\n" +
            "仅返回严格 JSON：\n" +
            "{\"acceptance\":\"...\",\"remark\":\"...\",\"reason\":\"...\"}",
            "你是验收规格助手。请根据\u201c源项目/规格\u201d生成验收标准与备注建议。\n" +
            "仅返回严格 JSON：\n" +
            "{\"acceptance\":\"...\",\"remark\":\"...\",\"reason\":\"...\"}\n" +
            "要求：\n" +
            "- 用中文\n" +
            "- 内容简洁、可执行\n" +
            "- 不确定时可返回空字符串\n" +
            "源项目：{{sourceProject}}\n" +
            "源规格：{{sourceSpecification}}",
            ["sourceProject", "sourceSpecification", "referenceInfo"],
            ["sourceProject", "sourceSpecification", "referenceInfo"],
            ["acceptance", "remark", "reason"])
    ];

    public static IReadOnlyList<SystemPromptTemplateDefinition> GetSystemTemplates() => Definitions;

    public static SystemPromptTemplateDefinition GetByScene(PromptTemplateScene scene)
    {
        return Definitions.First(definition => definition.Scene == scene);
    }

    public static bool TryGetByName(string? name, out SystemPromptTemplateDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            string.Equals(item.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return definition != null;
    }
}
