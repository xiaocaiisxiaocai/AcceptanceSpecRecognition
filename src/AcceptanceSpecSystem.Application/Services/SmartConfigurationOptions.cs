namespace AcceptanceSpecSystem.Application.Services;

public sealed class SmartConfigurationOptions
{
    public const string SectionName = "SmartConfiguration";

    public int StructureAdjudicationTimeoutSeconds { get; set; } = 20;

    public double AutoApplyConfidenceThreshold { get; set; } = 0.85;

    public double MinimumSpecificationNonEmptyRate { get; set; } = 0.5;

    public int GlobalRulePromotionCustomerThreshold { get; set; } = 2;
}
