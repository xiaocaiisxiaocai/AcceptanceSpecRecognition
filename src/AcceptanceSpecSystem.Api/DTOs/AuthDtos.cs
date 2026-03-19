namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 登录请求
/// </summary>
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 刷新Token请求
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// 前端登录返回结构（兼容 PureAdmin 默认 user.ts）
/// </summary>
public class FrontendAuthResponse<TData>
{
    public bool Success { get; set; }

    public TData? Data { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// 登录成功数据
/// </summary>
public class LoginSuccessData
{
    public string Avatar { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public List<string> Permissions { get; set; } = [];

    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime Expires { get; set; }
}

/// <summary>
/// 刷新Token成功数据
/// </summary>
public class RefreshTokenSuccessData
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime Expires { get; set; }
}
