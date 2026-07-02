namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 识别来源
/// </summary>
public enum IdentificationSource
{
    /// <summary>历史模板</summary>
    SavedTemplate = 1,

    /// <summary>规则匹配</summary>
    RuleBased = 2,

    /// <summary>LLM 语义理解</summary>
    LlmBased = 3
}
