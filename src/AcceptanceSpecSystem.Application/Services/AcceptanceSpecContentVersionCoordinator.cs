using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 统一维护当前正文、版本号、引用计数与不可变快照之间的不变量。
/// </summary>
public sealed class AcceptanceSpecContentVersionCoordinator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly Dictionary<int, string?> _actorNames = [];

    public AcceptanceSpecContentVersionCoordinator(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CreateInitialSnapshotAsync(
        AcceptanceSpec spec,
        string changeSource,
        int? changedByUserId,
        DateTime? changedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        await AddSnapshotAsync(
            spec,
            changeSource,
            changedByUserId,
            changeReason: null,
            restoredFromVersion: null,
            isMigrationBaseline: false,
            changedAtUtc ?? spec.ImportedAt,
            cancellationToken);
    }

    public async Task<bool> ApplyChangeAsync(
        AcceptanceSpec spec,
        string project,
        string specification,
        string? acceptance,
        string? remark,
        string changeSource,
        int? changedByUserId,
        string? changeReason = null,
        long? restoredFromVersion = null,
        DateTime? changedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedProject = NormalizeRequired(project);
        var normalizedSpecification = NormalizeRequired(specification);
        var changed = HasMaterialChange(
            spec,
            normalizedProject,
            normalizedSpecification,
            acceptance,
            remark);

        var now = changedAtUtc ?? DateTime.UtcNow;
        if (!changed)
        {
            spec.UpdatedAt = now;
            return false;
        }

        spec.Project = normalizedProject;
        spec.Specification = normalizedSpecification;
        spec.Acceptance = acceptance;
        spec.Remark = remark;
        spec.ReferenceCount = 0;
        spec.ReferenceVersion++;
        spec.UpdatedAt = now;
        spec.CleanupScanIgnored = false;
        spec.CleanupScanIgnoredAtUtc = null;
        spec.CleanupScanIgnoredByUserId = null;
        spec.CleanupScanIgnoreReason = null;

        await AddSnapshotAsync(
            spec,
            changeSource,
            changedByUserId,
            NormalizeOptional(changeReason),
            restoredFromVersion,
            isMigrationBaseline: false,
            now,
            cancellationToken);
        return true;
    }

    public static bool HasMaterialChange(
        AcceptanceSpec spec,
        string project,
        string specification,
        string? acceptance,
        string? remark)
    {
        return !string.Equals(NormalizeRequired(spec.Project), NormalizeRequired(project), StringComparison.Ordinal) ||
               !string.Equals(NormalizeRequired(spec.Specification), NormalizeRequired(specification), StringComparison.Ordinal) ||
               !string.Equals(NormalizeOptional(spec.Acceptance), NormalizeOptional(acceptance), StringComparison.Ordinal) ||
               !string.Equals(NormalizeOptional(spec.Remark), NormalizeOptional(remark), StringComparison.Ordinal);
    }

    private async Task AddSnapshotAsync(
        AcceptanceSpec spec,
        string changeSource,
        int? changedByUserId,
        string? changeReason,
        long? restoredFromVersion,
        bool isMigrationBaseline,
        DateTime changedAtUtc,
        CancellationToken cancellationToken)
    {
        var actorName = await ResolveActorNameAsync(changedByUserId, cancellationToken);
        await _unitOfWork.AcceptanceSpecContentVersions.AddAsync(
            new AcceptanceSpecContentVersion
            {
                AcceptanceSpec = spec,
                AcceptanceSpecId = spec.Id,
                Version = spec.ReferenceVersion,
                Project = spec.Project,
                Specification = spec.Specification,
                Acceptance = spec.Acceptance,
                Remark = spec.Remark,
                ChangedAtUtc = changedAtUtc,
                ChangedByUserId = changedByUserId,
                ChangedByNameSnapshot = actorName,
                ChangeSource = changeSource,
                ChangeReason = changeReason,
                RestoredFromVersion = restoredFromVersion,
                IsMigrationBaseline = isMigrationBaseline
            },
            cancellationToken);
    }

    private async Task<string?> ResolveActorNameAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue || userId.Value <= 0)
            return null;

        if (_actorNames.TryGetValue(userId.Value, out var cached))
            return cached;

        var name = await _unitOfWork.SystemUsers.Query()
            .Where(user => user.Id == userId.Value)
            .Select(user => string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname)
            .SingleOrDefaultAsync(cancellationToken);
        _actorNames[userId.Value] = name;
        return name;
    }

    private static string NormalizeRequired(string? value) => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
