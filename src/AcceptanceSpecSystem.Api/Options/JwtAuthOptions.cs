namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// JWT 鉴权配置
/// </summary>
public class JwtAuthOptions
{
    public const string SectionName = "JwtAuth";

    /// <summary>
    /// 签发者
    /// </summary>
    public string Issuer { get; set; } = "AcceptanceSpecSystem";

    /// <summary>
    /// 受众
    /// </summary>
    public string Audience { get; set; } = "AcceptanceSpecSystem.Web";

    /// <summary>
    /// 对称签名密钥（至少 32 字符）
    /// </summary>
    public string SigningKey { get; set; } = "ChangeThisToLongRandomSecretAtLeast32Chars";

    /// <summary>
    /// AccessToken 有效期（分钟）
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 120;

    /// <summary>
    /// RefreshToken 有效期（天）
    /// </summary>
    public int RefreshTokenDays { get; set; } = 7;
}
