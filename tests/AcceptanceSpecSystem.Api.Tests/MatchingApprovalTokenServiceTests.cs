using System.Reflection;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingApprovalTokenServiceTests
{
    [Fact]
    public void ResolveBundle_WhenTokenIssuedWithExactMatchOnly_ShouldPreserveExactMatchOnly()
    {
        var provider = DataProtectionProvider.Create("matching-approval-token-tests");
        var service = new MatchingApprovalTokenService(provider);
        var token = service.IssueToken(
            userId: 7,
            tableIndex: 0,
            rowIndex: 2,
            specId: 11,
            sourceProject: "源项目",
            sourceSpecification: "源规格",
            specProject: "目标项目",
            specSpecification: "目标规格",
            specAcceptance: "验收",
            specRemark: "备注",
            customerId: 3,
            processId: 5,
            machineModelId: null,
            config: new MatchingConfig
            {
                ExactMatchOnly = true,
                FilterEmptySourceRows = false
            });

        var mappings = new[]
        {
            ((int?)0, new FillMapping
            {
                RowIndex = 2,
                SpecId = 11,
                ReviewApprovalToken = token
            })
        };

        var resolveBundle = typeof(MatchingApprovalTokenService)
            .GetMethod("ResolveBundle", BindingFlags.Instance | BindingFlags.NonPublic);
        resolveBundle.Should().NotBeNull();

        var bundle = resolveBundle!.Invoke(service, [mappings, 7]);
        bundle.Should().NotBeNull();

        var config = bundle!.GetType().GetProperty("Config")!.GetValue(bundle);
        config.Should().BeOfType<MatchingConfig>();
        ((MatchingConfig)config!).ExactMatchOnly.Should().BeTrue();
        ((MatchingConfig)config!).FilterEmptySourceRows.Should().BeFalse();
    }
}
