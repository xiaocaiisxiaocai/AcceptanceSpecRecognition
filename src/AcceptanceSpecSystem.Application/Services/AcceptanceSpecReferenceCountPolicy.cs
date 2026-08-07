using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

internal static class AcceptanceSpecReferenceCountPolicy
{
    public static void ResetIfContentChanged(
        AcceptanceSpec spec,
        string project,
        string specification,
        string? acceptance,
        string? remark)
    {
        if (!string.Equals(NormalizeRequired(spec.Project), NormalizeRequired(project), StringComparison.Ordinal) ||
            !string.Equals(NormalizeRequired(spec.Specification), NormalizeRequired(specification), StringComparison.Ordinal) ||
            !string.Equals(NormalizeOptional(spec.Acceptance), NormalizeOptional(acceptance), StringComparison.Ordinal) ||
            !string.Equals(NormalizeOptional(spec.Remark), NormalizeOptional(remark), StringComparison.Ordinal))
        {
            spec.ReferenceCount = 0;
            spec.ReferenceVersion++;
        }
    }

    private static string NormalizeRequired(string? value) => (value ?? string.Empty).Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
