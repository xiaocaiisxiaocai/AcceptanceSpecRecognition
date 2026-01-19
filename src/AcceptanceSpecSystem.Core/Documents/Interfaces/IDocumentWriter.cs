using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Interfaces;

/// <summary>
/// 文档写入器接口（预留Excel扩展）
/// </summary>
public interface IDocumentWriter
{
    /// <summary>
    /// 支持的文档类型
    /// </summary>
    DocumentType DocumentType { get; }

    /// <summary>
    /// 检查是否可以写入指定文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否可以写入</returns>
    bool CanWrite(string filePath);

    /// <summary>
    /// 写入表格数据到文档流
    /// </summary>
    /// <param name="stream">文档流（可读写）</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="operations">写入操作列表</param>
    /// <returns>成功写入的操作数量</returns>
    Task<int> WriteTableDataAsync(Stream stream, int tableIndex, IEnumerable<CellWriteOperation> operations);

    /// <summary>
    /// 写入表格数据到文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="operations">写入操作列表</param>
    /// <returns>成功写入的操作数量</returns>
    Task<int> WriteTableDataAsync(string filePath, int tableIndex, IEnumerable<CellWriteOperation> operations);

    /// <summary>
    /// 写入单个单元格
    /// </summary>
    /// <param name="stream">文档流（可读写）</param>
    /// <param name="tableIndex">表格索引</param>
    /// <param name="rowIndex">行索引</param>
    /// <param name="columnIndex">列索引</param>
    /// <param name="value">要写入的值</param>
    /// <returns>是否写入成功</returns>
    Task<bool> WriteCellAsync(Stream stream, int tableIndex, int rowIndex, int columnIndex, string value);

    /// <summary>
    /// 创建文档副本并写入数据
    /// </summary>
    /// <param name="sourceFilePath">源文件路径</param>
    /// <param name="targetFilePath">目标文件路径</param>
    /// <param name="tableIndex">表格索引</param>
    /// <param name="operations">写入操作列表</param>
    /// <returns>成功写入的操作数量</returns>
    Task<int> WriteToNewFileAsync(string sourceFilePath, string targetFilePath, int tableIndex, IEnumerable<CellWriteOperation> operations);
}
