using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAuthLoginAppService
{
    Task<AuthLoginResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record AuthLoginResult(
    AuthLoginStatus Status,
    AuthAccessContext? Access = null);

public enum AuthLoginStatus
{
    Success,
    InvalidCredentials,
    AccessDenied
}

/// <summary>
/// 登录凭据校验与访问上下文装配用例。HTTP Cookie/JWT 映射仍由 Api 适配器负责。
/// </summary>
public sealed class AuthLoginAppService : IAuthLoginAppService
{
    private readonly ISystemUserRepository _users;
    private readonly IAuthPasswordService _passwords;
    private readonly IAuthAccessService _access;

    public AuthLoginAppService(
        ISystemUserRepository users,
        IAuthPasswordService passwords,
        IAuthAccessService access)
    {
        _users = users;
        _passwords = passwords;
        _access = access;
    }

    public async Task<AuthLoginResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByUsernameAsync(username);
        if (user == null || !user.IsActive || !_passwords.VerifyPassword(user.PasswordHash, password))
            return new(AuthLoginStatus.InvalidCredentials);

        var access = await _access.GetByUsernameAsync(username, cancellationToken);
        return access == null || !access.IsActive
            ? new(AuthLoginStatus.AccessDenied)
            : new(AuthLoginStatus.Success, access);
    }
}
