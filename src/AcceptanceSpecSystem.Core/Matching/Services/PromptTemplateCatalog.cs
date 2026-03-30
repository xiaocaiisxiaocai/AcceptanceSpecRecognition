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
            "你是验收规格匹配复核助手。系统已经完成 Embedding 召回与证据裁决，现在只允许你基于结构化证据判断该匹配是否可通过复核。\n\n" +
            "【任务】对比\"源文档\"与\"系统匹配结果\"的项目名称、规格描述和结构化证据，判断两者是否指向同一个验收项。\n" +
            "若存在关键字段硬冲突或证据明显不足，必须给出低分，不得因为语义相近而放行。\n\n" +
            "【源文档】\n" +
            "项目：{{sourceProject}}\n" +
            "规格：{{sourceSpecification}}\n\n" +
            "【系统匹配结果】\n" +
            "项目：{{bestMatchProject}}\n" +
            "规格：{{bestMatchSpecification}}\n" +
            "验收标准：{{bestMatchAcceptance}}\n" +
            "备注：{{bestMatchRemark}}\n\n" +
            "【当前决策】{{currentDecision}}\n" +
            "【是否硬冲突】{{hasHardConflict}}\n" +
            "【复核触发原因】{{reviewTrigger}}\n" +
            "【Embedding 基础得分】{{baseScore}}\n" +
            "【得分明细】{{scoreDetailsJson}}\n" +
            "【证据摘要】{{evidenceSummaryJson}}\n" +
            "【冲突摘要】{{conflictSummaryJson}}\n\n" +
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
            [
                "sourceProject",
                "sourceSpecification",
                "bestMatchProject",
                "bestMatchSpecification",
                "bestMatchAcceptance",
                "bestMatchRemark",
                "baseScore",
                "scoreDetailsJson",
                "currentDecision",
                "hasHardConflict",
                "reviewTrigger",
                "evidenceSummaryJson",
                "conflictSummaryJson"
            ],
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
            ["acceptance", "remark", "reason"]),
        new(
            PromptTemplateScene.MatchingEntityResolution,
            "matching-entity-resolution",
            "智能填充实体判别",
            "用于智能填充流程中的品牌/实体关系判别。",
            "你是品牌/实体判别助手。系统已经提取出两个实体候选，你只能判断它们是否为同一实体、别名同一、明确冲突，或无法判断。\n\n" +
            "【源项实体】{{sourceEntity}}\n" +
            "【候选实体】{{candidateEntity}}\n\n" +
            "【源项上下文】{{sourceText}}\n" +
            "【候选上下文】{{candidateText}}\n\n" +
            "【约束】\n" +
            "1. 只判断实体关系，不要根据数值或型号是否一致来推断品牌关系\n" +
            "2. 证据不足时必须返回 unknown，禁止猜测\n" +
            "3. relation 只允许 same、alias_same、conflict、unknown 四个值\n" +
            "4. confidence 取值 0~1\n\n" +
            "仅返回严格 JSON：\n" +
            "{\"relation\":\"unknown\",\"confidence\":0.0,\"normalizedEntity\":\"\",\"reason\":\"...\"}",
            null,
            ["sourceEntity", "candidateEntity", "sourceText", "candidateText"],
            ["sourceEntity", "candidateEntity", "sourceText", "candidateText"],
            ["relation", "confidence", "normalizedEntity", "reason"]),
        new(
            PromptTemplateScene.MatchingKnowledgeGenerate,
            "matching-knowledge-generate",
            "匹配知识草稿生成",
            "用于从文本或文档样本中生成匹配知识草稿候选。",
            "你是匹配知识整理助手。请从输入内容中提取当前分类的候选项。\n\n" +
            "【当前分类】{{category}}\n" +
            "【分类说明】{{categoryDescription}}\n\n" +
            "【输入内容】\n{{sourceText}}\n\n" +
            "【约束】\n" +
            "1. 只输出当前分类需要的候选项\n" +
            "2. 不要编造输入中不存在的专业名词\n" +
            "3. 单位规则只输出单位别名，不允许输出倍率或换算系数\n" +
            "4. 冲突词对只输出明确互斥的词，不要输出语义模糊的关系\n\n" +
            "仅返回严格 JSON：\n" +
            "{\"items\":[{\"key\":\"...\",\"value\":\"...\",\"evidenceSnippet\":\"...\",\"reason\":\"...\"}]}",
            null,
            ["category", "categoryDescription", "sourceText"],
            ["category", "categoryDescription", "sourceText"],
            ["items"])
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
