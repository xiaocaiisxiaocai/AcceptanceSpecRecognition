namespace AcceptanceSpecSystem.Core.Matching.Models;

public enum PromptTemplateScene
{
    Unknown = 0,
    MatchingReview = 1,
    ImportDuplicateReview = 2,
    MatchingGenerate = 3,
    MatchingKnowledgeGenerate = 4,
    MatchingEntityResolution = 5,
    MatchingEquivalenceAdjudication = 6
}

public class PromptTemplateModel
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;
}
