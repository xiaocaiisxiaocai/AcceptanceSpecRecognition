using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 规格文本规范化器。
/// 通过 SI 前缀引擎、品牌字典、同义表达字典消除等价差异，
/// 使"7.5kW vs 7500W"、"松下 vs Panasonic"等规范化后变成精确命中。
/// </summary>
public sealed class SpecCanonicalizer : ISpecCanonicalizer
{
    private const string DefaultKnowledgeRelativePath = "Matching/Knowledge/smart-fill-knowledge.json";

    // ── SI 前缀表（名称 → 10^n 因子）──────────────────────────────
    private static readonly IReadOnlyDictionary<string, double> SiPrefixFactors =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Y"] = 1e24,
            ["Z"] = 1e21,
            ["E"] = 1e18,
            ["P"] = 1e15,
            ["T"] = 1e12,
            ["G"] = 1e9,
            ["M"] = 1e6,
            ["k"] = 1e3,
            ["K"] = 1e3,
            ["h"] = 1e2,
            ["da"] = 1e1,
            ["d"] = 1e-1,
            ["c"] = 1e-2,
            ["m"] = 1e-3,
            ["μ"] = 1e-6,
            ["u"] = 1e-6,
            ["n"] = 1e-9,
            ["p"] = 1e-12,
            ["f"] = 1e-15,
            ["a"] = 1e-18,
        };

    // ── 量纲词根表：词根 → (量纲标识, 基准单位因子)──────────────
    // 因子 = 该词根的1单位换算成基准单位的倍数
    private static readonly IReadOnlyDictionary<string, (string Dimension, double Factor)> UnitRoots =
        new Dictionary<string, (string, double)>(StringComparer.Ordinal)
        {
            // 长度 (基准: m)
            ["m"] = ("length", 1),
            ["mm"] = ("length", 1e-3),
            ["cm"] = ("length", 1e-2),
            ["km"] = ("length", 1e3),
            ["米"] = ("length", 1),
            ["毫米"] = ("length", 1e-3),
            ["厘米"] = ("length", 1e-2),
            ["inch"] = ("length", 0.0254),
            ["in"] = ("length", 0.0254),
            ["\""] = ("length", 0.0254),
            ["″"] = ("length", 0.0254),
            ["mil"] = ("length", 2.54e-5),
            ["ft"] = ("length", 0.3048),
            ["feet"] = ("length", 0.3048),

            // 质量 (基准: g)
            ["g"] = ("mass", 1),
            ["mg"] = ("mass", 1e-3),
            ["kg"] = ("mass", 1e3),
            ["t"] = ("mass", 1e6),   // 公吨
            ["lb"] = ("mass", 453.592),
            ["oz"] = ("mass", 28.3495),

            // 时间 (基准: s)
            ["s"] = ("time", 1),
            ["sec"] = ("time", 1),
            ["秒"] = ("time", 1),
            ["min"] = ("time", 60),
            ["分钟"] = ("time", 60),
            ["h"] = ("time", 3600),
            ["hr"] = ("time", 3600),
            ["小时"] = ("time", 3600),
            ["ms"] = ("time", 1e-3),  // 直接登记，防止 m+s 误分解
            ["us"] = ("time", 1e-6),
            ["μs"] = ("time", 1e-6),

            // 功率 (基准: W)
            ["W"] = ("power", 1),
            ["w"] = ("power", 1),
            ["kW"] = ("power", 1e3),
            ["kw"] = ("power", 1e3),
            ["mW"] = ("power", 1e-3),
            ["mw"] = ("power", 1e-3),
            ["VA"] = ("power", 1),    // 视在功率，工程上等价
            ["hp"] = ("power", 745.7),
            ["PS"] = ("power", 735.5),

            // 电压 (基准: V)
            ["V"] = ("voltage", 1),
            ["v"] = ("voltage", 1),
            ["kV"] = ("voltage", 1e3),
            ["kv"] = ("voltage", 1e3),
            ["mV"] = ("voltage", 1e-3),
            ["mv"] = ("voltage", 1e-3),

            // 电流 (基准: A)
            ["A"] = ("current", 1),
            ["a"] = ("current", 1),
            ["mA"] = ("current", 1e-3),
            ["ma"] = ("current", 1e-3),
            ["kA"] = ("current", 1e3),
            ["ka"] = ("current", 1e3),

            // 频率 (基准: Hz)
            ["Hz"] = ("frequency", 1),
            ["HZ"] = ("frequency", 1),
            ["hz"] = ("frequency", 1),
            ["kHz"] = ("frequency", 1e3),
            ["KHz"] = ("frequency", 1e3),
            ["MHz"] = ("frequency", 1e6),
            ["GHz"] = ("frequency", 1e9),
            ["rpm"] = ("speed_rot", 1),
            ["rps"] = ("speed_rot", 60),
            ["r/min"] = ("speed_rot", 1),
            ["RPM"] = ("speed_rot", 1),

            // 力 (基准: N)
            ["N"] = ("force", 1),
            ["kgf"] = ("force", 9.80665),
            ["lbf"] = ("force", 4.44822),

            // 扭矩 (基准: N·m)
            ["N·m"] = ("torque", 1),
            ["Nm"] = ("torque", 1),
            ["N*m"] = ("torque", 1),
            ["N·cm"] = ("torque", 0.01),
            ["Ncm"] = ("torque", 0.01),
            ["kgf·m"] = ("torque", 9.80665),
            ["kgf·cm"] = ("torque", 0.0980665),
            ["lbf·ft"] = ("torque", 1.35582),
            ["lbf·in"] = ("torque", 0.112985),

            // 压力 (基准: Pa)
            ["Pa"] = ("pressure", 1),
            ["pa"] = ("pressure", 1),
            ["bar"] = ("pressure", 1e5),
            ["atm"] = ("pressure", 101325),
            ["psi"] = ("pressure", 6894.76),
            ["Torr"] = ("pressure", 133.322),
            ["mmHg"] = ("pressure", 133.322),
            ["MPa"] = ("pressure", 1e6),
            ["mpa"] = ("pressure", 1e6),
            ["kPa"] = ("pressure", 1e3),
            ["kpa"] = ("pressure", 1e3),
            ["kgf/cm2"] = ("pressure", 98066.5),
            ["kg/cm2"] = ("pressure", 98066.5),

            // 角度 (基准: deg)
            ["°"] = ("angle", 1),
            ["deg"] = ("angle", 1),
            ["rad"] = ("angle", 57.2958),

            // 电阻 (基准: Ω)
            ["Ω"] = ("resistance", 1),
            ["ohm"] = ("resistance", 1),
            ["kΩ"] = ("resistance", 1e3),
            ["KΩ"] = ("resistance", 1e3),
            ["MΩ"] = ("resistance", 1e6),

            // 电容 (基准: F)
            ["F"] = ("capacitance", 1),
            ["mF"] = ("capacitance", 1e-3),
            ["uF"] = ("capacitance", 1e-6),
            ["μF"] = ("capacitance", 1e-6),
            ["nF"] = ("capacitance", 1e-9),
            ["pF"] = ("capacitance", 1e-12),

            // 电感 (基准: H)
            ["H"] = ("inductance", 1),
            ["mH"] = ("inductance", 1e-3),
            ["uH"] = ("inductance", 1e-6),
            ["μH"] = ("inductance", 1e-6),

            // 温度：同温标可比数值，跨温标(℃/℉/K)不可线性换算→给不同量纲，交由冲突扫描器拦截
            ["℃"] = ("temp_c", 1),
            ["°C"] = ("temp_c", 1),
            ["°F"] = ("temp_f", 1),
            ["℉"] = ("temp_f", 1),
            ["K"] = ("temp_k", 1),

            // 数据量 (基准: bit)
            ["bit"] = ("data", 1),
            ["B"] = ("data", 8),
            ["byte"] = ("data", 8),

            // 能量 (基准: J)
            ["J"] = ("energy", 1),
            ["cal"] = ("energy", 4.184),
            ["Wh"] = ("energy", 3600),
            ["kWh"] = ("energy", 3.6e6),

            // 速度 (基准: m/s)
            ["m/s"] = ("velocity", 1),
            ["mm/s"] = ("velocity", 0.001),
            ["cm/s"] = ("velocity", 0.01),
            ["m/min"] = ("velocity", 1.0 / 60),
            ["mm/min"] = ("velocity", 0.001 / 60),
            ["km/h"] = ("velocity", 1.0 / 3.6),

            // 流量 (基准: L/min)
            ["L/min"] = ("flow_lpm", 1),
            ["l/min"] = ("flow_lpm", 1),
            ["L/h"] = ("flow_lpm", 1.0 / 60),
            ["mL/min"] = ("flow_lpm", 0.001),
            ["ml/min"] = ("flow_lpm", 0.001),

            // 非标自动化产能/节拍类复合单位
            ["upm"] = ("unit_rate_per_min", 1),
            ["uph"] = ("unit_rate_per_min", 1.0 / 60),
            ["pcs/min"] = ("piece_rate_per_min", 1),
            ["pcs/h"] = ("piece_rate_per_min", 1.0 / 60),
            ["pcs/s"] = ("piece_rate_per_min", 60),
            ["pc/min"] = ("piece_rate_per_min", 1),
            ["pc/h"] = ("piece_rate_per_min", 1.0 / 60),
            ["pc/s"] = ("piece_rate_per_min", 60),
            ["cycle/s"] = ("cycle_rate_per_sec", 1),
            ["cycle/min"] = ("cycle_rate_per_sec", 1.0 / 60),
            ["ct/s"] = ("count_rate_per_sec", 1),
            ["ct/min"] = ("count_rate_per_sec", 1.0 / 60),
            ["tray/min"] = ("tray_rate_per_min", 1),
            ["tray/h"] = ("tray_rate_per_min", 1.0 / 60),
            ["站/min"] = ("station_rate_per_min", 1),
            ["站/h"] = ("station_rate_per_min", 1.0 / 60),
            ["ppm"] = ("ppm", 1),
            ["kppm"] = ("ppm", 1e3),
            ["qps"] = ("query_rate_per_sec", 1),
            ["kqps"] = ("query_rate_per_sec", 1e3),
            ["GOPS"] = ("ai_ops", 1),
            ["gops"] = ("ai_ops", 1),
            ["TOPS"] = ("ai_ops", 1e3),
            ["tops"] = ("ai_ops", 1e3),
            ["瓶/s"] = ("bottle_rate_per_sec", 1),
            ["瓶/min"] = ("bottle_rate_per_sec", 1.0 / 60),
            ["像素"] = ("pixel_count", 1),
            ["万像素"] = ("pixel_count", 1e4),
            ["MP"] = ("pixel_count", 1e6),
            ["mp"] = ("pixel_count", 1e6),

            // 比例/百分数（基准: 1）
            ["%"] = ("ratio", 0.01),
        };

    // ── 品牌字典（统一到英文规范名）────────────────────────────────
    private static readonly IReadOnlyDictionary<string, string> BrandNormMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 日系
            ["松下"] = "Panasonic",
            ["panasonic"] = "Panasonic",
            ["ABB"] = "ABB",
            ["abb"] = "ABB",
            ["阿西布朗"] = "ABB",
            ["欧姆龙"] = "Omron",
            ["omron"] = "Omron",
            ["三菱"] = "Mitsubishi",
            ["mitsubishi"] = "Mitsubishi",
            ["基恩士"] = "Keyence",
            ["keyence"] = "Keyence",
            ["安川"] = "Yaskawa",
            ["yaskawa"] = "Yaskawa",
            ["发那科"] = "Fanuc",
            ["fanuc"] = "Fanuc",
            ["日本电产"] = "Nidec",
            ["nidec"] = "Nidec",
            ["富士"] = "Fuji",
            ["fuji"] = "Fuji",
            ["东芝"] = "Toshiba",
            ["toshiba"] = "Toshiba",
            ["日立"] = "Hitachi",
            ["hitachi"] = "Hitachi",
            ["SMC"] = "SMC",
            ["smc"] = "SMC",
            ["CKD"] = "CKD",
            ["ckd"] = "CKD",
            ["费斯托"] = "Festo",
            ["festo"] = "Festo",
            ["亚德客"] = "Airtac",
            ["airtac"] = "Airtac",
            ["MISUMI"] = "Misumi",
            ["misumi"] = "Misumi",
            ["米思米"] = "Misumi",
            ["上银"] = "HIWIN",
            ["hiwin"] = "HIWIN",
            ["THK"] = "THK",
            ["thk"] = "THK",
            ["IAI"] = "IAI",
            ["iai"] = "IAI",
            // 欧系
            ["施耐德"] = "Schneider",
            ["schneider"] = "Schneider",
            ["西门子"] = "Siemens",
            ["siemens"] = "Siemens",
            ["博世"] = "Bosch",
            ["bosch"] = "Bosch",
            ["力士乐"] = "Rexroth",
            ["rexroth"] = "Rexroth",
            ["菲尼克斯"] = "Phoenix",
            ["phoenix"] = "Phoenix",
            ["皮尔磁"] = "Pilz",
            ["pilz"] = "Pilz",
            ["倍福"] = "Beckhoff",
            ["beckhoff"] = "Beckhoff",
            ["倍加福"] = "Pepperl-Fuchs",
            ["pepperl-fuchs"] = "Pepperl-Fuchs",
            ["Pepperl Fuchs"] = "Pepperl-Fuchs",
            ["pepperl fuchs"] = "Pepperl-Fuchs",
            ["图尔克"] = "Turck",
            ["turck"] = "Turck",
            ["西克"] = "Sick",
            ["sick"] = "Sick",
            ["易福门"] = "IFM",
            ["ifm"] = "IFM",
            ["巴鲁夫"] = "Balluff",
            ["balluff"] = "Balluff",
            ["台达"] = "Delta",
            ["delta"] = "Delta",
            ["魏德米勒"] = "Weidmuller",
            ["weidmuller"] = "Weidmuller",
            // 美系
            ["艾伦-布拉德利"] = "Allen-Bradley",
            ["allen-bradley"] = "Allen-Bradley",
            ["罗克韦尔"] = "Rockwell",
            ["rockwell"] = "Rockwell",
            ["爱默生"] = "Emerson",
            ["emerson"] = "Emerson",
            ["霍尼韦尔"] = "Honeywell",
            ["honeywell"] = "Honeywell",
            ["邦纳"] = "Banner",
            ["banner"] = "Banner",
            ["康耐视"] = "Cognex",
            ["cognex"] = "Cognex",
            ["派克"] = "Parker",
            ["parker"] = "Parker",
            ["英飞凌"] = "Infineon",
            ["infineon"] = "Infineon",
            // 韩系
            ["三星"] = "Samsung",
            ["samsung"] = "Samsung",
            ["LG"] = "LG",
            ["lg"] = "LG",
            ["现代"] = "Hyundai",
            ["hyundai"] = "Hyundai",
            // 台系
            ["研华"] = "Advantech",
            ["advantech"] = "Advantech",
            ["威纶通"] = "Weintek",
            ["weintek"] = "Weintek",
            // 视觉/机器人
            ["巴斯勒"] = "Basler",
            ["basler"] = "Basler",
            ["海康机器人"] = "Hikrobot",
            ["hikrobot"] = "Hikrobot",
            ["大恒"] = "Daheng",
            ["daheng"] = "Daheng",
            ["库卡"] = "Kuka",
            ["kuka"] = "Kuka",
            ["川崎"] = "Kawasaki",
            ["kawasaki"] = "Kawasaki",
            ["爱普生"] = "Epson",
            ["epson"] = "Epson",
            ["优傲"] = "Universal Robots",
            ["universal robots"] = "Universal Robots",
            ["史陶比尔"] = "Staubli",
            ["staubli"] = "Staubli",
            // 国产自动化/视觉/机器人
            ["汇川"] = "Inovance",
            ["inovance"] = "Inovance",
            ["英威腾"] = "INVT",
            ["invt"] = "INVT",
            ["雷赛"] = "Leadshine",
            ["leadshine"] = "Leadshine",
            ["新时达"] = "STEP",
            ["step"] = "STEP",
            ["禾川"] = "HCFA",
            ["hcfa"] = "HCFA",
            ["众为兴"] = "Adtech",
            ["adtech"] = "Adtech",
            ["固高"] = "Googoltech",
            ["googoltech"] = "Googoltech",
            ["正运动"] = "Zmotion",
            ["zmotion"] = "Zmotion",
            ["奥普特"] = "OPT",
            ["opt"] = "OPT",
            ["迈德威视"] = "MindVision",
            ["mindvision"] = "MindVision",
            ["遨博"] = "Aubo",
            ["aubo"] = "Aubo",
            ["节卡"] = "Jaka",
            ["jaka"] = "Jaka",
            ["大族机器人"] = "Han's Robot",
            ["大族"] = "Han's Robot",
            ["han"] = "Han's Robot",
            ["han's robot"] = "Han's Robot",
            ["图漾"] = "Percipio",
            ["percipio"] = "Percipio",
            ["华睿"] = "Huaray",
            ["huaray"] = "Huaray",
            ["镭神"] = "Leishen",
            ["leishen"] = "Leishen",
            ["埃斯顿"] = "Estun",
            ["estun"] = "Estun",
            ["越疆"] = "Dobot",
            ["dobot"] = "Dobot",
            ["梅卡曼德"] = "Mech-Mind",
            ["mech-mind"] = "Mech-Mind",
            ["mech mind"] = "Mech-Mind",
        };

    private static readonly IReadOnlyList<string> BrandAdjacentDeviceWords =
    [
        "plc", "hmi", "io", "i/o",
        "变频器", "伺服", "伺服电机", "伺服驱动", "电机", "马达", "编码器",
        "传感器", "接近开关", "光电", "光电开关", "安全光栅", "光栅", "读码器",
        "相机", "工业相机", "3d相机", "深度相机", "镜头", "控制器", "控制卡", "模块", "安全模块",
        "触摸屏", "交换机",
        "断路器", "接触器", "继电器", "电源", "端子", "按钮", "指示灯",
        "气缸", "电磁阀", "液压阀", "电缸", "滑轨", "导轨", "丝杆",
        "机器人", "机械臂", "协作臂", "协作机器人", "安全继电器",
        "光源", "雷达", "激光雷达", "工控机", "步进驱动"
    ];

    // ── 同义表达字典（统一到标准形式）────────────────────────────────
    // 安全原则：只收录"多字或符号、无歧义、不翻转语义"的表达。
    //   1. 单字语气词（应/须/要/可）不收：单字替换会污染正常词（要求→需求、适应→适需）。
    //   2. 软等价词（最多/至少/大约）不收：它们隐含方向，归一成符号有翻转语义的风险。
    //   3. 区间连接符（到/至/—）不在此处理：单字"到"会污染"达到/收到"，改由数字间专用正则识别。
    private static readonly IReadOnlyDictionary<string, string> SynonymMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 比较符同义（多字或符号，无歧义，安全）
            ["不超过"] = "≤",
            ["不大于"] = "≤",
            ["小于等于"] = "≤",
            ["小于或等于"] = "≤",
            ["不多于"] = "≤",
            ["<="] = "≤",
            ["=<"] = "≤",
            ["≦"] = "≤",
            ["不低于"] = "≥",
            ["不小于"] = "≥",
            ["大于等于"] = "≥",
            ["大于或等于"] = "≥",
            ["不少于"] = "≥",
            [">="] = "≥",
            ["=>"] = "≥",
            ["≧"] = "≥",
            ["约等于"] = "≈",
            ["近似等于"] = "≈",
            ["~="] = "≈",
            ["≅"] = "≈",
            ["≒"] = "≈",
            ["不等于"] = "≠",
            ["!="] = "≠",
            ["<>"] = "≠",
            // 注意："小于"/"大于" 是双字词，但仍可能出现在复合词中（如"远大于"/"绝对小于"）。
            // 这里通过 ApplySynonymMap 调用时按词长降序替换来保证多字词优先，
            // "大于等于"/"小于等于" 等长词先命中，单独的"小于"/"大于" 只在未被更长词覆盖时才替换。
            ["小于"] = "<",
            ["大于"] = ">",
            // 公差正负号符号统一（符号，安全）
            ["+/-"] = "±",
            ["+/−"] = "±",
            ["±"] = "±",
            // 乘号统一（尺寸元组 200×100 / 200✕100 → 200*100）
            ["×"] = "*",
            ["✕"] = "*",
            ["╳"] = "*",
            // 一元负号统一（U+2212 数学减号 → ASCII -）
            ["−"] = "-",
            // 温度符号统一（符号，安全）
            ["摄氏度"] = "°C",
            ["℃"] = "°C",
            ["华氏度"] = "°F",
            ["℉"] = "°F",
            // 电阻符号（ohm 多字，安全；Ω 已是目标形）
            ["ohm"] = "Ω",
            // 全角括号归一
            ["（"] = "(",
            ["）"] = ")",
        };

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    // 移除汉字相邻处的空格：Word/Excel 复制的规格常在汉字间夹换行/空格，
    // "气缸 上升" 与 "气缸上升" 必须归一为同一文本，否则规范化精确匹配会漏。
    // 仅当空格至少一侧是汉字时移除，纯英文词间空格（如 "max load"）保留。
    private static readonly Regex CjkAdjacentSpaceRegex = new(
        @"(?<=[一-鿿])\s+|\s+(?=[一-鿿])",
        RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, (string Dimension, double Factor)> _unitRoots;
    private readonly IReadOnlyDictionary<string, string> _brandNormMap;
    private readonly IReadOnlyList<string> _brandAdjacentDeviceWords;
    private readonly Regex _brandNormRegex;
    private readonly Regex _brandDeviceSpacingRegex;
    private readonly Regex _numericUnitRegex;
    private readonly Regex _unknownCompoundUnitRegex;
    private readonly Regex _toleranceIntervalRegex;
    private readonly Regex _rangeIntervalRegex;
    private readonly Regex _hyphenRangeIntervalRegex;

    public SpecCanonicalizer()
        : this(LoadDefaultExternalKnowledge())
    {
    }

    public SpecCanonicalizer(string? externalKnowledgePath)
        : this(LoadExternalKnowledge(externalKnowledgePath))
    {
    }

    private SpecCanonicalizer(ExternalMatchingKnowledge? externalKnowledge)
    {
        _unitRoots = BuildUnitRoots(externalKnowledge);
        _brandNormMap = BuildBrandNormMap(externalKnowledge);
        _brandAdjacentDeviceWords = BuildBrandAdjacentDeviceWords(externalKnowledge);

        var brandAlternation = BuildRegexAlternation(_brandNormMap.Keys);
        var brandDeviceAlternation = BuildRegexAlternation(_brandAdjacentDeviceWords);
        var brandValueAlternation = BuildRegexAlternation(_brandNormMap.Values);
        _brandNormRegex = new Regex(
            $@"(?<![A-Za-z0-9一-鿿])(?<brand>{brandAlternation})(?=(?![A-Za-z0-9一-鿿])|(?:{brandDeviceAlternation}))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _brandDeviceSpacingRegex = new Regex(
            $@"(?<brand>{brandValueAlternation})(?<space>\s+)(?<device>{brandDeviceAlternation})(?![A-Za-z0-9一-鿿])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var numericUnitTokenPattern =
            $@"(?:{BuildRegexAlternation(_unitRoots.Keys.Concat(["%"]))}|[A-Za-z][A-Za-z0-9]*(?:/[A-Za-z0-9一-鿿]+)?|[一-鿿]{{1,4}}(?:/[A-Za-z0-9一-鿿]+)?)";

        _numericUnitRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<num>-?\d+(?:[.,]\d+)?(?:[eE][+-]?\d+)?)\s*(?<unit>{numericUnitTokenPattern})",
            RegexOptions.Compiled);
        _unknownCompoundUnitRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<expr>(?<num>-?\d+(?:[.,]\d+)?(?:[eE][+-]?\d+)?)\s*(?<known>{BuildRegexAlternation(_unitRoots.Keys)})/(?<unknown>[一-鿿A-Za-z][A-Za-z0-9一-鿿]*))(?![A-Za-z0-9一-鿿])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _toleranceIntervalRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<center>-?\d+(?:\.\d+)?)\s*±\s*(?<tol>\d+(?:\.\d+)?)\s*(?<unit>{numericUnitTokenPattern})?",
            RegexOptions.Compiled);
        _rangeIntervalRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<lo>-?\d+(?:\.\d+)?)\s*(?<u1>{numericUnitTokenPattern})?\s*(?:~|到|至)\s*(?<hi>-?\d+(?:\.\d+)?)\s*(?<u2>{numericUnitTokenPattern})?",
            RegexOptions.Compiled);
        _hyphenRangeIntervalRegex = new Regex(
            $@"(?<![\d.~±A-Za-z-])(?<lo>\d+(?:\.\d+)?)\s*-\s*(?<hi>\d+(?:\.\d+)?)\s*(?<unit>{numericUnitTokenPattern})?",
            RegexOptions.Compiled);
    }

    public string Canonicalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = text
            .Replace(" ", " ")
            .Replace("​", string.Empty)
            .Replace("﻿", string.Empty)
            .Trim();

        // 全角数字/字母转半角
        result = FullWidthToHalfWidth(result);

        // 同义词替换（比较符 / 语气词 / 温度符号）
        result = ApplySynonymMap(result);

        // 品牌归一
        result = ApplyBrandNorm(result);
        result = NormalizeBrandDeviceSpacing(result);

        // 区间归一（必须在数值单位归一之前，否则单值会先被替换破坏区间识别）
        // 公差型 10±2 与范围型 8~12/8到12 统一为同一通带 token，使等价区间变成精确命中。
        result = NormalizeIntervals(result);

        // 数值+单位归一
        result = NormalizeNumericUnits(result);

        // 空白归一
        result = WhitespaceRegex.Replace(result, " ").Trim().ToLowerInvariant();

        // 移除汉字相邻处的空格（换行/分词差异归一）
        result = CjkAdjacentSpaceRegex.Replace(result, string.Empty);

        return result;
    }

    public bool TryNormalizeToBaseUnit(
        double value,
        string unitToken,
        out double baseValue,
        out string baseDimension)
    {
        baseValue = value;
        baseDimension = string.Empty;

        if (string.IsNullOrWhiteSpace(unitToken))
            return false;

        var unit = unitToken.Trim();

        // 直接查词根表（优先全词，处理 ms/us 等避免误前缀分解）
        if (_unitRoots.TryGetValue(unit, out var direct))
        {
            if (double.IsNaN(direct.Factor))
                return false; // 跨温标等不支持自动换算的

            baseValue = value * direct.Factor;
            baseDimension = direct.Dimension;
            return true;
        }

        // SI 前缀分解：尝试从最长前缀开始匹配
        foreach (var (prefix, prefixFactor) in SiPrefixFactors.OrderByDescending(p => p.Key.Length))
        {
            if (!unit.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var root = unit[prefix.Length..];
            if (!_unitRoots.TryGetValue(root, out var rootEntry))
                continue;

            if (double.IsNaN(rootEntry.Factor))
                return false;

            baseValue = value * prefixFactor * rootEntry.Factor;
            baseDimension = rootEntry.Dimension;
            return true;
        }

        return false;
    }

    public IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)>
        ExtractNormalizedValues(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var results = new List<(double, string, string)>();
        foreach (Match match in _numericUnitRegex.Matches(text))
        {
            var numStr = match.Groups["num"].Value.Replace(",", ".");
            var unit = match.Groups["unit"].Value;

            if (!double.TryParse(numStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var num))
                continue;

            if (!TryNormalizeToBaseUnit(num, unit, out var baseVal, out var dim))
                continue;

            results.Add((baseVal, dim, match.Value));
        }

        return results;
    }

    public IReadOnlyList<(string UnitToken, string OriginalExpression)> ExtractUnknownUnitTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var results = new List<(string, string)>();
        foreach (Match match in _unknownCompoundUnitRegex.Matches(text))
        {
            var known = match.Groups["known"].Value;
            if (!TryNormalizeToBaseUnit(1, known, out _, out var knownDim) ||
                !string.Equals(knownDim, "time", StringComparison.Ordinal))
            {
                continue;
            }

            var unit = $"{match.Groups["known"].Value}/{match.Groups["unknown"].Value}";
            if (IsKnownCompoundDenominator(match.Groups["unknown"].Value))
                continue;

            if (TryNormalizeToBaseUnit(1, unit, out _, out _))
                continue;

            results.Add((unit, match.Groups["expr"].Value));
        }

        foreach (Match match in _numericUnitRegex.Matches(text))
        {
            var unit = match.Groups["unit"].Value;
            if (IsUnknownCjkUnitSeparatedByWhitespace(match))
                continue;

            if (IsKnownPlainSuffix(unit))
                continue;

            if (TryNormalizeToBaseUnit(1, unit, out _, out _))
                continue;

            // 经过上面的归一尝试仍无法识别，且 token 是纯汉字（无字母/数字）构成的多字词，
            // 判定为名词词组而非量纲单位（如"边吸取""米高""分钟可调"），跳过以避免误报。
            // 真正的中文单位（米/秒/毫米等）已在上面的 TryNormalizeToBaseUnit 命中并 continue。
            if (IsCjkNounPhraseUnit(unit))
                continue;

            results.Add((unit, match.Value));
        }

        return results
            .DistinctBy(item => $"{item.Item1}\u001F{item.Item2}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static string FullWidthToHalfWidth(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // 全角字母数字：U+FF01~U+FF5E → 对应半角
            if (ch >= '！' && ch <= '～')
                sb.Append((char)(ch - 0xFEE0));
            // 全角空格
            else if (ch == '　')
                sb.Append(' ');
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string ApplySynonymMap(string text)
    {
        // 按关键词长度从长到短替换，避免短词先替换后破坏长词
        foreach (var (from, to) in SynonymMap.OrderByDescending(p => p.Key.Length))
        {
            text = text.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    public bool TryNormalizeBrandToken(string brandToken, out string normalizedBrand)
    {
        normalizedBrand = string.Empty;
        if (string.IsNullOrWhiteSpace(brandToken))
            return false;

        if (_brandNormMap.TryGetValue(brandToken.Trim(), out var normalized))
        {
            normalizedBrand = normalized;
            return true;
        }

        return false;
    }

    private string ApplyBrandNorm(string text)
    {
        return _brandNormRegex.Replace(text, match =>
        {
            var token = match.Groups["brand"].Value;
            return _brandNormMap.TryGetValue(token, out var normalized) ? normalized : token;
        });
    }

    private string NormalizeBrandDeviceSpacing(string text)
    {
        return _brandDeviceSpacingRegex.Replace(text, match =>
            $"{match.Groups["brand"].Value}{match.Groups["device"].Value}");
    }

    private static string BuildRegexAlternation(IEnumerable<string> values)
    {
        return string.Join(
            "|",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .Select(Regex.Escape));
    }

    private static bool IsKnownPlainSuffix(string unit)
    {
        return string.Equals(unit, "%", StringComparison.Ordinal) ||
               string.Equals(unit, "万像素", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "像素", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "pcs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "pc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownCompoundDenominator(string unit)
    {
        return string.Equals(unit, "s", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "sec", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "min", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(unit, "hr", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsUnknownCjkUnitSeparatedByWhitespace(Match match)
    {
        var unit = match.Groups["unit"].Value;
        if (!Regex.IsMatch(unit, @"^[一-鿿]"))
            return false;

        if (CanNormalizeUnitToken(unit))
            return false;

        var value = match.Value;
        return value.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// 判断无法归一的 token 是否为中文名词（词组）而非量纲单位。
    /// 前提：调用方已先尝试 <see cref="TryNormalizeToBaseUnit"/>，真正的中文单位（米/秒/毫米等）
    /// 已被命中并排除，能进入此方法的纯汉字 token 必然无法归一为已知量纲，基本是名词
    /// （如"边""边吸取""米高""分钟可调"）。
    /// 规则：token 去掉斜杠分隔后全部由汉字构成（不含字母/数字），即判定为名词，
    /// 跳过以避免误报"未识别单位"冲突。
    /// </summary>
    private static bool IsCjkNounPhraseUnit(string unit)
    {
        if (string.IsNullOrEmpty(unit))
            return false;

        // 含字母或数字的 token（如 m/min、10mm、rpm）不是纯中文名词，
        // 交由常规未识别单位逻辑处理
        if (unit.Any(ch => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || char.IsDigit(ch)))
            return false;

        // 去掉斜杠（复合单位分隔符）后统计汉字
        var coreChars = unit.Where(ch => ch != '/').ToArray();
        if (coreChars.Length == 0)
            return false;

        // 全部为 CJK 汉字才判定为名词（合法中文单位已在上游 TryNormalizeToBaseUnit 命中排除）
        return coreChars.All(ch => ch >= '一' && ch <= '鿿');
    }

    private bool CanNormalizeUnitToken(string unit)
    {
        if (_unitRoots.ContainsKey(unit))
            return true;

        foreach (var (prefix, _) in SiPrefixFactors.OrderByDescending(p => p.Key.Length))
        {
            if (!unit.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            if (_unitRoots.ContainsKey(unit[prefix.Length..]))
                return true;
        }

        return false;
    }

    private string NormalizeNumericUnits(string text)
    {
        // 替换所有能归一的数值+单位为 "基准值基准单位" 形式
        return _numericUnitRegex.Replace(text, match =>
        {
            var numStr = match.Groups["num"].Value.Replace(",", ".");
            var unit = match.Groups["unit"].Value;

            if (!double.TryParse(numStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var num))
                return match.Value;

            if (!TryNormalizeToBaseUnit(num, unit, out var baseVal, out var dim))
                return match.Value;

            return $"{FormatNumber(baseVal)}[{dim}]";
        });
    }

    /// <summary>
    /// 区间归一：把公差型(A±B)与范围型(A~B / A到B / A至B / A-B)统一为同一通带 token
    /// 「lo~hi[dim]」，使等价区间（如 10±2 与 8到12）在规范化后变成精确命中。
    /// 端点各自按自身单位归一到基准量纲，仅当两端量纲一致（或皆无单位）时才输出区间 token，
    /// 否则保留原文，交由冲突扫描器处理。
    /// </summary>
    private string NormalizeIntervals(string text)
    {
        // 1. 公差型 A±B[unit]
        text = _toleranceIntervalRegex.Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["center"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var center) ||
                !double.TryParse(match.Groups["tol"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var tol))
            {
                return match.Value;
            }

            var unit = match.Groups["unit"].Value;
            return BuildIntervalToken(center - tol, unit, center + tol, unit) ?? match.Value;
        });

        // 2. 范围型 A[u1](~|到|至)B[u2]
        text = _rangeIntervalRegex.Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["lo"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lo) ||
                !double.TryParse(match.Groups["hi"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var hi))
            {
                return match.Value;
            }

            var u1 = match.Groups["u1"].Value;
            var u2 = match.Groups["u2"].Value;
            // 缺失的一端单位用对端补（"8到12mm" → 两端都按 mm）
            var loUnit = string.IsNullOrEmpty(u1) ? u2 : u1;
            var hiUnit = string.IsNullOrEmpty(u2) ? u1 : u2;
            return BuildIntervalToken(lo, loUnit, hi, hiUnit) ?? match.Value;
        });

        // 3. 连字符型 A-B[unit]（保守：正则已限定两数非负，再要求 lo<hi 才视为区间）
        text = _hyphenRangeIntervalRegex.Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["lo"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lo) ||
                !double.TryParse(match.Groups["hi"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var hi))
            {
                return match.Value;
            }

            if (lo >= hi)
            {
                // 非递增：可能是负号或减法，保守保留原文
                return match.Value;
            }

            var unit = match.Groups["unit"].Value;
            return BuildIntervalToken(lo, unit, hi, unit) ?? match.Value;
        });

        return text;
    }

    /// <summary>
    /// 构建区间 token。两端各自按单位归一到基准量纲，量纲一致（或皆无单位）才返回 token，
    /// 否则返回 null（由调用方保留原文）。输出形如 "8~12[length]" 或无单位 "8~12"。
    /// </summary>
    private string? BuildIntervalToken(double lo, string loUnit, double hi, string hiUnit)
    {
        // 归一单个端点：无单位返回 (原值, "")；有单位且能归一返回 (基准值, 量纲)；无法归一返回 null
        (double Value, string Dim)? NormalizeEndpoint(double value, string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return (value, string.Empty);
            }

            return TryNormalizeToBaseUnit(value, unit, out var baseValue, out var dim)
                ? (baseValue, dim)
                : null;
        }

        var loResult = NormalizeEndpoint(lo, loUnit);
        var hiResult = NormalizeEndpoint(hi, hiUnit);
        if (loResult == null || hiResult == null)
        {
            return null;
        }

        // 两端量纲必须一致（包括"皆无单位"，此时 Dim 均为 ""）。
        // 一端有单位一端没有时 Dim 不相等，自然被拦截，保守保留原文。
        if (!string.Equals(loResult.Value.Dim, hiResult.Value.Dim, StringComparison.Ordinal))
        {
            return null;
        }

        var dimSuffix = string.IsNullOrEmpty(loResult.Value.Dim) ? string.Empty : $"[{loResult.Value.Dim}]";
        return $"{FormatNumber(loResult.Value.Value)}~{FormatNumber(hiResult.Value.Value)}{dimSuffix}";
    }

    /// <summary>
    /// 数值格式化：整数不带小数点（避免 1000.0 vs 1000 不等），其余用 G6 有效数字。
    /// </summary>
    private static string FormatNumber(double value)
    {
        return value == Math.Floor(value) && !double.IsInfinity(value)
            ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IReadOnlyDictionary<string, (string Dimension, double Factor)> BuildUnitRoots(
        ExternalMatchingKnowledge? externalKnowledge)
    {
        var result = new Dictionary<string, (string Dimension, double Factor)>(
            UnitRoots,
            StringComparer.Ordinal);

        foreach (var unit in externalKnowledge?.Units ?? [])
        {
            if (string.IsNullOrWhiteSpace(unit.Dimension) || unit.Tokens.Count == 0)
                continue;

            foreach (var token in unit.Tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                result[token.Trim()] = (unit.Dimension.Trim(), unit.Factor);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildBrandNormMap(
        ExternalMatchingKnowledge? externalKnowledge)
    {
        var result = new Dictionary<string, string>(BrandNormMap, StringComparer.OrdinalIgnoreCase);

        foreach (var brand in externalKnowledge?.Brands ?? [])
        {
            if (string.IsNullOrWhiteSpace(brand.Canonical))
                continue;

            var canonical = brand.Canonical.Trim();
            result[canonical] = canonical;
            foreach (var alias in brand.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    result[alias.Trim()] = canonical;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> BuildBrandAdjacentDeviceWords(
        ExternalMatchingKnowledge? externalKnowledge)
    {
        return BrandAdjacentDeviceWords
            .Concat(externalKnowledge?.BrandDeviceWords ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExternalMatchingKnowledge? LoadDefaultExternalKnowledge()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var path = Path.Combine(
            baseDirectory,
            DefaultKnowledgeRelativePath.Replace('/', Path.DirectorySeparatorChar));

        return LoadExternalKnowledge(path, throwIfMissing: false);
    }

    private static ExternalMatchingKnowledge? LoadExternalKnowledge(string? path, bool throwIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
        {
            if (throwIfMissing)
                throw new FileNotFoundException("智能填充外置知识库文件不存在", resolvedPath);

            return null;
        }

        var json = File.ReadAllText(resolvedPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ExternalMatchingKnowledge>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
    }

    private sealed class ExternalMatchingKnowledge
    {
        [JsonPropertyName("brands")]
        public List<ExternalBrandRule> Brands { get; init; } = [];

        [JsonPropertyName("brandDeviceWords")]
        public List<string> BrandDeviceWords { get; init; } = [];

        [JsonPropertyName("units")]
        public List<ExternalUnitRule> Units { get; init; } = [];
    }

    private sealed class ExternalBrandRule
    {
        [JsonPropertyName("canonical")]
        public string Canonical { get; init; } = string.Empty;

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; init; } = [];
    }

    private sealed class ExternalUnitRule
    {
        [JsonPropertyName("dimension")]
        public string Dimension { get; init; } = string.Empty;

        [JsonPropertyName("factor")]
        public double Factor { get; init; } = 1;

        [JsonPropertyName("tokens")]
        public List<string> Tokens { get; init; } = [];
    }
}
