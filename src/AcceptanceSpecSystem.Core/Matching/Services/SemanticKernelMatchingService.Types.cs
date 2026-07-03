using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class SemanticKernelMatchingService : IMatchingService
{
    private sealed class LlmCallBudget
    {
        private int _remaining;

        public LlmCallBudget(int maxCalls)
        {
            _remaining = Math.Max(0, maxCalls);
        }

        /// <summary>
        /// 尝试占用一次 LLM 调用配额。成功返回 true 并扣减；预算耗尽返回 false。
        /// </summary>
        public bool TryConsume()
        {
            // 原子地将剩余值减 1，仅当减之前 > 0 才算成功
            while (true)
            {
                var current = Volatile.Read(ref _remaining);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _remaining, current - 1, current) == current)
                {
                    return true;
                }
            }
        }
    }

    /// <summary>
    /// LLM 熔断器：按"连续失败"计数，任意一次成功即复位。
    /// 避免大批量低失败率场景下累计失败误触发永久熔断。
    /// </summary>
    private sealed class LlmCircuitBreaker
    {
        private readonly int _failureThreshold;
        private int _consecutiveFailureCount;
        private int _isOpen;

        public LlmCircuitBreaker(int failureThreshold)
        {
            _failureThreshold = Math.Clamp(failureThreshold, 3, 200);
        }

        public bool IsOpen => Volatile.Read(ref _isOpen) == 1;

        public void RecordFailure()
        {
            if (Interlocked.Increment(ref _consecutiveFailureCount) >= _failureThreshold)
            {
                Volatile.Write(ref _isOpen, 1);
            }
        }

        public void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailureCount, 0);
        }
    }

    private readonly record struct LlmCallExecution<T>(T? Result, bool Failed, bool BudgetExhausted);

    private sealed class EvaluatedCandidate
    {
        public required MatchSource Source { get; init; }
        public required MatchCandidate Candidate { get; init; }
        public double EmbeddingScore { get; init; }
        public double FinalScore { get; set; }
        public double ProjectScore { get; set; }
        public double SpecificationTextScore { get; set; }
        public double NumericScore { get; set; }
        public double ProjectCodeConflictPenalty { get; set; }
        public bool IsSkeletonRescue { get; init; }
        public string? RerankSummary { get; set; }
        public MatchSelectionMode SelectionMode { get; set; } = MatchSelectionMode.EmbeddingTop1;
        public string? SelectionSummary { get; set; }
        public MatchBasis MatchBasis { get; set; } = MatchBasis.ProjectSpecification;
        public MatchEvidence? Evidence { get; set; }
        public List<MatchIssue>? Issues { get; set; }
        public LlmEquivalenceAdjudicationResult? LlmEquivalence { get; set; }
    }

}
