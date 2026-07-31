namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 单次文档解析得到的表信息与完整表数据快照。
/// </summary>
public sealed class DocumentTableSnapshot
{
    public IReadOnlyList<TableInfo> Tables { get; init; } = [];

    public IReadOnlyList<TableData> TableData { get; init; } = [];
}
