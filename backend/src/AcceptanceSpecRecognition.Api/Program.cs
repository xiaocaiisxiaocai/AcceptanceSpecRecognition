using System.Text.Json.Serialization;
using AcceptanceSpecRecognition.Core.Services;
using AcceptanceSpecRecognition.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS for React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 注册HttpClient工厂，避免Socket耗尽问题
builder.Services.AddHttpClient("EmbeddingClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient("LLMClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient("ConfigTestClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// 注册内存缓存 (P0-2)
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 10000; // 最多10000个条目
});

// Register application services
// JsonStorageService 需要先注册，因为 ConfigManager 依赖它
builder.Services.AddSingleton<IJsonStorageService, JsonStorageService>();
// 使用工厂方法注册 ConfigManager，避免构造函数中的同步阻塞
builder.Services.AddSingleton<IConfigManager>(sp =>
{
    var storage = sp.GetRequiredService<IJsonStorageService>();
    var logger = sp.GetRequiredService<ILogger<ConfigManager>>();
    return ConfigManager.Create(storage, logger);
});
// 注册缓存服务 (P0-2)
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<ITextPreprocessor, TextPreprocessor>();
builder.Services.AddSingleton<IKeywordHighlighter, KeywordHighlighter>();
builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
builder.Services.AddSingleton<ILLMService, LLMService>();
// MatchingEngine 现在需要 ILogger，由 DI 容器自动注入
builder.Services.AddSingleton<IMatchingEngine, MatchingEngine>();
builder.Services.AddSingleton<IBatchProcessor, BatchProcessor>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton<IHealthCheckService, HealthCheckService>();
builder.Services.AddSingleton<IFeedbackLearningService, FeedbackLearningService>();

var app = builder.Build();

// 确保配置在应用启动前加载完成
var configManager = app.Services.GetRequiredService<IConfigManager>();
await configManager.EnsureInitializedAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapControllers();

app.Run();
