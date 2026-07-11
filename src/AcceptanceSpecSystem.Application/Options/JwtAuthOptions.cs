namespace AcceptanceSpecSystem.Application.Options;

/// <summary>
/// JWT 鉴权配置。
/// </summary>
public sealed class JwtAuthOptions
{
    public const string SectionName = "JwtAuth";
    public string Issuer { get; set; } = "AcceptanceSpecSystem";
    public string Audience { get; set; } = "AcceptanceSpecSystem.Web";
    public string SigningKey { get; set; } = "ChangeThisToLongRandomSecretAtLeast32Chars";
    public int AccessTokenMinutes { get; set; } = 120;
    public int RefreshTokenDays { get; set; } = 7;
}
