using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>
/// 文档智能识别服务
/// </summary>
public interface IDocumentIntelligenceService
{
    /// <summary>
    /// 识别目标表格
    /// </summary>
    /// <param name="tables">所有表格信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表格识别结果</returns>
    Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 识别列映射
    /// </summary>
    /// <param name="tableData">表格数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>列映射识别结果</returns>
    Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 自动配置（一站式：表格识别 + 列映射识别）
    /// </summary>
    /// <param name="tables">所有表格信息</param>
    /// <param name="tablesData">所有表格数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>自动配置结果</returns>
    Task<AutoConfigResult> AutoConfigureAsync(
        IReadOnlyList<TableInfo> tables,
        IReadOnlyList<TableData> tablesData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检测表头行位置
    /// </summary>
    /// <param name="tableData">表格数据</param>
    /// <returns>表头行索引（0-based）</returns>
    int DetectHeaderRowIndex(TableData tableData);
}
