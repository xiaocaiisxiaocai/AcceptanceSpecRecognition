using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 系统初始化默认账号配置
/// </summary>
public sealed class AuthSeedOptions
{
    public const string SectionName = "AuthSeed";

    public string? AdminPassword { get; init; }

    public string? CommonPassword { get; init; }
}

/// <summary>
/// 默认账号口令配置校验器。
/// </summary>
public sealed class AuthSeedOptionsValidator : IValidateOptions<AuthSeedOptions>
{
    private const int MinimumPasswordLength = 4;
    private const int MaximumPasswordLength = 200;

    private readonly IHostEnvironment _hostEnvironment;

    public AuthSeedOptionsValidator(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    public ValidateOptionsResult Validate(string? name, AuthSeedOptions options)
    {
        var failures = new List<string>();

        var isNonProductionRelaxedEnvironment =
            _hostEnvironment.IsDevelopment() || _hostEnvironment.IsEnvironment("Testing");

        if (!isNonProductionRelaxedEnvironment)
        {
            ValidatePassword(nameof(AuthSeedOptions.AdminPassword), options.AdminPassword, failures);
            ValidatePassword(nameof(AuthSeedOptions.CommonPassword), options.CommonPassword, failures);

            if (string.IsNullOrWhiteSpace(options.AdminPassword))
            {
                failures.Add($"{AuthSeedOptions.SectionName}:AdminPassword 生产环境不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.CommonPassword))
            {
                failures.Add($"{AuthSeedOptions.SectionName}:CommonPassword 生产环境不能为空");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePassword(string optionName, string? password, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var passwordLength = password.Trim().Length;
        if (passwordLength < MinimumPasswordLength || passwordLength > MaximumPasswordLength)
        {
            failures.Add(
                $"{AuthSeedOptions.SectionName}:{optionName} 长度必须在 {MinimumPasswordLength} 到 {MaximumPasswordLength} 位之间");
        }

        if (ProductionSecretGuard.IsKnownPlaceholder(password))
        {
            failures.Add($"{AuthSeedOptions.SectionName}:{optionName} 不能使用示例值或已知占位符");
        }
    }
}
