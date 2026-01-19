using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Interfaces;

/// <summary>
/// 文档解析器接口（预留Excel扩展）
/// </summary>
public interface IDocumentParser
{
    /// <summary>
    /// 支持的文档类型
    /// </summary>
    DocumentType DocumentType { get; }

    /// <summary>
    /// 检查是否可以解析指定文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否可以解析</returns>
    bool CanParse(string filePath);

    /// <summary>
    /// 获取文档中所有表格的信息
    /// </summary>
    /// <param name="stream">文档流</param>
    /// <returns>表格信息列表</returns>
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(Stream stream);

    /// <summary>
    /// 从文件路径获取文档中所有表格的信息
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>表格信息列表</returns>
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(string filePath);

    /// <summary>
    /// 提取指定表格的数据
    /// </summary>
    /// <param name="stream">文档流</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="mapping">列映射配置（可选）</param>
    /// <returns>表格数据</returns>
    Task<TableData> ExtractTableDataAsync(Stream stream, int tableIndex, ColumnMapping? mapping = null);

    /// <summary>
    /// 从文件路径提取指定表格的数据
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="mapping">列映射配置（可选）</param>
    /// <returns>表格数据</returns>
    Task<TableData> ExtractTableDataAsync(string filePath, int tableIndex, ColumnMapping? mapping = null);

    /// <summary>
    /// 提取所有表格的数据
    /// </summary>
    /// <param name="stream">文档流</param>
    /// <returns>所有表格数据列表</returns>
    Task<IReadOnlyList<TableData>> ExtractAllTablesDataAsync(Stream stream);
}
