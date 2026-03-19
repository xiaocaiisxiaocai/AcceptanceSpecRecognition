namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 登录密码服务（哈希生成与校验）
/// </summary>
public interface IAuthPasswordService
{
    string HashPassword(string plainPassword);

    bool VerifyPassword(string passwordHash, string inputPassword);
}
