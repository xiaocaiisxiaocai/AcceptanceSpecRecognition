namespace AcceptanceSpecSystem.Core.Matching.Models;

/// <summary>
/// 匹配知识配置
/// </summary>
public sealed class MatchingKnowledge
{
    /// <summary>
    /// 品牌与组织别名映射
    /// </summary>
    public Dictionary<string, string> EntityAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位归一化映射
    /// </summary>
    public Dictionary<string, string> UnitAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单位换算系数（兼容保留，不在页面展示）
    /// </summary>
    public Dictionary<string, decimal> UnitFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 关键字段槽位别名映射
    /// </summary>
    public Dictionary<string, string> FieldAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 冲突词对
    /// </summary>
    public List<(string Left, string Right)> ConflictPairs { get; set; } = [];

    /// <summary>
    /// 创建默认知识配置
    /// </summary>
    public static MatchingKnowledge CreateDefault()
    {
        return new MatchingKnowledge
        {
            EntityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["panasonic"] = "松下",
                ["松下"] = "松下",
                ["mitsubishi"] = "三菱",
                ["三菱"] = "三菱",
                ["delta"] = "台达",
                ["台达"] = "台达",
                ["foxconn"] = "富士康",
                ["富士康"] = "富士康",
                ["omron"] = "欧姆龙",
                ["欧姆龙"] = "欧姆龙",
                ["siemens"] = "西门子",
                ["西门子"] = "西门子",
                ["abb"] = "ABB",
                ["施耐德"] = "施耐德",
                ["schneider"] = "施耐德",
                ["keyence"] = "基恩士",
                ["基恩士"] = "基恩士",
                ["fanuc"] = "发那科",
                ["发那科"] = "发那科",
                ["yaskawa"] = "安川",
                ["安川"] = "安川",
                ["smc"] = "SMC",
                ["festo"] = "费斯托",
                ["费斯托"] = "费斯托",
                ["ti"] = "德州仪器",
                ["texas instruments"] = "德州仪器",
                ["德州仪器"] = "德州仪器",
                ["infineon"] = "英飞凌",
                ["英飞凌"] = "英飞凌",
                ["st"] = "意法半导体",
                ["stmicroelectronics"] = "意法半导体",
                ["意法半导体"] = "意法半导体",
                ["nxp"] = "恩智浦",
                ["恩智浦"] = "恩智浦",
                ["onsemi"] = "安森美",
                ["安森美"] = "安森美",
                ["renesas"] = "瑞萨",
                ["瑞萨"] = "瑞萨",
                ["microchip"] = "Microchip",
                ["adi"] = "亚德诺",
                ["analog devices"] = "亚德诺",
                ["亚德诺"] = "亚德诺",
                ["intel"] = "英特尔",
                ["英特尔"] = "英特尔",
                ["amd"] = "AMD",
                ["nvidia"] = "英伟达",
                ["英伟达"] = "英伟达",
                ["qualcomm"] = "高通",
                ["高通"] = "高通",
                ["micron"] = "美光",
                ["美光"] = "美光",
                ["samsung"] = "三星",
                ["三星"] = "三星",
                ["tsmc"] = "台积电",
                ["台积电"] = "台积电",
                ["smic"] = "中芯国际",
                ["中芯国际"] = "中芯国际"
            },
            UnitAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["m"] = "m",
                ["米"] = "m",
                ["cm"] = "cm",
                ["厘米"] = "cm",
                ["mm"] = "mm",
                ["毫米"] = "mm",
                ["um"] = "um",
                ["μm"] = "um",
                ["µm"] = "um",
                ["微米"] = "um",
                ["nm"] = "nm",
                ["纳米"] = "nm",
                ["℃"] = "degc",
                ["°c"] = "degc",
                ["摄氏度"] = "degc",
                ["度"] = "degc",
                ["v"] = "v",
                ["伏"] = "v",
                ["volt"] = "v",
                ["vdc"] = "v",
                ["vac"] = "v",
                ["mv"] = "mv",
                ["毫伏"] = "mv",
                ["kv"] = "kv",
                ["千伏"] = "kv",
                ["a"] = "a",
                ["安"] = "a",
                ["amp"] = "a",
                ["amps"] = "a",
                ["ma"] = "ma",
                ["毫安"] = "ma",
                ["ua"] = "ua",
                ["μa"] = "ua",
                ["µa"] = "ua",
                ["微安"] = "ua",
                ["w"] = "w",
                ["瓦"] = "w",
                ["kw"] = "kw",
                ["千瓦"] = "kw",
                ["mw"] = "mw",
                ["毫瓦"] = "mw",
                ["hz"] = "hz",
                ["赫兹"] = "hz",
                ["khz"] = "khz",
                ["千赫"] = "khz",
                ["mhz"] = "mhz",
                ["兆赫"] = "mhz",
                ["ghz"] = "ghz",
                ["吉赫"] = "ghz",
                ["kpa"] = "kpa",
                ["千帕"] = "kpa",
                ["mpa"] = "mpa",
                ["兆帕"] = "mpa",
                ["kg/cm2"] = "kg/cm2",
                ["kg/cm3"] = "kg/cm3",
                ["kgf/cm2"] = "kg/cm2",
                ["n"] = "n",
                ["牛"] = "n",
                ["kn"] = "kn",
                ["千牛"] = "kn",
                ["kg"] = "kg",
                ["千克"] = "kg",
                ["g"] = "g",
                ["克"] = "g",
                ["mg"] = "mg",
                ["毫克"] = "mg",
                ["s"] = "s",
                ["秒"] = "s",
                ["sec"] = "s",
                ["ms"] = "ms",
                ["毫秒"] = "ms",
                ["us"] = "us",
                ["μs"] = "us",
                ["µs"] = "us",
                ["微秒"] = "us",
                ["ns"] = "ns",
                ["纳秒"] = "ns",
                ["min"] = "min",
                ["分钟"] = "min",
                ["hr"] = "hr",
                ["hrs"] = "hr",
                ["hour"] = "hr",
                ["小时"] = "hr",
                ["ohm"] = "ohm",
                ["欧姆"] = "ohm",
                ["kohm"] = "kohm",
                ["千欧"] = "kohm",
                ["mohm"] = "mohm",
                ["兆欧"] = "mohm"
            },
            UnitFactors = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
            FieldAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["宽"] = "宽度",
                ["宽度"] = "宽度",
                ["width"] = "宽度",
                ["厚"] = "厚度",
                ["厚度"] = "厚度",
                ["thickness"] = "厚度",
                ["电压"] = "电压",
                ["供电电压"] = "电压",
                ["voltage"] = "电压",
                ["工作电压"] = "电压",
                ["电流"] = "电流",
                ["工作电流"] = "电流",
                ["额定电流"] = "电流",
                ["current"] = "电流",
                ["功率"] = "功率",
                ["额定功率"] = "功率",
                ["功耗"] = "功率",
                ["power"] = "功率",
                ["频率"] = "频率",
                ["工作频率"] = "频率",
                ["frequency"] = "频率",
                ["气压"] = "压力",
                ["气压需求"] = "压力",
                ["长度"] = "长度",
                ["长"] = "长度",
                ["length"] = "长度",
                ["高度"] = "高度",
                ["高"] = "高度",
                ["height"] = "高度",
                ["压力"] = "压力",
                ["工作压力"] = "压力",
                ["pressure"] = "压力",
                ["扭矩"] = "扭矩",
                ["torque"] = "扭矩",
                ["转速"] = "转速",
                ["速度"] = "转速",
                ["speed"] = "转速",
                ["重量"] = "重量",
                ["质量"] = "重量",
                ["weight"] = "重量",
                ["精度"] = "精度",
                ["accuracy"] = "精度",
                ["温度"] = "温度",
                ["temperature"] = "温度",
                ["线宽"] = "线宽",
                ["linewidth"] = "线宽",
                ["制程"] = "制程",
                ["工艺节点"] = "制程",
                ["process"] = "制程",
                ["封装"] = "封装",
                ["package"] = "封装",
                ["引脚数"] = "引脚数",
                ["针脚数"] = "引脚数",
                ["pin"] = "引脚数"
            },
            ConflictPairs =
            [
                ("输入", "输出"),
                ("投板", "收板"),
                ("上料", "下料"),
                ("正转", "反转"),
                ("loading", "unloading"),
                ("loader", "unloader")
            ]
        };
    }
}
