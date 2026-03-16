using AcceptanceSpecSystem.Api.Middleware;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 添加控制器
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

// 配置Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "验收规格管理系统 API",
        Version = "v1",
        Description = "验收规格管理系统的RESTful API接口"
    });
});

// HttpClient（用于AI连接测试等外部调用）
builder.Services.AddHttpClient();

// 注册DataProtection（用于ApiKey加密存储）
builder.Services.AddDataProtection();

// 配置MySQL数据库连接
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? AppDbContext.DefaultConnectionString;
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 注册UnitOfWork
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// 文件存储服务（服务器文件系统）
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();

// 注册文档服务
builder.Services.AddSingleton<DocumentServiceFactory>();
builder.Services.AddScoped<IFileCompareService, FileCompareService>();

// 注册匹配服务（Semantic Kernel）
builder.Services.AddScoped<AiServiceSelector>();
builder.Services.AddSingleton<ISemanticKernelServiceFactory, SemanticKernelServiceFactory>();
builder.Services.AddScoped<IEmbeddingService, SemanticKernelEmbeddingService>();
builder.Services.AddSingleton<ITextSimilarityService, TextSimilarityService>();
builder.Services.AddScoped<IMatchingService, SemanticKernelMatchingService>();
builder.Services.AddScoped<ILlmReviewService, LlmMatchingAssistService>();
builder.Services.AddScoped<ILlmSuggestionService, LlmMatchingAssistService>();

// 文本处理（Core 4.1）
builder.Services.AddScoped<IChineseConversionService, OpenCcChineseConversionService>();
builder.Services.AddScoped<IOkNgConversionService, OkNgConversionService>();
builder.Services.AddScoped<ISynonymService, SynonymService>();
builder.Services.AddScoped<IKeywordService, KeywordService>();
builder.Services.AddScoped<ITextPreprocessingPipeline, DefaultTextPreprocessingPipeline>();

// 配置CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 启动时应用数据库迁移（避免运行期出现字段缺失）
// 测试环境下由测试工厂自行控制数据库初始化方式（例如 SQLite in-memory）
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

// 使用异常处理中间件
app.UseExceptionHandling();

// 配置HTTP请求管道
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "验收规格管理系统 API v1");
        options.RoutePrefix = "swagger";
    });
}

// 使用CORS
app.UseCors("AllowVueFrontend");

// 使用路由和控制器
app.UseAuthorization();
app.MapControllers();

// 健康检查端点
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("System");

app.Run();

// For integration tests (WebApplicationFactory)
public partial class Program { }
