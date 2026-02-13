using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Interfaces;

public interface IAiLlmConnector
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}

public interface IAiLlmConnectorFactory
{
    IAiLlmConnector Create(AiServiceConfig config);
}
