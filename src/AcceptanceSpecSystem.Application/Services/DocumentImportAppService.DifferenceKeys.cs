using System.Text.Json;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private static readonly TimeSpan ImportDecisionLifetime = TimeSpan.FromHours(2);

    private Dictionary<string, PendingDecisionEntry> BuildPendingDecisionMap(
        SpecAccessContext scope,
        int fileId,
        int sourceIndex,
        int customerId,
        int? processId,
        int? machineModelId,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys)
    {
        var result = new Dictionary<string, PendingDecisionEntry>(StringComparer.Ordinal);
        AddPendingDecisions(confirmedDifferenceKeys, DifferenceDecision.Import);
        AddPendingDecisions(partiallyConfirmedDifferenceKeys, DifferenceDecision.PartialImport);
        AddPendingDecisions(skippedDifferenceKeys, DifferenceDecision.Skip);
        return result;

        void AddPendingDecisions(IEnumerable<string>? keys, DifferenceDecision decision)
        {
            foreach (var key in keys ?? [])
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var entry = ParsePendingDecisionEntry(
                    key,
                    decision,
                    scope,
                    fileId,
                    sourceIndex,
                    customerId,
                    processId,
                    machineModelId);
                if (!result.TryAdd(entry.LookupKey, entry))
                {
                    throw new ApplicationServiceException(400, "同一差异项存在重复或冲突的确认决议，请重新确认");
                }
            }
        }
    }

    private PendingDecisionEntry ParsePendingDecisionEntry(
        string protectedKey,
        DifferenceDecision decision,
        SpecAccessContext scope,
        int fileId,
        int sourceIndex,
        int customerId,
        int? processId,
        int? machineModelId)
    {
        ImportDecisionTokenPayload payload;
        try
        {
            var json = _decisionTokenProtector.Unprotect(protectedKey);
            payload = JsonSerializer.Deserialize<ImportDecisionTokenPayload>(json)
                ?? throw new InvalidOperationException("确认令牌为空");
        }
        catch (Exception ex) when (ex is not ApplicationServiceException)
        {
            throw new ApplicationServiceException(400, "差异确认令牌无效或已损坏，请重新发起导入");
        }

        if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ApplicationServiceException(400, "差异确认令牌已过期，请重新发起导入");
        }

        if (payload.UserId != scope.UserId ||
            payload.CompanyId != scope.CompanyId ||
            payload.FileId != fileId ||
            payload.SourceIndex != sourceIndex ||
            payload.CustomerId != customerId ||
            payload.ProcessId != processId ||
            payload.MachineModelId != machineModelId)
        {
            throw new ApplicationServiceException(400, "差异确认令牌与当前文件或导入范围不一致，请重新发起导入");
        }

        if (payload.RowIndex < 0 || payload.ExistingSpecId <= 0 || string.IsNullOrWhiteSpace(payload.MatchType))
        {
            throw new ApplicationServiceException(400, "差异确认令牌内容不完整，请重新发起导入");
        }

        return new PendingDecisionEntry
        {
            LookupKey = BuildPendingDecisionLookupKey(
                payload.SourceIndex,
                payload.RowIndex,
                payload.Project,
                payload.Specification,
                payload.Acceptance,
                payload.Remark),
            MatchType = payload.MatchType,
            ExistingSpecId = payload.ExistingSpecId,
            Decision = decision
        };
    }

    private static string BuildPendingDecisionLookupKey(
        int tableIndex,
        int rowIndex,
        string normalizedProject,
        string normalizedSpecification,
        string normalizedAcceptance,
        string normalizedRemark)
    {
        return $"{tableIndex}|{rowIndex}|{normalizedProject}|{normalizedSpecification}|{normalizedAcceptance}|{normalizedRemark}";
    }

    private string BuildDifferenceKey(
        ImportExecutionContext context,
        int sourceIndex,
        int rowIndex,
        string matchType,
        int existingSpecId,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        var now = DateTimeOffset.UtcNow;
        return _decisionTokenProtector.Protect(JsonSerializer.Serialize(new ImportDecisionTokenPayload
        {
            UserId = context.UserId,
            CompanyId = context.CompanyId,
            FileId = context.FileId,
            SourceIndex = sourceIndex,
            CustomerId = context.CustomerId,
            ProcessId = context.ProcessId,
            MachineModelId = context.MachineModelId,
            RowIndex = rowIndex,
            MatchType = matchType,
            ExistingSpecId = existingSpecId,
            Project = project,
            Specification = specification,
            Acceptance = acceptance,
            Remark = remark,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(ImportDecisionLifetime)
        }));
    }

    private sealed class ImportDecisionTokenPayload
    {
        public int UserId { get; init; }
        public int CompanyId { get; init; }
        public int FileId { get; init; }
        public int SourceIndex { get; init; }
        public int CustomerId { get; init; }
        public int? ProcessId { get; init; }
        public int? MachineModelId { get; init; }
        public int RowIndex { get; init; }
        public string MatchType { get; init; } = string.Empty;
        public int ExistingSpecId { get; init; }
        public string Project { get; init; } = string.Empty;
        public string Specification { get; init; } = string.Empty;
        public string Acceptance { get; init; } = string.Empty;
        public string Remark { get; init; } = string.Empty;
        public DateTimeOffset IssuedAtUtc { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
