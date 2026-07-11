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
        var service = new MatchingApprovalTokenService(new MatchingApprovalTokenProtector(provider));
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
                FilterEmptySourceRows = false,
                EnableDeterministicAutoApply = false,
                LlmMaxCallsPerBatch = 7
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
        ((MatchingConfig)config!).EnableDeterministicAutoApply.Should().BeFalse();
        ((MatchingConfig)config!).LlmMaxCallsPerBatch.Should().Be(7);
    }

    [Fact]
    public void EnsureRequestContextMatchesBundle_WhenRequestScopeDiffers_ShouldReject()
    {
        var provider = DataProtectionProvider.Create("matching-approval-token-tests");
        var service = new MatchingApprovalTokenService(new MatchingApprovalTokenProtector(provider));
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
            machineModelId: 9,
            config: new MatchingConfig
            {
                MinScoreThreshold = 0.1,
                RecallTopK = 3,
                HighConfidenceThreshold = 0.95
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

        var ensure = typeof(MatchingApprovalTokenService)
            .GetMethod("EnsureRequestContextMatchesBundle", BindingFlags.Instance | BindingFlags.NonPublic);
        ensure.Should().NotBeNull();

        var act = () => ensure!.Invoke(service, [
            bundle,
            4,
            5,
            9,
            new MatchingConfig
            {
                MinScoreThreshold = 0.1,
                RecallTopK = 3,
                HighConfidenceThreshold = 0.95
            }
        ]);

        var exception = act.Should().Throw<TargetInvocationException>().Subject.Single();
        exception.InnerException.Should().NotBeNull();
        exception.InnerException!.Message.Should().Contain("放行令牌与当前执行范围或配置不一致");
    }
}
