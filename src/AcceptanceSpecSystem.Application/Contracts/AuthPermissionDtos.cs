using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

/// <summary>
/// 权限列表项 DTO。
/// </summary>
public sealed class AuthPermissionListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PermissionType PermissionType { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}
