namespace AcceptanceSpecSystem.Core.TextProcessing.Models;

public sealed record SynonymWordModel(string Word, bool IsStandard);

public sealed record SynonymGroupModel(IReadOnlyList<SynonymWordModel> Words);
