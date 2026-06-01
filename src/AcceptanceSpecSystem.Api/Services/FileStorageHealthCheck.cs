using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class FileStorageHealthCheck : IHealthCheck
{
    private readonly IFileStorageService _fileStorageService;

    public FileStorageHealthCheck(IFileStorageService fileStorageService)
    {
        _fileStorageService = fileStorageService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var relativePath = await _fileStorageService.WriteHealthCheckFileAsync(cancellationToken);
            await _fileStorageService.DeleteIfExistsAsync(relativePath, cancellationToken);
            return HealthCheckResult.Healthy("文件存储目录可写");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("文件存储目录不可写", ex);
        }
    }
}
