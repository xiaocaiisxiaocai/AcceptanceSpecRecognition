namespace AcceptanceSpecSystem.Core.Matching.Models;

public enum PromptTemplateScene
{
    Unknown = 0,
    MatchingReview = 1,
    ImportDuplicateReview = 2,
    MatchingGenerate = 3
}

public class PromptTemplateModel
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;
}
