using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class DefaultTextPreprocessingPipeline : ITextPreprocessingPipeline
{
    private readonly ITextProcessingConfigProvider _configProvider;
    private readonly IChineseConversionService _chinese;
    private readonly IOkNgConversionService _okNg;
    private readonly ISynonymService _synonyms;

    public DefaultTextPreprocessingPipeline(
        ITextProcessingConfigProvider configProvider,
        IChineseConversionService chinese,
        IOkNgConversionService okNg,
        ISynonymService synonyms)
    {
        _configProvider = configProvider;
        _chinese = chinese;
        _okNg = okNg;
        _synonyms = synonyms;
    }

    public async Task<TextProcessingSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _configProvider.GetConfigAsync(cancellationToken);
        var map = cfg.EnableSynonym
            ? await _synonyms.GetWordToStandardMapAsync(cancellationToken)
            : new Dictionary<string, string>();

        return new TextProcessingSession(cfg, _chinese, _okNg, map);
    }
}

