using System.Text;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 规格重复/近重复排查服务。
/// </summary>
internal static class SpecDuplicateDetectionService
{
    private const double DefaultMinSimilarity = 0.88;

    public static SpecDuplicateDetectionResultModel Detect(
        IEnumerable<AcceptanceSpecDuplicateCandidate> specs,
        IResourceBudgetGovernor resourceBudgetGovernor,
        CancellationToken cancellationToken,
        double? minSimilarity = null,
        int? maxGroups = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var similarityThreshold = Math.Clamp(minSimilarity ?? DefaultMinSimilarity, 0.7, 0.99);
        var groupLimit = Math.Clamp(maxGroups ?? 20, 1, 100);

        var candidates = new List<AcceptanceSpecDuplicateCandidate>();
        foreach (var spec in specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(spec.Project) && !string.IsNullOrWhiteSpace(spec.Specification))
                candidates.Add(spec);
        }

        candidates.Sort(static (left, right) =>
        {
            var projectOrder = string.Compare(left.Project, right.Project, StringComparison.Ordinal);
            return projectOrder != 0 ? projectOrder : left.Id.CompareTo(right.Id);
        });
        resourceBudgetGovernor.ValidateDuplicateCandidates(candidates.Count);
        cancellationToken.ThrowIfCancellationRequested();

        var exactGroups = BuildExactGroups(candidates, cancellationToken);
        var exactMemberIds = exactGroups
            .SelectMany(group => group.Items)
            .Select(item => item.Id)
            .ToHashSet();
        cancellationToken.ThrowIfCancellationRequested();

        var similarGroups = BuildSimilarGroups(
            candidates.Where(spec => !exactMemberIds.Contains(spec.Id)).ToList(),
            similarityThreshold,
            resourceBudgetGovernor,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return new SpecDuplicateDetectionResultModel
        {
            ScannedCount = candidates.Count,
            ExactGroupCount = exactGroups.Count,
            SimilarGroupCount = similarGroups.Count,
            ExactGroups = exactGroups.Take(groupLimit).ToList(),
            SimilarGroups = similarGroups.Take(groupLimit).ToList()
        };
    }

    private static List<SpecDuplicateGroupModel> BuildExactGroups(
        IReadOnlyList<AcceptanceSpecDuplicateCandidate> specs,
        CancellationToken cancellationToken)
    {
        var buckets = new Dictionary<string, List<AcceptanceSpecDuplicateCandidate>>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = BuildExactKey(spec.Project, spec.Specification);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets.Add(key, bucket);
            }
            bucket.Add(spec);
        }

        var result = new List<SpecDuplicateGroupModel>();
        foreach (var group in buckets.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count <= 1)
                continue;
            var items = group
                    .OrderBy(item => item.Id)
                    .Select(MapItem)
                    .ToList();
            var first = items[0];

            result.Add(new SpecDuplicateGroupModel
            {
                GroupType = "exact",
                Project = first.Project,
                SpecificationPreview = BuildPreview(first.Specification),
                Reason = "项目与规格在忽略空白和标点后完全一致",
                SimilarityScore = 1,
                ItemCount = items.Count,
                Items = items
            });
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result
            .OrderByDescending(group => group.ItemCount)
            .ThenBy(group => group.Project)
            .ThenBy(group => group.Items[0].Id)
            .ToList();
    }

    private static List<SpecDuplicateGroupModel> BuildSimilarGroups(
        IReadOnlyList<AcceptanceSpecDuplicateCandidate> specs,
        double similarityThreshold,
        IResourceBudgetGovernor resourceBudgetGovernor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (specs.Count < 2)
            return [];

        var unionFind = new UnionFind(specs.Select(spec => spec.Id));
        var pairScores = new List<DuplicatePairScore>();
        long comparisonCount = 0;

        foreach (var candidatePair in EnumerateCandidatePairs(specs, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            comparisonCount++;
            resourceBudgetGovernor.ValidateDuplicateComparisons(comparisonCount);
            cancellationToken.ThrowIfCancellationRequested();
            var pair = TryBuildPair(
                specs[candidatePair.LeftIndex],
                specs[candidatePair.RightIndex],
                similarityThreshold);
            if (pair == null)
                continue;

            unionFind.Union(pair.LeftId, pair.RightId);
            pairScores.Add(pair);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (pairScores.Count == 0)
            return [];

        var pairRootLookup = new Dictionary<int, List<DuplicatePairScore>>();
        foreach (var pair in pairScores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = unionFind.Find(pair.LeftId);
            if (!pairRootLookup.TryGetValue(root, out var rootPairs))
            {
                rootPairs = [];
                pairRootLookup.Add(root, rootPairs);
            }
            rootPairs.Add(pair);
        }

        var memberGroups = new Dictionary<int, List<AcceptanceSpecDuplicateCandidate>>();
        foreach (var spec in specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = unionFind.Find(spec.Id);
            if (!memberGroups.TryGetValue(root, out var members))
            {
                members = [];
                memberGroups.Add(root, members);
            }
            members.Add(spec);
        }

        var result = new List<SpecDuplicateGroupModel>();

        foreach (var (root, rootMembers) in memberGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = rootMembers
                .OrderBy(spec => spec.Id)
                .ToList();

            if (members.Count < 2)
                continue;

            if (!pairRootLookup.TryGetValue(root, out var groupPairs) || groupPairs.Count == 0)
                continue;

            var bestPair = groupPairs
                .OrderByDescending(pair => pair.CombinedScore)
                .ThenByDescending(pair => pair.SpecificationScore)
                .ThenBy(pair => pair.LeftId)
                .First();
            var first = members[0];

            result.Add(new SpecDuplicateGroupModel
            {
                GroupType = "similar",
                Project = first.Project,
                SpecificationPreview = BuildPreview(first.Specification),
                Reason = bestPair.Reason,
                SimilarityScore = Math.Round(bestPair.CombinedScore, 4),
                ItemCount = members.Count,
                Items = members.Select(MapItem).ToList()
            });
        }

        return result
            .OrderByDescending(group => group.SimilarityScore)
            .ThenByDescending(group => group.ItemCount)
            .ThenBy(group => group.Project)
            .ThenBy(group => group.Items[0].Id)
            .ToList();
    }

    internal static IEnumerable<CandidatePair> EnumerateCandidatePairs(
        IReadOnlyList<AcceptanceSpecDuplicateCandidate> specs,
        CancellationToken cancellationToken)
    {
        // 安全召回依据：
        // 1. 非包含关系要达到项目 Dice 阈值，交集必非空，因此双方必共享现有 similarity token；
        // 2. 任一非空包含关系必共享至少一个归一化字符（包括单字符和纯标点）；
        // 3. strict key 相等会直接得到 0.99，空 strict key 也必须是合法桶。
        // 字符与 token 分别映射到固定 256-bit FNV-1a 签名；同一元素必落到同一 bit，
        // 因此 AND 为零才可安全排除。哈希碰撞只会增加比较，不会漏报，且结果跨进程稳定。
        var signatures = new ProjectCandidateSignature[specs.Count];
        for (var index = 0; index < specs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            signatures[index] = BuildProjectCandidateSignature(specs[index].Project);
        }

        for (var leftIndex = 0; leftIndex < specs.Count; leftIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var rightIndex = leftIndex + 1; rightIndex < specs.Count; rightIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var left = signatures[leftIndex];
                var right = signatures[rightIndex];
                if (string.Equals(left.StrictKey, right.StrictKey, StringComparison.Ordinal) ||
                    left.CharacterMask.SharesAny(right.CharacterMask) ||
                    left.TokenMask.SharesAny(right.TokenMask))
                {
                    yield return new CandidatePair(leftIndex, rightIndex);
                }
            }
        }
    }

    private static ProjectCandidateSignature BuildProjectCandidateSignature(string project)
    {
        var normalized = NormalizeComparableText(project);
        var strict = NormalizeStrictKey(normalized);
        var characterMask = SignatureMask.Empty;
        foreach (var character in normalized)
            characterMask = characterMask.Add(StableFnv1A(character.ToString()));

        var tokenMask = SignatureMask.Empty;
        foreach (var token in BuildSimilarityTokens(normalized, strict))
            tokenMask = tokenMask.Add(StableFnv1A(token));

        return new ProjectCandidateSignature(strict, characterMask, tokenMask);
    }

    private static ulong StableFnv1A(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= (byte)character;
            hash *= prime;
            hash ^= (byte)(character >> 8);
            hash *= prime;
        }

        return hash;
    }

    private static DuplicatePairScore? TryBuildPair(
        AcceptanceSpecDuplicateCandidate left,
        AcceptanceSpecDuplicateCandidate right,
        double similarityThreshold)
    {
        var projectScore = ComputeTextSimilarity(left.Project, right.Project, 0.88);
        if (projectScore < 0.8)
            return null;

        var specificationScore = ComputeTextSimilarity(left.Specification, right.Specification, 0.9);
        if (specificationScore < similarityThreshold)
            return null;

        var combinedScore = Math.Clamp(projectScore * 0.35 + specificationScore * 0.65, 0, 1);
        if (combinedScore < similarityThreshold)
            return null;

        var reasons = new List<string>();
        if (projectScore >= 0.99)
            reasons.Add("项目一致");
        else if (projectScore >= 0.88)
            reasons.Add("项目接近");
        else
            reasons.Add("项目部分接近");

        if (specificationScore >= 0.99)
            reasons.Add("规格文本几乎一致");
        else if (specificationScore >= 0.93)
            reasons.Add("规格文本高度接近");
        else
            reasons.Add("规格文本接近");

        return new DuplicatePairScore(
            left.Id,
            right.Id,
            projectScore,
            specificationScore,
            Math.Round(combinedScore, 4),
            string.Join("，", reasons));
    }

    private static SpecDuplicateItemModel MapItem(AcceptanceSpecDuplicateCandidate spec)
    {
        return new SpecDuplicateItemModel
        {
            Id = spec.Id,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt
        };
    }

    private static string BuildExactKey(string project, string specification)
    {
        var normalizedProject = NormalizeStrictKey(project);
        var normalizedSpecification = NormalizeStrictKey(specification);
        if (string.IsNullOrWhiteSpace(normalizedProject) || string.IsNullOrWhiteSpace(normalizedSpecification))
            return string.Empty;

        return $"{normalizedProject}|{normalizedSpecification}";
    }

    private static string BuildPreview(string? value)
    {
        var comparable = NormalizeComparableText(value);
        if (comparable.Length <= 80)
            return comparable;
        return comparable[..80] + "...";
    }

    private static double ComputeTextSimilarity(string? left, string? right, double containmentScore)
    {
        var normalizedLeft = NormalizeComparableText(left);
        var normalizedRight = NormalizeComparableText(right);

        if (string.IsNullOrWhiteSpace(normalizedLeft) && string.IsNullOrWhiteSpace(normalizedRight))
            return 1;
        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            return 0;
        if (normalizedLeft == normalizedRight)
            return 1;

        var strictLeft = NormalizeStrictKey(normalizedLeft);
        var strictRight = NormalizeStrictKey(normalizedRight);
        if (strictLeft == strictRight)
            return 0.99;

        if (normalizedLeft.Contains(normalizedRight, StringComparison.OrdinalIgnoreCase) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.OrdinalIgnoreCase))
        {
            return containmentScore;
        }

        return ComputeDiceCoefficient(
            BuildSimilarityTokens(normalizedLeft, strictLeft),
            BuildSimilarityTokens(normalizedRight, strictRight));
    }

    private static HashSet<string> BuildSimilarityTokens(string comparableText, string strictText)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in comparableText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length >= 2)
                result.Add(part);
        }

        if (strictText.Length <= 2)
        {
            if (!string.IsNullOrWhiteSpace(strictText))
                result.Add(strictText);
            return result;
        }

        for (var index = 0; index < strictText.Length - 1; index++)
        {
            result.Add(strictText.Substring(index, 2));
        }

        return result;
    }

    private static double ComputeDiceCoefficient(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
            return 0;

        var overlap = left.Intersect(right, StringComparer.Ordinal).Count();
        return (2d * overlap) / (left.Count + right.Count);
    }

    private static string NormalizeComparableText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;

        foreach (var rawChar in value.Trim().ToLowerInvariant())
        {
            var normalizedChar = NormalizeChar(rawChar);

            if (char.IsWhiteSpace(normalizedChar))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            builder.Append(normalizedChar);
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeStrictKey(string? value)
    {
        var comparable = NormalizeComparableText(value);
        if (string.IsNullOrWhiteSpace(comparable))
            return string.Empty;

        var builder = new StringBuilder(comparable.Length);
        foreach (var item in comparable)
        {
            if (char.IsLetterOrDigit(item) || IsCjk(item))
                builder.Append(item);
        }

        return builder.ToString();
    }

    private static char NormalizeChar(char value)
    {
        return value switch
        {
            '（' => '(',
            '）' => ')',
            '，' => ',',
            '。' => '.',
            '；' => ';',
            '：' => ':',
            '、' => ',',
            '“' => '"',
            '”' => '"',
            '‘' => '\'',
            '’' => '\'',
            '【' => '[',
            '】' => ']',
            '《' => '<',
            '》' => '>',
            '－' => '-',
            '—' => '-',
            '～' => '~',
            '×' => 'x',
            _ => value
        };
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u4e00' and <= '\u9fff';
    }

    private sealed class UnionFind
    {
        private readonly Dictionary<int, int> _parents;

        public UnionFind(IEnumerable<int> values)
        {
            _parents = values.Distinct().ToDictionary(value => value, value => value);
        }

        public int Find(int value)
        {
            var parent = _parents[value];
            if (parent == value)
                return value;

            var root = Find(parent);
            _parents[value] = root;
            return root;
        }

        public void Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot == rightRoot)
                return;

            if (leftRoot < rightRoot)
            {
                _parents[rightRoot] = leftRoot;
            }
            else
            {
                _parents[leftRoot] = rightRoot;
            }
        }
    }

    private sealed record DuplicatePairScore(
        int LeftId,
        int RightId,
        double ProjectScore,
        double SpecificationScore,
        double CombinedScore,
        string Reason);

    internal readonly record struct CandidatePair(int LeftIndex, int RightIndex);

    private readonly record struct ProjectCandidateSignature(
        string StrictKey,
        SignatureMask CharacterMask,
        SignatureMask TokenMask);

    private readonly record struct SignatureMask(ulong Lane0, ulong Lane1, ulong Lane2, ulong Lane3)
    {
        public static SignatureMask Empty => default;

        public SignatureMask Add(ulong hash)
        {
            var bit = 1UL << (int)(hash & 63);
            return ((hash >> 6) & 3) switch
            {
                0 => this with { Lane0 = Lane0 | bit },
                1 => this with { Lane1 = Lane1 | bit },
                2 => this with { Lane2 = Lane2 | bit },
                _ => this with { Lane3 = Lane3 | bit }
            };
        }

        public bool SharesAny(SignatureMask other) =>
            (Lane0 & other.Lane0) != 0 ||
            (Lane1 & other.Lane1) != 0 ||
            (Lane2 & other.Lane2) != 0 ||
            (Lane3 & other.Lane3) != 0;
    }
}
