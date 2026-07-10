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

public sealed class BlockingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static bool WasCancelled { get; private set; }

    public static void Reset()
    {
        WasCancelled = false;
    }

    public async Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
            throw;
        }
    }
}

public sealed class CountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class StructureCacheCountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class StructureCacheFusedRangeAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var tableIndex = request.RuleCandidates.First().TableIndex;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身补出规格列并触发融合缓存",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = tableIndex,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class SharedBudgetCountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class ZeroBudgetCountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class CountingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult());
    }
}

public sealed class SharedBudgetCountingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult());
    }
}

public sealed class FailingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        throw new InvalidOperationException("测试替身模拟列语义召回失败");
    }
}

public sealed class BlockingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public static bool WasCancelled { get; private set; }

    public static TimeSpan CancelledAfter { get; private set; }

    public static void Reset()
    {
        WasCancelled = false;
        CancelledAfter = TimeSpan.Zero;
    }

    public async Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
            CancelledAfter = Stopwatch.GetElapsedTime(startedAt);
            throw;
        }
    }
}

public sealed class SpecificationColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult
        {
            Suggestions =
            [
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 1,
                    Header = "管控要求",
                    TargetField = "Specification",
                    Confidence = 0.88,
                    Reason = "表头表示规格约束要求",
                    Source = "SemanticRecall"
                },
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 1,
                    Header = "管控要求",
                    TargetField = "Unknown",
                    Confidence = 0.72,
                    Reason = "同列低置信度冲突建议应被丢弃",
                    Source = "SemanticRecall"
                }
            ]
        });
    }
}

public sealed class AcceptanceColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult
        {
            Suggestions =
            [
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 2,
                    Header = "验收方式",
                    TargetField = "Acceptance",
                    Confidence = 0.91,
                    Reason = "测试替身故意返回方法列",
                    Source = "SemanticRecall"
                },
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 3,
                    Header = "确认结果",
                    TargetField = "Acceptance",
                    Confidence = 0.89,
                    Reason = "表头表示供应商确认结果",
                    Source = "SemanticRecall"
                }
            ]
        });
    }
}

public sealed class InvalidColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult
        {
            Suggestions =
            [
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 1,
                    Header = "管控要求",
                    TargetField = "MadeUpField",
                    Confidence = 0.95,
                    Reason = "非法字段应被丢弃",
                    Source = "SemanticRecall"
                }
            ]
        });
    }
}

public sealed class HeaderCorrectionStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.94,
            Decision = "autoApply",
            Reason = "测试替身修正表头行",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    HeaderRowIndex = 1,
                    HeaderRowCount = 1,
                    DataStartRowIndex = 2,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.94,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class InvalidHeaderStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.95,
            Decision = "autoApply",
            Reason = "测试替身返回非法表头行",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    HeaderRowIndex = 99,
                    HeaderRowCount = 1,
                    DataStartRowIndex = 100,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.95,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class RecordingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static LlmDocumentStructureAdjudicationRequest? LastRequest { get; private set; }

    public static void Reset()
    {
        LastRequest = null;
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身记录历史案例",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class RoutingBudgetRecordingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static LlmDocumentStructureAdjudicationRequest? LastRequest { get; private set; }

    public static void Reset()
    {
        LastRequest = null;
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class OffsetHeaderRecordingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static LlmDocumentStructureAdjudicationRequest? LastRequest { get; private set; }

    public static void Reset()
    {
        LastRequest = null;
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身记录带前导说明行的坐标",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}
