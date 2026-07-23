using Microsoft.AspNetCore.DataProtection;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class MatchingApprovalTokenProtector : IMatchingApprovalTokenProtector
{
    private readonly IDataProtector _protector;

    public MatchingApprovalTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("MatchingWorkflowSupportService.ReviewApprovalToken.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
