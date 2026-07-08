using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>
/// 表头字段默认识别词 catalog。运行期权威来源是数据库，这里只用于启动播种和恢复默认词。
/// </summary>
public static class ColumnMappingRuleDefaults
{
    private static readonly IReadOnlyDictionary<ColumnType, string[]> Defaults = new Dictionary<ColumnType, string[]>
    {
        [ColumnType.Project] =
        [
            "项目", "检验项目", "测试项目", "检测项目", "检查项目",
            "項目", "驗收項目", "檢驗項目", "測試項目", "檢測項目", "檢查項目",
            "检查对象", "檢查對象", "测试内容", "检测内容", "检验内容",
            "管控项", "管控項", "关键特性", "關鍵特性", "ctq",
            "item", "test item", "inspection item", "test content"
        ],
        [ColumnType.Specification] =
        [
            "规格", "规格内容", "规格要求", "标准", "要求", "技术标准", "技术要求", "检验标准",
            "規格", "驗收規格", "規格要求", "標準", "技術標準", "技術要求", "檢驗標準",
            "规范", "参数", "指标", "基准", "判定基准", "管制条件", "管控要点",
            "規範", "參數", "指標", "基準", "判定基準",
            "spec", "specification", "standard", "requirement", "criteria"
        ],
        [ColumnType.Acceptance] =
        [
            "验收", "验收方法", "验收标准", "验收方式", "判定", "判定标准",
            "结果", "测试结果", "检测结果", "判定结果", "检查方法", "检验方法", "测试方法",
            "驗收", "驗收方法", "驗收標準", "驗收方式", "判定標準",
            "設備商確認", "确认", "確認", "結果", "測試結果", "檢測結果", "判定結果",
            "檢查方法", "檢驗方法", "測試方法", "实测", "实测值", "实测结果",
            "實測", "實測值", "實測結果", "ok/ng", "合格判定",
            "acceptance", "result", "judgment", "actual", "pass/fail"
        ],
        [ColumnType.Remark] =
        [
            "备注", "说明", "注释", "补充", "补充说明", "附注",
            "備註", "說明", "註釋", "補充", "補充說明", "附註",
            "remark", "note", "comment", "description", "remarks"
        ]
    };

    public static IReadOnlyDictionary<ColumnType, IReadOnlyList<string>> GetAll() =>
        Defaults.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value
                .Select(word => word.Trim())
                .Where(word => word.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());

    public static IReadOnlyList<string> Get(ColumnType columnType) =>
        GetAll().TryGetValue(columnType, out var words) ? words : [];
}
