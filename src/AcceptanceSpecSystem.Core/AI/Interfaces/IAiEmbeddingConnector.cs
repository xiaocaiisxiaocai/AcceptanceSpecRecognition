using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Interfaces;

public interface IAiEmbeddingConnector
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface IAiEmbeddingConnectorFactory
{
    IAiEmbeddingConnector Create(AiServiceConfig config);
}

