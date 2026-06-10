namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public sealed class SemanticKernelOptions
{
    public const string SectionName = "SemanticKernel";

    /// <summary>
    /// Azure OpenAI API 版本，默认使用稳定版。
    /// </summary>
    public string AzureOpenAIApiVersion { get; set; } = "2024-10-21";
}
