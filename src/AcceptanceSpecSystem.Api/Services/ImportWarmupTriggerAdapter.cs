namespace AcceptanceSpecSystem.Api.Services;

public sealed class ImportWarmupTriggerAdapter : IImportWarmupTrigger
{
    private readonly IEmbeddingCacheWarmupTrigger _trigger;

    public ImportWarmupTriggerAdapter(IEmbeddingCacheWarmupTrigger trigger)
    {
        _trigger = trigger;
    }

    public bool Request() => _trigger.Request();
}
