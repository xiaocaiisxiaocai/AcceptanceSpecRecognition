using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 语义冲突扫描器。
/// 用确定性规则检测数值/单位、比较符/边界方向、极性反义三类硬冲突，
/// 产出 severity=hard_conflict 的 MatchIssue，替代 LLM 裁决这类有规律的场景。
/// </summary>
public sealed class SemanticConflictScanner
{
    private readonly ISpecCanonicalizer _canonicalizer;

    // ── 比较符识别（含中文等价表达） ────────────────────────────────────
    private static readonly Regex ComparatorRegex = new(
        @"(?<op>≥|≤|>|<|≈|=|不超过|不大于|不低于|不小于|大于等于|小于等于|大于|小于|约等于)",
        RegexOptions.Compiled);

    private static readonly Regex BrandContextRegex = new(
        @"(?:品牌|厂家|厂商|制造商|供应商|vendor|brand)\s*(?:要求|为|：|:|=)?\s*(?!品牌|厂家|厂商|制造商|供应商)(?<brand>[A-Za-z][A-Za-z0-9\- ]{1,40}|[\u4e00-\u9fff]{2,20})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlySet<string> NonBrandContextTokens =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "用例", "冲突", "要求", "单侧未知", "未知", "国产", "规格"
        };

    private static readonly IReadOnlyList<string> BrandDeviceSuffixWords =
    [
        "协作机器人", "安全继电器", "安全模块", "安全光栅", "伺服驱动", "伺服电机", "步进驱动",
        "工业相机", "深度相机", "3d相机", "3D相机", "3d", "3D", "激光雷达", "光电开关", "接近开关",
        "plc", "hmi", "机器人", "协作臂", "机械臂", "控制器", "控制卡", "工控机",
        "变频器", "伺服", "电机", "马达", "编码器", "光电传感器", "传感器", "相机", "镜头",
        "读码器", "光源", "雷达", "液压阀", "电磁阀", "气缸", "导轨", "触摸屏",
        "断路器", "接触器", "继电器", "模块", "电源", "端子", "光电"
    ];

    private static readonly Regex UnsupportedChineseNumberRegex = new(
        @"(?:约|不多于|不少于|大约|超过|低于|高于)?[零一二三四五六七八九十百千半]+(?:毫米|厘米|米|度|%|％)",
        RegexOptions.Compiled);

    private static readonly Regex UnsupportedNaturalFormatRegex = new(
        @"上下浮动|(?:\d+(?:\.\d+)?\s*(?:%|％|mm|cm|m|μm|um|s|ms|bar|kpa|KPa|kPa|mpa|MPa)?(?:以上|以下))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── 极性/方向反义对（成对列表，每对互为反义） ─────────────────────
    // 安全原则：只收录"多字、无歧义"的反义词。单字（开/关、进/出、夹/松）一律不收，
    // 因为单字会作为子串误命中无关词（"进行"含"进"、"出现"含"出"），
    // 虽然误报方向是安全的（强制人工不会错填），但会拉低自动通过率、伤体验。
    private static readonly IReadOnlyList<(string A, string B)> PolarityPairs =
    [
        ("开启", "关闭"), ("打开", "关闭"),
        ("启动", "停止"), ("启动", "关闭"),
        ("正转", "反转"), ("顺转", "逆转"),
        ("上升", "下降"), ("上行", "下行"),
        ("夹紧", "松开"),
        ("升温", "降温"), ("加热", "冷却"),
        ("输入", "输出"),
        ("上料", "下料"), ("投板", "收板"),
        ("左进右出", "右进左出"),
        ("允许", "禁止"), ("允许", "不允许"),
        ("应报警", "不应报警"),
        ("有效", "无效"), ("使能", "禁用"),
        ("高电平", "低电平"), ("高位", "低位"),
        ("常开", "常闭"),
        // 常见对象名一字差（Case 4-B 补词）：进料/出料型，方向相反或对象相反
        ("进料", "出料"), ("进料口", "出料口"), ("进料端", "出料端"),
        ("上料口", "下料口"), ("进站", "出站"), ("进片", "出片"),
        ("入料", "出料"), ("入口", "出口"), ("进口", "出口"),
        ("正面", "反面"), ("正向", "反向"), ("前进", "后退"),
        ("顺时针", "逆时针"), ("左旋", "右旋"),
        ("打开", "闭合"), ("接通", "断开"), ("通电", "断电"),
        ("加压", "泄压"), ("增大", "减小"), ("提高", "降低"),
    ];

    // ── 量纲组合：温度特殊处理（跨温标不自动比较） ──────────────────────
    private const double NumericCompareToleranceRatio = 1e-3;

    public SemanticConflictScanner(ISpecCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer;
    }

    /// <summary>
    /// 扫描源项与候选项之间的语义硬冲突，追加到 evidence 中。
    /// </summary>
    public void Scan(MatchEvidence evidence, MatchSource source, MatchCandidate candidate)
    {
        var srcText = $"{source.Project} {source.Specification}".Trim();
        var candText = $"{candidate.Project} {candidate.Specification}".Trim();

        // 规范化完全一致说明单位、区间、品牌、格式已由确定性规则证明等价。
        // 此时不再用原始数字集合做硬冲突，避免 8mm到12mm vs 10±2mm 这类等价区间被端点数字误伤。
        var canonicalEquivalent = string.Equals(
            _canonicalizer.Canonicalize(srcText),
            _canonicalizer.Canonicalize(candText),
            StringComparison.Ordinal);

        if (!canonicalEquivalent)
        {
            ScanNumericUnitConflicts(evidence, srcText, candText);
        }

        ScanDimensionTupleConflicts(evidence, srcText, candText);
        ScanComparatorConflicts(evidence, srcText, candText);
        ScanPolarityConflicts(evidence, srcText, candText);
        ScanUnknownUnitWarnings(evidence, srcText, candText);
        ScanUnknownBrandWarnings(evidence, srcText, candText);
        ScanUnsupportedFormatWarnings(evidence, srcText, candText);
    }

    // ── 数值/单位冲突 ─────────────────────────────────────────────────────

    // 温度量纲集合：跨温标无法线性换算，必须单独拦截
    private static readonly IReadOnlySet<string> TemperatureDimensions =
        new HashSet<string>(StringComparer.Ordinal) { "temp_c", "temp_f", "temp_k" };

    private void ScanNumericUnitConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcValues = _canonicalizer.ExtractNormalizedValues(srcText);
        var candValues = _canonicalizer.ExtractNormalizedValues(candText);

        if (srcValues.Count == 0 || candValues.Count == 0)
            return;

        // 按量纲分组
        var srcByDim = srcValues.GroupBy(v => v.Dimension)
            .ToDictionary(g => g.Key, g => g.ToList());
        var candByDim = candValues.GroupBy(v => v.Dimension)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 跨温标检测：源与候选使用了不同温标（℃/℉/K），无法自动换算，强制人工
        var srcTempDims = srcByDim.Keys.Where(TemperatureDimensions.Contains).ToList();
        var candTempDims = candByDim.Keys.Where(TemperatureDimensions.Contains).ToList();
        if (srcTempDims.Count > 0 && candTempDims.Count > 0 &&
            !srcTempDims.Intersect(candTempDims).Any())
        {
            var srcExpr = srcByDim[srcTempDims[0]][0].OriginalExpression;
            var candExpr = candByDim[candTempDims[0]][0].OriginalExpression;
            var msg = $"温度跨温标，无法自动比较：{srcExpr} vs {candExpr}";
            evidence.Issues.Add(new MatchIssue
            {
                Code = "cross_temperature_scale",
                Severity = "hard_conflict",
                FieldName = "温度",
                SourceValue = srcExpr,
                CandidateValue = candExpr,
                Message = msg,
                SuggestedAction = "请人工确认温度是否等价"
            });
            evidence.Conflicts.Add(msg);
        }

        // 同量纲数值比较：对每个两边都出现的量纲，比较"排序后的归一值集合"，
        // 避免笛卡尔积把 (220V,24V) vs (220V,24V) 误判为冲突。
        foreach (var (dim, srcList) in srcByDim)
        {
            if (!candByDim.TryGetValue(dim, out var candList))
                continue;

            if (!NumericSetsEqual(srcList, candList, out var srcExpr, out var candExpr))
            {
                var msg = $"数值不等价：{srcExpr} vs {candExpr}（量纲 {dim}）";
                evidence.Issues.Add(new MatchIssue
                {
                    Code = "numeric_unit_conflict",
                    Severity = "hard_conflict",
                    FieldName = "数值/单位",
                    SourceValue = srcExpr,
                    CandidateValue = candExpr,
                    Message = msg,
                    SuggestedAction = "数值或单位不同，请人工确认"
                });
                evidence.Conflicts.Add(msg);
            }
        }
    }

    /// <summary>
    /// 比较两组同量纲归一值是否构成同一集合。
    /// 排序后逐项比较：数量不同或任一对应项超容差 → 视为不等（返回 false），
    /// 并回填首个不一致的原始表达式用于提示。
    /// </summary>
    private static bool NumericSetsEqual(
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> srcList,
        IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)> candList,
        out string srcExpr,
        out string candExpr)
    {
        var srcSorted = srcList.OrderBy(v => v.BaseValue).ToList();
        var candSorted = candList.OrderBy(v => v.BaseValue).ToList();

        if (srcSorted.Count != candSorted.Count)
        {
            srcExpr = string.Join("、", srcSorted.Select(v => v.OriginalExpression));
            candExpr = string.Join("、", candSorted.Select(v => v.OriginalExpression));
            return false;
        }

        for (var i = 0; i < srcSorted.Count; i++)
        {
            if (AreNumericConflict(srcSorted[i].BaseValue, candSorted[i].BaseValue))
            {
                srcExpr = srcSorted[i].OriginalExpression;
                candExpr = candSorted[i].OriginalExpression;
                return false;
            }
        }

        srcExpr = string.Empty;
        candExpr = string.Empty;
        return true;
    }

    private static bool AreNumericConflict(double a, double b)
    {
        if (a == 0 && b == 0) return false;
        var maxAbs = Math.Max(Math.Abs(a), Math.Abs(b));
        return Math.Abs(a - b) / maxAbs > NumericCompareToleranceRatio;
    }

    // ── 尺寸元组冲突（位置敏感） ───────────────────────────────────────────

    // 尺寸元组：数字+可选单位(×|x|*|✕ 数字+可选单位)+，如 200mm×100mm / 200x100。
    // 与普通数值不同，元组的"位置"有语义（长×宽×高），不能排序后比较。
    private static readonly Regex DimensionTupleRegex = new(
        @"-?\d+(?:[.,]\d+)?\s*[A-Za-z%°℃℉Ωμ一-鿿]*" +
        @"(?:\s*[×x*✕╳]\s*-?\d+(?:[.,]\d+)?\s*[A-Za-z%°℃℉Ωμ一-鿿]*)+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TupleSplitRegex = new(
        @"\s*[×x*✕╳]\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TupleDimensionPartRegex = new(
        @"^\s*(?<num>-?\d+(?:[.,]\d+)?)(?<unit>[A-Za-z%°℃℉Ωμ一-鿿]*)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 尺寸元组按位置逐项比较：200×100×50 与 100×200×50 虽数值集合相同，
    /// 但长宽高分配不同，是实质冲突。修复"排序后多重集相等"会漏判这类元组的漏洞。
    /// </summary>
    private void ScanDimensionTupleConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcTuples = ExtractDimensionTuples(srcText);
        var candTuples = ExtractDimensionTuples(candText);
        if (srcTuples.Count == 0 || candTuples.Count == 0)
            return;

        // 源中每个元组，若候选存在"维数相同的同类元组"却无任一逐位相等者，则判冲突。
        // 维数不同（2D vs 3D）不在此判定，交由数值/语义层处理，避免误报。
        foreach (var (srcExpr, srcDims) in srcTuples)
        {
            var comparable = candTuples.Where(c => c.Dims.Count == srcDims.Count).ToList();
            if (comparable.Count == 0)
                continue;

            if (comparable.Any(c => TuplePositionsEqual(srcDims, c.Dims)))
                continue;

            var candExpr = comparable[0].Expr;
            var msg = $"尺寸元组不一致：{srcExpr} vs {candExpr}（按长宽高位置比较，数值分配不同）";
            evidence.Issues.Add(new MatchIssue
            {
                Code = "dimension_tuple_conflict",
                Severity = "hard_conflict",
                FieldName = "尺寸元组",
                SourceValue = srcExpr,
                CandidateValue = candExpr,
                Message = msg,
                SuggestedAction = "尺寸各维数值分配不同，请人工确认"
            });
            evidence.Conflicts.Add(msg);
        }
    }

    private List<(string Expr, List<double> Dims)> ExtractDimensionTuples(string text)
    {
        var result = new List<(string, List<double>)>();
        foreach (Match match in DimensionTupleRegex.Matches(text))
        {
            var dims = TupleSplitRegex.Split(match.Value)
                .Select(ParseTupleDimensionPart)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (dims.Count >= 2)
                result.Add((match.Value, dims));
        }

        return result;
    }

    private double? ParseTupleDimensionPart(string part)
    {
        var match = TupleDimensionPartRegex.Match(part);
        if (!match.Success)
            return null;

        var numText = match.Groups["num"].Value.Replace(",", ".");
        if (!double.TryParse(
                numText,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return null;

        var unit = match.Groups["unit"].Value;
        if (string.IsNullOrWhiteSpace(unit))
            return value;

        return _canonicalizer.TryNormalizeToBaseUnit(value, unit, out var baseValue, out var dimension) &&
               string.Equals(dimension, "length", StringComparison.Ordinal)
            ? baseValue
            : value;
    }

    private static bool TuplePositionsEqual(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (AreNumericConflict(a[i], b[i]))
                return false;
        }

        return true;
    }

    // ── 比较符/边界方向冲突 ───────────────────────────────────────────────

    private static void ScanComparatorConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        var srcOps = ExtractComparators(srcText);
        var candOps = ExtractComparators(candText);

        if (srcOps.Count == 0 || candOps.Count == 0)
            return;

        if (srcOps.SequenceEqual(candOps))
            return;

        // 只比较两边不共同出现的比较符，避免"≥下限且≤上限"这类正常范围表达
        // 因内部同时包含 ≥/≤ 被交叉比较成方向冲突。
        var srcOnlyOps = srcOps.Except(candOps).ToList();
        var candOnlyOps = candOps.Except(srcOps).ToList();
        foreach (var srcOp in srcOnlyOps)
        {
            foreach (var candOp in candOnlyOps)
            {
                if (AreComparatorConflict(srcOp, candOp))
                {
                    var msg = $"比较符方向冲突：源项 \"{srcOp}\" vs 候选 \"{candOp}\"";
                    evidence.Issues.Add(new MatchIssue
                    {
                        Code = "comparator_conflict",
                        Severity = "hard_conflict",
                        FieldName = "比较符",
                        SourceValue = srcOp,
                        CandidateValue = candOp,
                        Message = msg,
                        SuggestedAction = "比较符方向不同，请人工确认"
                    });
                    evidence.Conflicts.Add(msg);
                }
            }
        }
    }

    private static List<string> ExtractComparators(string text)
    {
        return ComparatorRegex.Matches(text)
            .Select(m => NormalizeComparator(m.Groups["op"].Value))
            .Distinct()
            .ToList();
    }

    private static string NormalizeComparator(string op)
    {
        return op switch
        {
            "不超过" or "不大于" or "小于等于" => "≤",
            "不低于" or "不小于" or "大于等于" => "≥",
            "大于" => ">",
            "小于" => "<",
            "约等于" => "≈",
            _ => op
        };
    }

    private static bool AreComparatorConflict(string a, string b)
    {
        if (a == b) return false;

        // ≥ vs ≤ / > vs < 互为冲突
        return (a == "≥" && b == "≤") || (a == "≤" && b == "≥") ||
               (a == ">"  && b == "<")  || (a == "<"  && b == ">")  ||
               (a == "≥" && b == "<")  || (a == ">"  && b == "≤") ||
               (a == "≤" && b == ">")  || (a == "<"  && b == "≥");
    }

    // ── 极性/方向反义冲突 ─────────────────────────────────────────────────

    private static void ScanPolarityConflicts(MatchEvidence evidence, string srcText, string candText)
    {
        foreach (var (termA, termB) in PolarityPairs)
        {
            var srcHasA = ContainsTerm(srcText, termA);
            var srcHasB = ContainsTerm(srcText, termB);
            var candHasA = ContainsTerm(candText, termA);
            var candHasB = ContainsTerm(candText, termB);

            // 仅当一侧"纯A极性"、另一侧"纯B极性"才判冲突。
            // 若任一侧同时出现A和B（如"正转/反转切换"），方向不明确，不下硬冲突结论，交由后续语义判断。
            var srcOnlyA = srcHasA && !srcHasB;
            var srcOnlyB = srcHasB && !srcHasA;
            var candOnlyA = candHasA && !candHasB;
            var candOnlyB = candHasB && !candHasA;

            if ((srcOnlyA && candOnlyB) || (srcOnlyB && candOnlyA))
            {
                var srcTerm = srcOnlyA ? termA : termB;
                var candTerm = candOnlyA ? termA : termB;
                var msg = $"方向/极性冲突：源项含 \"{srcTerm}\"，候选含 \"{candTerm}\"";
                evidence.Issues.Add(new MatchIssue
                {
                    Code = "polarity_conflict",
                    Severity = "hard_conflict",
                    FieldName = "方向/极性",
                    SourceValue = srcTerm,
                    CandidateValue = candTerm,
                    Message = msg,
                    SuggestedAction = "语义方向相反，请人工确认"
                });
                evidence.Conflicts.Add(msg);
            }
        }
    }

    private static bool ContainsTerm(string text, string term)
    {
        return text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void ScanUnknownUnitWarnings(MatchEvidence evidence, string srcText, string candText)
    {
        var srcUnknownUnits = _canonicalizer.ExtractUnknownUnitTokens(srcText);
        var candUnknownUnits = _canonicalizer.ExtractUnknownUnitTokens(candText);
        if (srcUnknownUnits.Count == 0 && candUnknownUnits.Count == 0)
            return;

        var srcTokens = srcUnknownUnits
            .Select(item => item.UnitToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candTokens = candUnknownUnits
            .Select(item => item.UnitToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (srcTokens.SequenceEqual(candTokens, StringComparer.OrdinalIgnoreCase))
            return;

        var srcValue = FormatUnknownTokenValue(srcUnknownUnits.Select(item => item.OriginalExpression));
        var candValue = FormatUnknownTokenValue(candUnknownUnits.Select(item => item.OriginalExpression));
        var msg = $"存在未识别单位，禁止确定性自动通过：{srcValue} vs {candValue}";
        evidence.Warnings.Add(msg);
        evidence.Issues.Add(new MatchIssue
        {
            Code = "unknown_unit_token",
            Severity = "warning",
            FieldName = "单位",
            SourceValue = srcValue,
            CandidateValue = candValue,
            Message = msg,
            SuggestedAction = "请交由 LLM 或人工确认未识别单位是否等价"
        });
    }

    private void ScanUnknownBrandWarnings(MatchEvidence evidence, string srcText, string candText)
    {
        var srcBrands = ExtractContextBrands(srcText);
        var candBrands = ExtractContextBrands(candText);
        if (srcBrands.Count == 0 || candBrands.Count == 0)
            return;

        var srcDistinct = srcBrands
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candDistinct = candBrands
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var srcCanonical = srcDistinct
            .Select(CanonicalizeBrandForComparison)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candCanonical = candDistinct
            .Select(CanonicalizeBrandForComparison)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (srcCanonical.SequenceEqual(candCanonical, StringComparer.OrdinalIgnoreCase))
            return;

        var srcValue = string.Join("、", srcDistinct);
        var candValue = string.Join("、", candDistinct);
        var msg = $"存在品牌差异或未识别品牌，禁止确定性自动通过：{srcValue} vs {candValue}";
        evidence.Warnings.Add(msg);
        evidence.Issues.Add(new MatchIssue
        {
            Code = "unknown_brand_token",
            Severity = "warning",
            FieldName = "品牌",
            SourceValue = srcValue,
            CandidateValue = candValue,
            Message = msg,
            SuggestedAction = "请交由 LLM 或人工确认品牌是否为同一实体"
        });
    }

    private static void ScanUnsupportedFormatWarnings(MatchEvidence evidence, string srcText, string candText)
    {
        var srcTokens = ExtractUnsupportedFormatTokens(srcText);
        var candTokens = ExtractUnsupportedFormatTokens(candText);
        if (srcTokens.Count == 0 && candTokens.Count == 0)
            return;

        // 两侧同类未覆盖格式完全一致时不额外拦截；差异表达需人工确认，避免 LLM 误放行。
        if (srcTokens.SequenceEqual(candTokens, StringComparer.OrdinalIgnoreCase))
            return;

        var srcValue = FormatUnknownTokenValue(srcTokens);
        var candValue = FormatUnknownTokenValue(candTokens);
        var msg = $"存在规则未覆盖的自然语言/中文数字格式，禁止自动通过：{srcValue} vs {candValue}";
        evidence.Warnings.Add(msg);
        evidence.Issues.Add(new MatchIssue
        {
            Code = "unsupported_format_token",
            Severity = "warning",
            FieldName = "格式",
            SourceValue = srcValue,
            CandidateValue = candValue,
            Message = msg,
            SuggestedAction = "请人工确认自然语言数字或格式表达是否等价"
        });
    }

    private static List<string> ExtractUnsupportedFormatTokens(string text)
    {
        var result = new List<string>();
        result.AddRange(UnsupportedChineseNumberRegex.Matches(text).Select(match => match.Value));
        result.AddRange(UnsupportedNaturalFormatRegex.Matches(text).Select(match => match.Value));
        return result
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatUnknownTokenValue(IEnumerable<string> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return distinct.Count == 0 ? "无" : string.Join("、", distinct);
    }

    private static List<string> ExtractContextBrands(string text)
    {
        return BrandContextRegex.Matches(text)
            .Select(match => NormalizeBrandToken(match.Groups["brand"].Value))
            .Where(IsValidBrandToken)
            .ToList();
    }

    private static bool IsValidBrandToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        if (NonBrandContextTokens.Contains(trimmed))
            return false;

        // 项目编号（如 B017/S030）常紧跟“品牌要求”出现在项目列里，不是品牌实体。
        if (Regex.IsMatch(trimmed, @"^[A-Za-z]\d{2,4}$"))
            return false;

        return Regex.IsMatch(trimmed, @"[A-Za-z]") ? trimmed.Length >= 3 : trimmed.Length >= 2;
    }

    private static string NormalizeBrandToken(string value)
    {
        var token = Regex.Replace(value.Trim(), @"\s+", " ");
        token = Regex.Replace(token, @"(?:\s*(?:分辨率|型号|规格|电压|功率|扭矩|转速|，|,|。|;|；).*)$", string.Empty);
        return token.Trim();
    }

    private string CanonicalizeBrandForComparison(string value)
    {
        if (_canonicalizer.TryNormalizeBrandToken(value, out var normalizedBrand))
            return normalizedBrand;

        var canonical = _canonicalizer.Canonicalize(value);
        canonical = Regex.Replace(canonical, @"\s+", " ").Trim();

        foreach (var suffix in BrandDeviceSuffixWords.OrderByDescending(item => item.Length))
        {
            if (!canonical.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            canonical = canonical[..^suffix.Length].Trim();
            break;
        }

        return canonical;
    }
}
