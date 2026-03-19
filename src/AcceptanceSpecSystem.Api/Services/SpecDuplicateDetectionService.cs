using System.Text;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 规格重复/近重复排查服务。
/// 当前用于规格管理页的人工诊断，不参与实际匹配流程。
/// </summary>
internal static class SpecDuplicateDetectionService
{
    private const double DefaultMinSimilarity = 0.88;

    public static SpecDuplicateDetectionResultDto Detect(
        IEnumerable<AcceptanceSpec> specs,
        double? minSimilarity = null,
        int? maxGroups = null)
    {
        var similarityThreshold = Math.Clamp(minSimilarity ?? DefaultMinSimilarity, 0.7, 0.99);
        var groupLimit = Math.Clamp(maxGroups ?? 20, 1, 100);

        var candidates = specs
            .Where(spec => !string.IsNullOrWhiteSpace(spec.Project) && !string.IsNullOrWhiteSpace(spec.Specification))
            .OrderBy(spec => spec.Project)
            .ThenBy(spec => spec.Id)
            .ToList();

        var exactGroups = BuildExactGroups(candidates);
        var exactMemberIds = exactGroups
            .SelectMany(group => group.Items)
            .Select(item => item.Id)
            .ToHashSet();

        var similarGroups = BuildSimilarGroups(
            candidates.Where(spec => !exactMemberIds.Contains(spec.Id)).ToList(),
            similarityThreshold);

        return new SpecDuplicateDetectionResultDto
        {
            ScannedCount = candidates.Count,
            ExactGroupCount = exactGroups.Count,
            SimilarGroupCount = similarGroups.Count,
            ExactGroups = exactGroups.Take(groupLimit).ToList(),
            SimilarGroups = similarGroups.Take(groupLimit).ToList()
        };
    }

    private static List<SpecDuplicateGroupDto> BuildExactGroups(IEnumerable<AcceptanceSpec> specs)
    {
        return specs
            .GroupBy(spec => BuildExactKey(spec.Project, spec.Specification), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(group =>
            {
                var items = group
                    .OrderBy(item => item.Id)
                    .Select(MapItem)
                    .ToList();
                var first = items[0];

                return new SpecDuplicateGroupDto
                {
                    GroupType = "exact",
                    Project = first.Project,
                    SpecificationPreview = BuildPreview(first.Specification),
                    Reason = "项目与规格在忽略空白和标点后完全一致",
                    SimilarityScore = 1,
                    ItemCount = items.Count,
                    Items = items
                };
            })
            .OrderByDescending(group => group.ItemCount)
            .ThenBy(group => group.Project)
            .ThenBy(group => group.Items[0].Id)
            .ToList();
    }

    private static List<SpecDuplicateGroupDto> BuildSimilarGroups(
        IReadOnlyList<AcceptanceSpec> specs,
        double similarityThreshold)
    {
        if (specs.Count < 2)
            return [];

        var unionFind = new UnionFind(specs.Select(spec => spec.Id));
        var pairScores = new List<DuplicatePairScore>();

        for (var leftIndex = 0; leftIndex < specs.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < specs.Count; rightIndex++)
            {
                var pair = TryBuildPair(specs[leftIndex], specs[rightIndex], similarityThreshold);
                if (pair == null)
                    continue;

                unionFind.Union(pair.LeftId, pair.RightId);
                pairScores.Add(pair);
            }
        }

        if (pairScores.Count == 0)
            return [];

        var itemLookup = specs.ToDictionary(spec => spec.Id);
        var pairRootLookup = pairScores
            .GroupBy(pair => unionFind.Find(pair.LeftId))
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new List<SpecDuplicateGroupDto>();

        foreach (var rootGroup in specs.GroupBy(spec => unionFind.Find(spec.Id)))
        {
            var members = rootGroup
                .Select(spec => itemLookup[spec.Id])
                .OrderBy(spec => spec.Id)
                .ToList();

            if (members.Count < 2)
                continue;

            if (!pairRootLookup.TryGetValue(rootGroup.Key, out var groupPairs) || groupPairs.Count == 0)
                continue;

            var bestPair = groupPairs
                .OrderByDescending(pair => pair.CombinedScore)
                .ThenByDescending(pair => pair.SpecificationScore)
                .ThenBy(pair => pair.LeftId)
                .First();
            var first = members[0];

            result.Add(new SpecDuplicateGroupDto
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

    private static DuplicatePairScore? TryBuildPair(
        AcceptanceSpec left,
        AcceptanceSpec right,
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

    private static SpecDuplicateItemDto MapItem(AcceptanceSpec spec)
    {
        return new SpecDuplicateItemDto
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
}
