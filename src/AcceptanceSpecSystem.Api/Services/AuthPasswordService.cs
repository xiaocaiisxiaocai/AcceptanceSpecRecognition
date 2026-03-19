using System.Security.Cryptography;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 登录密码校验服务（仅支持 PBKDF2 密码哈希）
/// </summary>
public class AuthPasswordService : IAuthPasswordService
{
    private const int DefaultIterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string HashPassword(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword))
            throw new ArgumentException("密码不能为空", nameof(plainPassword));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var derive = new Rfc2898DeriveBytes(
            password: plainPassword,
            salt: salt,
            iterations: DefaultIterations,
            hashAlgorithm: HashAlgorithmName.SHA256);
        var hash = derive.GetBytes(HashSize);

        return $"pbkdf2-sha256${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string passwordHash, string inputPassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrEmpty(inputPassword))
            return false;

        return VerifyPbkdf2Hash(passwordHash, inputPassword);
    }

    private static bool VerifyPbkdf2Hash(string passwordHash, string inputPassword)
    {
        try
        {
            var parts = passwordHash.Split('$');
            if (parts.Length != 4)
                return false;

            var algorithm = parts[0];
            if (!string.Equals(algorithm, "pbkdf2-sha256", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!int.TryParse(parts[1], out var iterations) || iterations < 10_000)
                return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            if (salt.Length == 0 || expectedHash.Length == 0)
                return false;

            using var derive = new Rfc2898DeriveBytes(
                password: inputPassword,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256);
            var actualHash = derive.GetBytes(expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
