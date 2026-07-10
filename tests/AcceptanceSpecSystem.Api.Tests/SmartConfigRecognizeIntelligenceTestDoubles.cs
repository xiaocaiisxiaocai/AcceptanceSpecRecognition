using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class MissingAcceptanceAndRemarkColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺验收列和备注列",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = null,
                RemarkColumn = null,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class LowConfidenceCompleteMappingIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TableIdentificationResult { TableIndex = 0, Confidence = 1 });

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.6,
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class LowConfidenceWrongHeaderIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 0.5
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.4,
            Details =
            [
                new ColumnIdentificationResult { ColumnIndex = 0, ColumnType = ColumnType.Project, Confidence = 0.4 },
                new ColumnIdentificationResult { ColumnIndex = 1, ColumnType = ColumnType.Specification, Confidence = 0.4 },
                new ColumnIdentificationResult { ColumnIndex = 2, ColumnType = ColumnType.Acceptance, Confidence = 0.4 },
                new ColumnIdentificationResult { ColumnIndex = 3, ColumnType = ColumnType.Remark, Confidence = 0.4 }
            ],
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingProjectColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TableIdentificationResult { TableIndex = 0, Confidence = 1 });

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Mapping = new ColumnMapping
            {
                SpecificationColumn = 1,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺规格列",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 1,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingSpecificationForSemanticRecallIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.72,
            Reasoning = "缺规格列以触发列语义召回",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            },
            Details =
            [
                new ColumnIdentificationResult { ColumnIndex = 0, HeaderText = "项目", ColumnType = ColumnType.Project, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 1, HeaderText = "管控要求", ColumnType = ColumnType.Unknown, Confidence = 0 },
                new ColumnIdentificationResult { ColumnIndex = 2, HeaderText = "验收结果", ColumnType = ColumnType.Acceptance, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 3, HeaderText = "备注", ColumnType = ColumnType.Remark, Confidence = 0.95 }
            ]
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingAcceptanceForSemanticRecallIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.70,
            Reasoning = "缺验收结果列以触发列语义召回",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = null,
                RemarkColumn = null,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            },
            Details =
            [
                new ColumnIdentificationResult { ColumnIndex = 0, HeaderText = "项目", ColumnType = ColumnType.Project, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 1, HeaderText = "规格内容", ColumnType = ColumnType.Specification, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 2, HeaderText = "验收方式", ColumnType = ColumnType.Unknown, Confidence = 0 },
                new ColumnIdentificationResult { ColumnIndex = 3, HeaderText = "确认结果", ColumnType = ColumnType.Unknown, Confidence = 0 }
            ]
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class FusableMissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺规格列，验收列已确定",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class OffsetHeaderMissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "表头存在前导说明行，缺规格列以触发 LLM 裁决",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 1,
                HeaderRowCount = 1,
                DataStartRowIndex = 2
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 1;
}

public sealed class SpecificationOnlyIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信仅规格结构",
            Mapping = new ColumnMapping
            {
                ProjectColumn = null,
                SpecificationColumn = 0,
                AcceptanceColumn = 1,
                RemarkColumn = 2,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class FillSpecificationColumnStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身补出规格列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class FillRequiredColumnsStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.93,
            Decision = "autoApply",
            Reason = "测试替身补齐导入必填列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.93,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class IncompleteRequiredColumnsStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.91,
            Decision = "needConfirm",
            Reason = "测试替身仍无法确认验收列和备注列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.91,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}
