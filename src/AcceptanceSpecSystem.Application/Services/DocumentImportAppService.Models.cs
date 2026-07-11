using System.Text;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private sealed class ImportExecutionContext
    {
        public required ImportResult Result { get; init; }

        public required List<AcceptanceSpec> ExistingSpecs { get; init; }

        public required List<AcceptanceSpec> PendingInsertedSpecs { get; init; }

        public required List<AcceptanceSpec> SpecsToInsert { get; init; }

        public required HashSet<string> ConfirmedDifferenceKeys { get; init; }

        public required HashSet<string> PartiallyConfirmedDifferenceKeys { get; init; }

        public required HashSet<string> SkippedDifferenceKeys { get; init; }

        public required Dictionary<string, PendingDecisionEntry> PendingDecisionMap { get; init; }

        public required ImportDuplicateDetectionSession DuplicateSession { get; init; }

        public required int CustomerId { get; init; }

        public required int? ProcessId { get; init; }

        public required int? MachineModelId { get; init; }

        public required int FileId { get; init; }

        public required int UserId { get; init; }

        public required int? OwnerOrgUnitId { get; init; }

        public required bool PreviewSkippedRows { get; init; }

        public int OverwriteCount { get; set; }

        public bool SkipSemanticDetection =>
            PendingDecisionMap.Count > 0 ||
            ConfirmedDifferenceKeys.Count > 0 ||
            PartiallyConfirmedDifferenceKeys.Count > 0 ||
            SkippedDifferenceKeys.Count > 0;

        public bool IsConfirmationReplay => SkipSemanticDetection;
    }

    private sealed class PendingDecisionEntry
    {
        public required string LookupKey { get; init; }

        public required string MatchType { get; init; }

        public required int ExistingSpecId { get; init; }

        public required DifferenceDecision Decision { get; init; }
    }

    private enum DifferenceDecision
    {
        Import,
        PartialImport,
        Skip
    }

    private sealed record ImportRowPayload(
        int RowIndex,
        List<string> RowValues,
        string? Project,
        string? Specification,
        string? Acceptance,
        string? Remark);
}
