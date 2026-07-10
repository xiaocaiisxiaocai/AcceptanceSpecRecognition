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
    IReadOnlyList<string> RequiredJsonKeys,
    IReadOnlyList<string>? AdditionalLegacyContents = null);

public static partial class PromptTemplateCatalog
{
    private static readonly SystemPromptTemplateDefinition[] Definitions =
    [
        new(
            PromptTemplateScene.MatchingReview,
            "matching-review",
            "智能填充复核",
            "用于智能填充流程中的 AI 复核。",
            MatchingReviewDefaultContent,
            MatchingReviewLegacyDefaultContent,
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "baseScore", "scoreDetailsJson", "workflowScene"],
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
                "reviewTrigger",
                "evidenceSummaryJson",
                "conflictSummaryJson",
                "workflowScene"
            ],
            ["score", "reason", "commentary"]),
        new(
            PromptTemplateScene.ImportDuplicateReview,
            "import-duplicate-review",
            "导入重复复核",
            "用于导入重复检查流程中的 LLM 复核。",
            ImportDuplicateReviewDefaultContent,
            ImportDuplicateReviewLegacyDefaultContent,
            ["sourceProject", "sourceSpecification", "bestMatchProject", "bestMatchSpecification", "baseScore", "scoreDetailsJson", "workflowScene"],
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
                "reviewTrigger",
                "evidenceSummaryJson",
                "conflictSummaryJson",
                "workflowScene"
            ],
            ["score", "reason", "commentary"]),
        new(
            PromptTemplateScene.MatchingEquivalenceAdjudication,
            "matching-equivalence-adjudication",
            "智能填充等价裁决",
            "用于智能填充当前最佳候选的等价表达裁决门禁。",
            MatchingEquivalenceAdjudicationDefaultContent,
            MatchingEquivalenceAdjudicationLegacyDefaultContent,
            ["sourceProject", "sourceSpecification", "candidateProject", "candidateSpecification", "currentDecision", "scoreDetailsJson", "evidenceSummaryJson", "conflictSummaryJson"],
            ["sourceProject", "sourceSpecification", "candidateProject", "candidateSpecification", "candidateAcceptance", "candidateRemark", "currentDecision", "scoreDetailsJson", "evidenceSummaryJson", "conflictSummaryJson"],
            ["verdict", "reasonType", "reason", "confidence"],
            AdditionalLegacyContents:
            [
                MatchingEquivalenceAdjudicationV1Content,
                MatchingEquivalenceAdjudicationV2Content,
                MatchingEquivalenceAdjudicationV3Content,
                MatchingEquivalenceAdjudicationV4Content
            ]),
        new(
            PromptTemplateScene.MatchingCandidateRerank,
            "matching-candidate-rerank",
            "智能填充候选重排",
            "用于智能填充 TopK 候选中的 AI 改选。",
            MatchingCandidateRerankDefaultContent,
            null,
            ["sourceProject", "sourceSpecification", "currentTopCandidateSpecId", "candidatesJson"],
            ["sourceProject", "sourceSpecification", "currentTopCandidateSpecId", "candidatesJson"],
            ["selectedSpecId", "reason", "confidence"]),
        new(
            PromptTemplateScene.SmartConfigStructureRecognition,
            "smart-config-structure-recognition",
            "智能结构识别裁决",
            "用于上传文档结构识别流程中的表格、表头和字段映射裁决。",
            SmartConfigStructureRecognitionDefaultContent,
            null,
            ["workflowScene", "documentTablesJson", "ruleCandidatesJson"],
            ["workflowScene", "documentTablesJson", "ruleCandidatesJson"],
            ["tables", "confidence", "decision"]),
        new(
            PromptTemplateScene.SmartConfigColumnSemanticRecall,
            "smart-config-column-semantic-recall",
            "智能结构列语义召回",
            "用于智能结构识别中未映射表头的字段候选建议。",
            SmartConfigColumnSemanticRecallDefaultContent,
            null,
            ["inputJson"],
            ["inputJson"],
            ["suggestions"])
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
