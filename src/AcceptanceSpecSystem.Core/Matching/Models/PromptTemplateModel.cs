namespace AcceptanceSpecSystem.Core.Matching.Models;

public enum PromptTemplateScene
{
    Unknown = 0,
    MatchingReview = 1,
    ImportDuplicateReview = 2,
    MatchingEquivalenceAdjudication = 6,
    MatchingCandidateRerank = 7,
    SmartConfigStructureRecognition = 8,
    SmartConfigColumnSemanticRecall = 9
}

public class PromptTemplateModel
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;
}
