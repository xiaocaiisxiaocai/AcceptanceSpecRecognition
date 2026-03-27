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
    /// 基准单位换算系数（归一到统一基准）
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
                ["delta"] = "台达",
                ["台达"] = "台达",
                ["foxconn"] = "富士康",
                ["富士康"] = "富士康"
            },
            UnitAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cm"] = "cm",
                ["厘米"] = "cm",
                ["mm"] = "mm",
                ["毫米"] = "mm",
                ["v"] = "v",
                ["伏"] = "v",
                ["volt"] = "v"
            },
            UnitFactors = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["mm"] = 1m,
                ["cm"] = 10m,
                ["v"] = 1m
            },
            FieldAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["宽"] = "宽度",
                ["宽度"] = "宽度",
                ["width"] = "宽度",
                ["电压"] = "电压",
                ["供电电压"] = "电压",
                ["voltage"] = "电压",
                ["长度"] = "长度",
                ["长"] = "长度",
                ["length"] = "长度",
                ["高度"] = "高度",
                ["高"] = "高度",
                ["height"] = "高度"
            },
            ConflictPairs =
            [
                ("输入", "输出"),
                ("投板", "收板"),
                ("loading", "unloading"),
                ("loader", "unloader")
            ]
        };
    }
}
