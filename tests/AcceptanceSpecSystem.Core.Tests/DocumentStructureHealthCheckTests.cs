using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class DocumentStructureHealthCheckTests
{
    [Fact]
    public void Evaluate_WhenMappingIsCompleteAndDataIsHealthy_ShouldAllowAutoApply()
    {
        var table = CreateTable(
            ["项目", "规格", "验收标准", "备注"],
            ["外观", "无划伤", "目视 OK", ""],
            ["尺寸", "长度 10mm", "卡尺检测", "抽检"]);
        var mapping = CreateMapping(projectColumn: 0, specificationColumn: 1, acceptanceColumn: 2, remarkColumn: 3);

        var result = DocumentStructureHealthCheck.Evaluate(table, mapping, confidence: 0.92);

        result.Decision.Should().Be(DocumentStructureDecision.AutoApply);
        result.CanAutoApply.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WhenSpecificationColumnMissing_ShouldDowngradeToNeedConfirm()
    {
        var table = CreateTable(["项目", "验收标准"], ["外观", "目视 OK"]);
        var mapping = CreateMapping(projectColumn: 0, specificationColumn: null, acceptanceColumn: 1, remarkColumn: null);

        var result = DocumentStructureHealthCheck.Evaluate(table, mapping, confidence: 0.96);

        result.Decision.Should().Be(DocumentStructureDecision.NeedConfirm);
        result.CanAutoApply.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.MissingSpecificationColumn);
    }

    [Fact]
    public void Evaluate_WhenImportRequiredColumnsMissing_ShouldDowngradeToNeedConfirm()
    {
        var table = CreateTable(["项目", "规格", "验收标准", "备注"], ["外观", "无划伤", "目视 OK", "抽检"]);
        var mapping = CreateMapping(projectColumn: null, specificationColumn: 1, acceptanceColumn: null, remarkColumn: null);

        var result = DocumentStructureHealthCheck.Evaluate(table, mapping, confidence: 0.96);

        result.Decision.Should().Be(DocumentStructureDecision.NeedConfirm);
        result.CanAutoApply.Should().BeFalse();
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.MissingProjectColumn);
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.MissingAcceptanceColumn);
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.MissingRemarkColumn);
    }

    [Fact]
    public void Evaluate_WhenSpecificationOnlyContextAllowsMissingProject_ShouldAllowAutoApply()
    {
        var table = CreateTable(
            ["规格", "验收标准", "备注"],
            ["无划伤", "目视 OK", "抽检"],
            ["长度 10mm", "卡尺检测", "全检"]);
        var mapping = CreateMapping(projectColumn: null, specificationColumn: 0, acceptanceColumn: 1, remarkColumn: 2);

        var result = DocumentStructureHealthCheck.Evaluate(
            table,
            mapping,
            confidence: 0.96,
            allowMissingProjectColumn: true);

        result.Decision.Should().Be(DocumentStructureDecision.AutoApply);
        result.CanAutoApply.Should().BeTrue();
        result.Issues.Should().NotContain(issue => issue.Code == DocumentStructureHealthIssueCode.MissingProjectColumn);
    }

    [Fact]
    public void Evaluate_WhenMappedColumnsDuplicate_ShouldDowngradeToNeedConfirm()
    {
        var table = CreateTable(["项目", "规格"], ["外观", "无划伤"]);
        var mapping = CreateMapping(projectColumn: 1, specificationColumn: 1, acceptanceColumn: null, remarkColumn: null);

        var result = DocumentStructureHealthCheck.Evaluate(table, mapping, confidence: 0.96);

        result.Decision.Should().Be(DocumentStructureDecision.NeedConfirm);
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.DuplicateMappedColumn);
    }

    [Fact]
    public void Evaluate_WhenConfiguredAutoApplyThresholdIsHigherThanConfidence_ShouldDowngradeToNeedConfirm()
    {
        var table = CreateTable(
            ["项目", "规格", "验收标准", "备注"],
            ["外观", "无划伤", "目视 OK", "抽检"]);
        var mapping = CreateMapping(projectColumn: 0, specificationColumn: 1, acceptanceColumn: 2, remarkColumn: 3);

        var result = DocumentStructureHealthCheck.Evaluate(
            table,
            mapping,
            confidence: 0.92,
            autoApplyConfidenceThreshold: 0.95);

        result.Decision.Should().Be(DocumentStructureDecision.NeedConfirm);
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.LowConfidence);
    }

    [Fact]
    public void Evaluate_WhenSpecificationDataAreaMostlyEmpty_ShouldDowngradeToNeedConfirm()
    {
        var table = CreateTable(
            ["项目", "规格"],
            ["外观", ""],
            ["尺寸", ""],
            ["电气", ""]);
        var mapping = CreateMapping(projectColumn: 0, specificationColumn: 1, acceptanceColumn: null, remarkColumn: null);

        var result = DocumentStructureHealthCheck.Evaluate(table, mapping, confidence: 0.96);

        result.Decision.Should().Be(DocumentStructureDecision.NeedConfirm);
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.EmptySpecificationDataArea);
    }

    [Fact]
    public void Evaluate_WhenConfiguredSpecificationNonEmptyRateIsLower_ShouldAllowSparseSpecificationData()
    {
        var table = CreateTable(
            ["项目", "规格", "验收标准", "备注"],
            ["外观", "", "目视 OK", "抽检"],
            ["尺寸", "长度 10mm", "卡尺检测", "全检"]);
        var mapping = CreateMapping(projectColumn: 0, specificationColumn: 1, acceptanceColumn: 2, remarkColumn: 3);

        var result = DocumentStructureHealthCheck.Evaluate(
            table,
            mapping,
            confidence: 0.96,
            minimumSpecificationNonEmptyRate: 0.4);

        result.Decision.Should().Be(DocumentStructureDecision.AutoApply);
        result.Issues.Should().NotContain(issue => issue.Code == DocumentStructureHealthIssueCode.EmptySpecificationDataArea);
    }

    [Fact]
    public void Evaluate_WhenProjectAndSpecificationLookReversed_ShouldDowngradeToNeedConfirm()
    {
        var table = CreateTable(
            ["规格要求", "项目"],
            ["无划伤、无明显变形，表面清洁", "外观"],
            ["长度 10mm，公差 ±0.1mm", "尺寸"]);
        var mapping = CreateMapping(projectColumn: 0, specificationColumn: 1, acceptanceColumn: null, remarkColumn: null);

        var result = DocumentStructureHealthCheck.Evaluate(table, mapping, confidence: 0.96);

        result.Decision.Should().Be(DocumentStructureDecision.NeedConfirm);
        result.Issues.Should().Contain(issue => issue.Code == DocumentStructureHealthIssueCode.ProjectSpecificationLikelyReversed);
    }

    private static ColumnMappingResult CreateMapping(
        int? projectColumn,
        int? specificationColumn,
        int? acceptanceColumn,
        int? remarkColumn)
    {
        return new ColumnMappingResult
        {
            Mapping = new ColumnMapping
            {
                ProjectColumn = projectColumn,
                SpecificationColumn = specificationColumn,
                AcceptanceColumn = acceptanceColumn,
                RemarkColumn = remarkColumn,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            },
            Confidence = 0.96
        };
    }

    private static TableData CreateTable(IReadOnlyList<string> headers, params IReadOnlyList<string>[] rows)
    {
        return new TableData
        {
            TableIndex = 0,
            Headers = headers.ToList(),
            Rows = rows
                .Select((row, rowIndex) => new RowData
                {
                    Index = rowIndex,
                    Cells = row
                        .Select((value, columnIndex) => new CellData
                        {
                            RowIndex = rowIndex,
                            ColumnIndex = columnIndex,
                            Value = value
                        })
                        .ToList()
                })
                .ToList()
        };
    }
}
