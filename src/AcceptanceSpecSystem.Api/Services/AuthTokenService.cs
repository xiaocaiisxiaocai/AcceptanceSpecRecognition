using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AcceptanceSpecSystem.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// JWT Token 服务
/// </summary>
public class AuthTokenService : IAuthTokenService
{
    private const string TokenTypeClaim = "token_type";
    private readonly JwtAuthOptions _options;
    private readonly TokenValidationParameters _validationParameters;
    private readonly SigningCredentials _signingCredentials;

    public AuthTokenService(IOptions<JwtAuthOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("JwtAuth:SigningKey 至少需要 32 个字符");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _validationParameters = BuildValidationParameters(key);
    }

    public AuthTokenPair CreateTokenPair(AuthTokenUser user)
    {
        var now = DateTime.UtcNow;
        var accessExpires = now.AddMinutes(Math.Max(5, _options.AccessTokenMinutes));
        var refreshExpires = now.AddDays(Math.Max(1, _options.RefreshTokenDays));

        var commonClaims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new("user_id", user.UserId.ToString()),
            new("company_id", user.CompanyId.ToString()),
            new("permission_version", user.PermissionVersion.ToString()),
            new(ClaimTypes.NameIdentifier, user.Username),
            new(ClaimTypes.Name, user.Username)
        };

        foreach (var role in user.Roles.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            commonClaims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in user.Permissions.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            commonClaims.Add(new Claim("permission", permission));
        }

        var accessToken = CreateToken(
            claims: [.. commonClaims, new Claim(TokenTypeClaim, "access")],
            expires: accessExpires);

        var refreshToken = CreateToken(
            claims: [.. commonClaims, new Claim(TokenTypeClaim, "refresh")],
            expires: refreshExpires);

        return new AuthTokenPair
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = accessExpires
        };
    }

    public ClaimsPrincipal? ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(refreshToken, _validationParameters, out _);
            var tokenType = principal.FindFirstValue(TokenTypeClaim);
            if (!string.Equals(tokenType, "refresh", StringComparison.OrdinalIgnoreCase))
                return null;

            return principal;
        }
        catch
        {
            return null;
        }
    }

    private string CreateToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters BuildValidationParameters(SecurityKey key)
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }
}
