using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class DefaultTextPreprocessingPipeline : ITextPreprocessingPipeline
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChineseConversionService _chinese;
    private readonly IOkNgConversionService _okNg;
    private readonly ISynonymService _synonyms;

    public DefaultTextPreprocessingPipeline(
        IUnitOfWork unitOfWork,
        IChineseConversionService chinese,
        IOkNgConversionService okNg,
        ISynonymService synonyms)
    {
        _unitOfWork = unitOfWork;
        _chinese = chinese;
        _okNg = okNg;
        _synonyms = synonyms;
    }

    public async Task<TextProcessingSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _unitOfWork.TextProcessingConfigs.GetConfigAsync();
        var map = cfg.EnableSynonym
            ? await _synonyms.GetWordToStandardMapAsync(cancellationToken)
            : new Dictionary<string, string>();

        return new TextProcessingSession(cfg, _chinese, _okNg, map);
    }
}

