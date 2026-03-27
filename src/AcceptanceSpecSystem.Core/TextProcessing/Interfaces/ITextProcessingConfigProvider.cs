using AcceptanceSpecSystem.Core.TextProcessing.Models;

namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface ITextProcessingConfigProvider
{
    Task<TextProcessingConfigModel> GetConfigAsync(CancellationToken cancellationToken = default);
}
