using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using AcceptanceSpecSystem.Api;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Middleware;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using AcceptanceSpecSystem.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Providers;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<AuditOperationFilter>();

// 添加控制器
builder.Services.AddControllers(options =>
    {
        options.Filters.AddService<AuditOperationFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var messages = context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "请求参数验证失败"
                : error.ErrorMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct()
            .ToArray();
        var message = messages.Length == 0
            ? "请求参数验证失败"
            : string.Join("；", messages);

        return new BadRequestObjectResult(ApiResponse.Error(StatusCodes.Status400BadRequest, message));
    };
});

// 配置Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "验收规格管理系统 API",
        Version = "v1",
        Description = "验收规格管理系统的RESTful API接口"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT 认证头，格式：Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// HttpClient（用于AI连接测试等外部调用）
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(AiServiceHttpClientDefaults.OllamaNativeChatClientName, client =>
{
    // Ollama 慢模型推理可能超过 .NET 默认 100 秒，超时由外层业务 CancellationToken 控制。
    client.Timeout = AiServiceHttpClientDefaults.LongRunningNetworkTimeout;
});

// 注册DataProtection（用于ApiKey加密存储）
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]?.Trim();
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys");
}

var fullDataProtectionKeysPath = Path.IsPathRooted(dataProtectionKeysPath)
    ? Path.GetFullPath(dataProtectionKeysPath)
    : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, dataProtectionKeysPath));
Directory.CreateDirectory(fullDataProtectionKeysPath);

builder.Services.AddDataProtection()
    .SetApplicationName("AcceptanceSpecSystem")
    .PersistKeysToFileSystem(new DirectoryInfo(fullDataProtectionKeysPath));

builder.Services.AddMemoryCache();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = UploadFileValidation.MaxAllowedFileSizeBytes * 10;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => CreateFixedWindowLimiter(
        httpContext,
        builder.Configuration.GetSection($"{ApiRateLimitOptions.SectionName}:Login").Get<RateLimitPolicyOptions>()
            ?? new ApiRateLimitOptions().Login));
    options.AddPolicy("upload", httpContext => CreateFixedWindowLimiter(
        httpContext,
        builder.Configuration.GetSection($"{ApiRateLimitOptions.SectionName}:Upload").Get<RateLimitPolicyOptions>()
            ?? new ApiRateLimitOptions().Upload));
    options.AddPolicy("ai-heavy", httpContext => CreateFixedWindowLimiter(
        httpContext,
        builder.Configuration.GetSection($"{ApiRateLimitOptions.SectionName}:AiHeavy").Get<RateLimitPolicyOptions>()
            ?? new ApiRateLimitOptions().AiHeavy));
});

// 配置MySQL数据库连接
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")?.Trim();
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection 未配置，当前分支禁止回退到硬编码默认数据库。");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<FileStorageHealthCheck>("fileStorage")
    .AddCheck<AiConfigHealthCheck>("aiConfig");

// 模块化服务注册
builder.Services.AddDataRepositories();
builder.Services.AddAcceptanceApplicationLayer();
builder.Services.AddApiLayerServices(builder.Configuration);

var jwtOptions = builder.Configuration.GetSection(JwtAuthOptions.SectionName).Get<JwtAuthOptions>()
    ?? new JwtAuthOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException("JwtAuth:SigningKey 至少需要 32 个字符");
}
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var validationService = context.HttpContext.RequestServices
                    .GetRequiredService<IAuthSessionValidationService>();
                var result = await validationService.ValidateAccessTokenAsync(
                    context.Principal,
                    context.HttpContext.RequestAborted);
                if (!result.IsValid)
                {
                    context.Fail(result.Message);
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// 配置CORS
string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? [];

if (allowedOrigins.Length == 0 || allowedOrigins.Contains("*", StringComparer.Ordinal))
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        allowedOrigins = ["http://localhost"];
    }
    else
    {
        throw new InvalidOperationException("Cors:AllowedOrigins 必须配置显式来源，禁止留空或使用通配符 *");
    }
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVueFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
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

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<SystemPromptTemplateInitializer>();
    await initializer.EnsureAsync();
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

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1", out var remainingPath))
    {
        // v1 路径先作为兼容别名接入，保留旧 /api 路由给现有前端和脚本继续使用。
        context.Request.Path = $"/api{remainingPath}";
    }

    await next();
});

// 使用CORS
app.UseRouting();
app.UseCors("AllowVueFrontend");

// 使用路由和控制器
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ApiPermissionMiddleware>();
app.UseAuthorization();
app.MapControllers();

await AuthUserSeedService.EnsureSeedUsersAsync(app.Services, app.Logger);

// 健康检查端点
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                entries = report.Entries.ToDictionary(
                    item => item.Key,
                    item => new
                    {
                        status = item.Value.Status.ToString(),
                        description = item.Value.Description,
                        durationMs = item.Value.Duration.TotalMilliseconds
                    })
            };
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    })
    .WithName("HealthCheck")
    .WithTags("System")
    .AllowAnonymous();

app.Run();

static RateLimitPartition<string> CreateFixedWindowLimiter(
    HttpContext httpContext,
    RateLimitPolicyOptions options)
{
    var partitionKey =
        httpContext.User?.Identity?.IsAuthenticated == true
            ? httpContext.User.Identity.Name
            : httpContext.Connection.RemoteIpAddress?.ToString();
    partitionKey = string.IsNullOrWhiteSpace(partitionKey) ? "anonymous" : partitionKey;

    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(1, options.PermitLimit),
        Window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)),
        QueueLimit = Math.Max(0, options.QueueLimit),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
    });
}

// For integration tests (WebApplicationFactory)
public partial class Program { }
