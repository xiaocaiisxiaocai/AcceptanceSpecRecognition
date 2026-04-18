namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

internal static class ReviewScenarioSamples
{
    public const string ApprovedSourceProject = "PanelLine";
    public const string ApprovedSourceSpecification = "AGV Dock-Bay";
    public const string ApprovedBestProject = "PanelLine";
    public const string ApprovedBestSpecification = "AGV Dock Bay";
    public const string ApprovedAltProject = "panelline";
    public const string ApprovedAltBestSpecification = "agv dock bay";

    public const string FailingSourceProject = "FaultLine";
    public const string FailingSourceSpecification = "Robot Dock-Bay";
    public const string FailingBestProject = "FaultLine";
    public const string FailingBestSpecification = "Robot Dock Bay";
    public const string FailingAltProject = "faultline";
    public const string FailingAltBestSpecification = "robot dock bay";
}
