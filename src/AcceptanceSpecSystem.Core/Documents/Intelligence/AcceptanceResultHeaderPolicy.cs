namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>
/// 统一区分验收方法列与最终结果列。
/// </summary>
public static class AcceptanceResultHeaderPolicy
{
    private static readonly string[] AcceptanceContexts =
    [
        "验收", "驗收", "检查", "檢查", "测试", "測試"
    ];

    private static readonly string[] MethodSignals = ["方法", "方式"];

    private static readonly string[] ResultSignals =
    [
        "确认", "確認", "结果", "結果", "结论", "結論", "判定", "OK", "NG"
    ];

    public static bool IsAcceptanceMethodHeader(string? header)
    {
        var text = header?.Trim() ?? string.Empty;
        return text.Length > 0 &&
               AcceptanceContexts.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) &&
               MethodSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) &&
               !HasAcceptanceResultSignal(text);
    }

    public static bool HasAcceptanceResultSignal(string? header)
    {
        var text = header?.Trim() ?? string.Empty;
        return text.Length > 0 &&
               ResultSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}
