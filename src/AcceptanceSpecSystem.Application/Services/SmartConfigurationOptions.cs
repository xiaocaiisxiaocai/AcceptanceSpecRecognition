namespace AcceptanceSpecSystem.Application.Services;

public sealed class SmartConfigurationOptions
{
    public const string SectionName = "SmartConfiguration";

    public int StructureAdjudicationTimeoutSeconds { get; set; } = 20;

    public double AutoApplyConfidenceThreshold { get; set; } = 0.85;

    public double MinimumSpecificationNonEmptyRate { get; set; } = 0.5;

    public int GlobalRulePromotionCustomerThreshold { get; set; } = 2;

    public int MaxStructureAdjudicationCallsPerDocument { get; set; } = 5;

    public int MaxColumnSemanticRecallCallsPerDocument { get; set; } = 5;

    public int HeaderDetectionScanRowLimit { get; set; } = 20;

    public int MaxHeaderRowCount { get; set; } = 6;
}
