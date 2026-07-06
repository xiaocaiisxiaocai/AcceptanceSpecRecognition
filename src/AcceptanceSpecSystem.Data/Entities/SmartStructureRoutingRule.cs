namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 智能结构识别表格路由规则。
/// 用于由人工配置或学习信号判断表类型、推荐级别和跳过原因。
/// </summary>
public class SmartStructureRoutingRule
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TableKind { get; set; } = "Unknown";

    public string Recommendation { get; set; } = "NeedConfirm";

    public SmartStructureRoutingMatchScope MatchScope { get; set; } = SmartStructureRoutingMatchScope.Any;

    public SmartStructureRoutingMatchMode MatchMode { get; set; } = SmartStructureRoutingMatchMode.Contains;

    public string Pattern { get; set; } = string.Empty;

    public double Weight { get; set; } = 1;

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public SmartStructureRoutingRuleSource Source { get; set; } = SmartStructureRoutingRuleSource.Manual;

    public int? CustomerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public enum SmartStructureRoutingMatchScope
{
    Any = 1,
    TableName = 2,
    Headers = 3,
    SampleRows = 4
}

public enum SmartStructureRoutingMatchMode
{
    Contains = 1,
    Equals = 2,
    Regex = 3
}

public enum SmartStructureRoutingRuleSource
{
    BuiltinSeed = 1,
    Manual = 2,
    Learned = 3,
    AiSuggested = 4
}
