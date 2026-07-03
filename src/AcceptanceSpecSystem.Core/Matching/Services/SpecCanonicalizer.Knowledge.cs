using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SpecCanonicalizer
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

}
