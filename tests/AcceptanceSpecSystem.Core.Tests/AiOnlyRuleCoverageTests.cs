using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class AiOnlyRuleCoverageTests
{
    public static IEnumerable<object[]> GetEquivalenceCases()
    {
        yield return
        [
            new EquivalenceCoverageCase(
                "等价表达可放行",
                "安装要求",
                "最大不可拆部件≈3200",
                "安装要求",
                "最大不可拆部件约等于3200。",
                LlmEquivalenceVerdict.Equivalent,
                LlmEquivalenceReasonType.EquivalentExpression,
                AiOnlyGateOutcome.AllowAutoApply)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "跨语种品牌别名可由AI放行",
                "Panasonic 设备",
                "品牌要求 Panasonic",
                "松下 设备",
                "品牌要求 松下",
                LlmEquivalenceVerdict.Equivalent,
                LlmEquivalenceReasonType.EquivalentExpression,
                AiOnlyGateOutcome.AllowAutoApply)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "品牌冲突需拦截",
                "Panasonic 设备",
                "品牌要求 Panasonic",
                "Mitsubishi 设备",
                "品牌要求 Mitsubishi",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "数值相容可放行",
                "尺寸要求",
                "宽度小于0.5cm",
                "尺寸要求",
                "宽度等于0.2cm",
                LlmEquivalenceVerdict.Equivalent,
                LlmEquivalenceReasonType.EquivalentExpression,
                AiOnlyGateOutcome.AllowAutoApply)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "数值明确冲突需拦截",
                "尺寸要求",
                "宽度小于0.5cm",
                "尺寸要求",
                "宽度等于0.7cm",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "多字段中一项冲突需拦截",
                "尺寸要求",
                "宽度小于0.5cm，高度等于1cm",
                "尺寸要求",
                "宽度等于0.2cm，高度等于2cm",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "跨单位等值可由AI放行",
                "芯片工艺",
                "线宽等于0.13μm",
                "芯片工艺",
                "线宽等于130nm",
                LlmEquivalenceVerdict.Equivalent,
                LlmEquivalenceReasonType.SymbolEquivalent,
                AiOnlyGateOutcome.AllowAutoApply)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "区间重叠但不完全一致需人工确认",
                "安全要求",
                "温度不大于60度",
                "安全要求",
                "温度不大于50度",
                LlmEquivalenceVerdict.Uncertain,
                LlmEquivalenceReasonType.Uncertain,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "电压漏位需拦截",
                "水/电/气",
                "电力规格要求: 380V三相/50HZ或22V/50HZ",
                "水/电/气",
                "电力规格要求: 380V三相/50HZ或220V/50HZ",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "小数点错位需拦截",
                "电压要求",
                "Panasonic 设备电压等于24V",
                "电压要求",
                "松下设备电压等于2.4V",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "电流数量级错误需拦截",
                "电流要求",
                "设备工作电流等于2A",
                "电流要求",
                "设备工作电流等于0.2A",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "型号冲突需拦截",
                "设备型号 ABC-100",
                "请使用 ABC-100",
                "设备型号 ABC-700",
                "请使用 ABC-700",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "收板放板相反需拦截",
                "设备设计要求",
                "收板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm",
                "设备设计要求",
                "放板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
        yield return
        [
            new EquivalenceCoverageCase(
                "正反转相反需拦截",
                "正转模式",
                "速度 100 mm/s",
                "反转模式",
                "速度 100 mm/s",
                LlmEquivalenceVerdict.Different,
                LlmEquivalenceReasonType.SemanticDifference,
                AiOnlyGateOutcome.RequireReview)
        ];
    }

    [Theory]
    [MemberData(nameof(GetEquivalenceCases))]
    public async Task AiOnlyEquivalenceGate_ShouldCoverCurrentRuleCases(EquivalenceCoverageCase testCase)
    {
        var service = new MatrixLlmEquivalenceAdjudicationService();

        var result = await service.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
        {
            SourceProject = testCase.SourceProject,
            SourceSpecification = testCase.SourceSpecification,
            CandidateProject = testCase.CandidateProject,
            CandidateSpecification = testCase.CandidateSpecification,
            CurrentDecision = "manualReview",
            ScoreDetails = new Dictionary<string, double>(),
            EvidenceSummary = [],
            ConflictSummary = []
        });

        result.Should().NotBeNull(testCase.Name);
        result!.Verdict.Should().Be(testCase.ExpectedVerdict, testCase.Name);
        result.ReasonType.Should().Be(testCase.ExpectedReasonType, testCase.Name);
        ProjectEquivalenceOutcome(result).Should().Be(testCase.ExpectedOutcome, testCase.Name);
    }

    private static AiOnlyGateOutcome ProjectEquivalenceOutcome(LlmEquivalenceAdjudicationResult result)
    {
        return result.Verdict == LlmEquivalenceVerdict.Equivalent
            ? AiOnlyGateOutcome.AllowAutoApply
            : AiOnlyGateOutcome.RequireReview;
    }

    public enum AiOnlyGateOutcome
    {
        AllowAutoApply,
        RequireReview,
        Reject
    }

    public sealed record EquivalenceCoverageCase(
        string Name,
        string SourceProject,
        string SourceSpecification,
        string CandidateProject,
        string CandidateSpecification,
        LlmEquivalenceVerdict ExpectedVerdict,
        LlmEquivalenceReasonType ExpectedReasonType,
        AiOnlyGateOutcome ExpectedOutcome);

    private sealed class MatrixLlmEquivalenceAdjudicationService : ILlmEquivalenceAdjudicationService
    {
        public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
            LlmEquivalenceAdjudicationRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = (request.SourceProject.Trim(), request.SourceSpecification.Trim(), request.CandidateProject.Trim(), request.CandidateSpecification.Trim()) switch
            {
                ("安装要求", "最大不可拆部件≈3200", "安装要求", "最大不可拆部件约等于3200。") => BuildEquivalent(LlmEquivalenceReasonType.EquivalentExpression, "≈ 与 约等于属于同义表达"),
                ("Panasonic 设备", "品牌要求 Panasonic", "松下 设备", "品牌要求 松下") => BuildEquivalent(LlmEquivalenceReasonType.EquivalentExpression, "Panasonic 与 松下属于同一品牌的中英文表达"),
                ("Panasonic 设备", "品牌要求 Panasonic", "Mitsubishi 设备", "品牌要求 Mitsubishi") => BuildDifferent("Panasonic 与 Mitsubishi 不是同一品牌"),
                ("尺寸要求", "宽度小于0.5cm", "尺寸要求", "宽度等于0.2cm") => BuildEquivalent(LlmEquivalenceReasonType.EquivalentExpression, "候选约束满足源项要求，可视为同一验收语义"),
                ("尺寸要求", "宽度小于0.5cm", "尺寸要求", "宽度等于0.7cm") => BuildDifferent("候选宽度超过源项上限"),
                ("尺寸要求", "宽度小于0.5cm，高度等于1cm", "尺寸要求", "宽度等于0.2cm，高度等于2cm") => BuildDifferent("高度不一致，不能视为同一验收语义"),
                ("芯片工艺", "线宽等于0.13μm", "芯片工艺", "线宽等于130nm") => BuildEquivalent(LlmEquivalenceReasonType.SymbolEquivalent, "不同单位表达的是同一数值约束"),
                ("安全要求", "温度不大于60度", "安全要求", "温度不大于50度") => BuildUncertain("候选范围更严，但是否可视为同一验收语义需要人工确认"),
                ("水/电/气", "电力规格要求: 380V三相/50HZ或22V/50HZ", "水/电/气", "电力规格要求: 380V三相/50HZ或220V/50HZ") => BuildDifferent("22V 与 220V 不是同一电压要求"),
                ("电压要求", "Panasonic 设备电压等于24V", "电压要求", "松下设备电压等于2.4V") => BuildDifferent("24V 与 2.4V 存在数量级差异"),
                ("电流要求", "设备工作电流等于2A", "电流要求", "设备工作电流等于0.2A") => BuildDifferent("2A 与 0.2A 存在数量级差异"),
                ("设备型号 ABC-100", "请使用 ABC-100", "设备型号 ABC-700", "请使用 ABC-700") => BuildDifferent("型号不同，不能视为同一验收项"),
                ("设备设计要求", "收板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm", "设备设计要求", "放板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm") => BuildDifferent("收板机与放板机语义相反"),
                ("正转模式", "速度 100 mm/s", "反转模式", "速度 100 mm/s") => BuildDifferent("正转与反转语义相反"),
                _ => BuildUncertain("测试矩阵未配置该案例")
            };

            return Task.FromResult<LlmEquivalenceAdjudicationResult?>(result);
        }

        public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
        {
            throw new NotSupportedException();
        }

        private static LlmEquivalenceAdjudicationResult BuildEquivalent(LlmEquivalenceReasonType reasonType, string reason) =>
            new()
            {
                Verdict = LlmEquivalenceVerdict.Equivalent,
                ReasonType = reasonType,
                Confidence = 0.93,
                Reason = reason
            };

        private static LlmEquivalenceAdjudicationResult BuildDifferent(string reason) =>
            new()
            {
                Verdict = LlmEquivalenceVerdict.Different,
                ReasonType = LlmEquivalenceReasonType.SemanticDifference,
                Confidence = 0.92,
                Reason = reason
            };

        private static LlmEquivalenceAdjudicationResult BuildUncertain(string reason) =>
            new()
            {
                Verdict = LlmEquivalenceVerdict.Uncertain,
                ReasonType = LlmEquivalenceReasonType.Uncertain,
                Confidence = 0.51,
                Reason = reason
            };
    }

}
