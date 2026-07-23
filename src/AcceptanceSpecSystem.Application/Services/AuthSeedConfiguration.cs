using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public sealed record AuthSeedConfiguration(
    string? AdminPassword,
    string? CommonPassword)
{
    public const string SectionName = "AuthSeed";
}

public interface IAuthPermissionSeedCatalog
{
    IReadOnlyCollection<AuthPermissionSeedDefinition> GetSeeds();
}

public sealed record AuthPermissionSeedDefinition(
    string Code,
    string Name,
    PermissionType PermissionType,
    string Resource,
    string Action,
    string? RoutePath = null,
    string? HttpMethod = null,
    string? ApiPath = null);
