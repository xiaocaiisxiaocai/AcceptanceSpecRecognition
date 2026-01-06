using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 健康检查服务 - 检查系统各组件状态
/// </summary>
public class HealthCheckService : IHealthCheckService
{
    private readonly IConfigManager _configManager;
    private readonly IJsonStorageService _storageService;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILLMService _llmService;

    public HealthCheckService(
        IConfigManager configManager,
        IJsonStorageService storageService,
        IEmbeddingService embeddingService,
        ILLMService llmService)
    {
        _configManager = configManager;
        _storageService = storageService;
        _embeddingService = embeddingService;
        _llmService = llmService;
    }

    public async Task<HealthCheckResult> CheckAllAsync()
    {
        var result = new HealthCheckResult
        {
            Timestamp = DateTime.UtcNow,
            Checks = new List<ComponentHealth>()
        };

        // 检查配置
        result.Checks.Add(await CheckConfigAsync());

        // 检查数据文件
        result.Checks.Add(await CheckDataFilesAsync());

        // 检查Embedding服务
        result.Checks.Add(await CheckEmbeddingServiceAsync());

        // 检查LLM服务
        result.Checks.Add(await CheckLLMServiceAsync());

        // 计算总体状态
        result.IsHealthy = result.Checks.All(c => c.Status == HealthStatus.Healthy || c.Status == HealthStatus.Degraded);
        result.Status = result.Checks.Any(c => c.Status == HealthStatus.Unhealthy)
            ? HealthStatus.Unhealthy
            : result.Checks.Any(c => c.Status == HealthStatus.Degraded)
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        return result;
    }

    public async Task<ComponentHealth> CheckEmbeddingServiceAsync()
    {
        var health = new ComponentHealth
        {
            Component = "EmbeddingService",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var config = _configManager.GetAll();
            if (string.IsNullOrEmpty(config.Embedding?.ApiKey))
            {
                health.Status = HealthStatus.Degraded;
                health.Message = "Embedding API密钥未配置，使用模拟模式";
                return health;
            }

            // 尝试生成一个简单的向量
            var testVector = await _embeddingService.EmbedAsync("健康检查测试");
            if (testVector != null && testVector.Length > 0)
            {
                health.Status = HealthStatus.Healthy;
                health.Message = $"Embedding服务正常，向量维度: {testVector.Length}";
            }
            else
            {
                health.Status = HealthStatus.Unhealthy;
                health.Message = "Embedding服务返回空向量";
            }
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"Embedding服务异常: {ex.Message}";
        }

        return health;
    }

    public async Task<ComponentHealth> CheckLLMServiceAsync()
    {
        var health = new ComponentHealth
        {
            Component = "LLMService",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var config = _configManager.GetAll();
            if (string.IsNullOrEmpty(config.LLM?.ApiKey))
            {
                health.Status = HealthStatus.Degraded;
                health.Message = "LLM API密钥未配置，使用规则模式";
                return health;
            }

            var modelInfo = _llmService.GetModelInfo();
            health.Status = HealthStatus.Healthy;
            health.Message = $"LLM服务正常，模型: {modelInfo.Name}";
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"LLM服务异常: {ex.Message}";
        }

        return await Task.FromResult(health);
    }

    public async Task<ComponentHealth> CheckDataFilesAsync()
    {
        var health = new ComponentHealth
        {
            Component = "DataFiles",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var requiredFiles = new[]
            {
                "history_records.json",
                "keywords.json",
                "config.json"
            };

            var missingFiles = new List<string>();
            var existingFiles = new List<string>();

            foreach (var file in requiredFiles)
            {
                try
                {
                    // 尝试读取文件
                    var content = await _storageService.ReadAsync<object>(file);
                    if (content != null)
                    {
                        existingFiles.Add(file);
                    }
                    else
                    {
                        missingFiles.Add(file);
                    }
                }
                catch
                {
                    missingFiles.Add(file);
                }
            }

            if (missingFiles.Count == 0)
            {
                health.Status = HealthStatus.Healthy;
                health.Message = $"所有数据文件正常 ({existingFiles.Count}个文件)";
            }
            else if (missingFiles.Count < requiredFiles.Length)
            {
                health.Status = HealthStatus.Degraded;
                health.Message = $"部分数据文件缺失: {string.Join(", ", missingFiles)}";
            }
            else
            {
                health.Status = HealthStatus.Unhealthy;
                health.Message = "所有数据文件缺失";
            }
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"数据文件检查异常: {ex.Message}";
        }

        return health;
    }

    public async Task<ComponentHealth> CheckConfigAsync()
    {
        var health = new ComponentHealth
        {
            Component = "Configuration",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var config = _configManager.GetAll();
            if (config == null)
            {
                health.Status = HealthStatus.Unhealthy;
                health.Message = "配置加载失败";
                return health;
            }

            var issues = new List<string>();

            if (config.Matching == null)
                issues.Add("匹配配置缺失");
            else
            {
                if (config.Matching.MatchSuccessThreshold <= 0 || config.Matching.MatchSuccessThreshold > 1)
                    issues.Add("匹配成功阈值配置无效");
            }

            if (issues.Count == 0)
            {
                health.Status = HealthStatus.Healthy;
                health.Message = $"配置正常，版本: {config.Version}";
            }
            else
            {
                health.Status = HealthStatus.Degraded;
                health.Message = $"配置问题: {string.Join("; ", issues)}";
            }
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Unhealthy;
            health.Message = $"配置检查异常: {ex.Message}";
        }

        return await Task.FromResult(health);
    }
}
