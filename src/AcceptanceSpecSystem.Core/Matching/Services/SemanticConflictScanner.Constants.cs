using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SemanticConflictScanner
{
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

    private static readonly IReadOnlyList<string> PositivePrefixTerms =
    [
        "包含", "含有", "带有", "需要", "要求", "允许", "启用", "使用", "具备", "支持"
    ];

    private static readonly IReadOnlyList<string> NegativePrefixTerms =
    [
        "不包含", "不含", "无", "无需", "免", "非", "不带", "禁止", "禁用", "不得", "不可", "不允许"
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
        ScanNegativePrefixConflicts(evidence, srcText, candText);
        ScanUnknownUnitWarnings(evidence, srcText, candText);
        ScanUnknownBrandWarnings(evidence, srcText, candText);
        ScanUnsupportedFormatWarnings(evidence, srcText, candText);
    }

    // 温度量纲集合：跨温标无法线性换算，必须单独拦截
    private static readonly IReadOnlySet<string> TemperatureDimensions =
        new HashSet<string>(StringComparer.Ordinal) { "temp_c", "temp_f", "temp_k" };
}
