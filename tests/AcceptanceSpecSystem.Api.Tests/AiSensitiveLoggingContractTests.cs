using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AiSensitiveLoggingContractTests
{
    [Theory]
    [InlineData("src/AcceptanceSpecSystem.Api/Controllers/AiServicesController.cs")]
    [InlineData("src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.LlmPolicy.cs")]
    [InlineData("src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.Llm.cs")]
    [InlineData("src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.Embedding.cs")]
    [InlineData("src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelEmbeddingService.cs")]
    public void AiRuntimeLogging_ShouldNotAttachProviderExceptions(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        source.Should().NotMatchRegex(@"Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(\s*(?:ex|exception)\s*,",
            "provider exceptions can contain prompts, response bodies, endpoints, credentials, or customer text");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "openspec")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
