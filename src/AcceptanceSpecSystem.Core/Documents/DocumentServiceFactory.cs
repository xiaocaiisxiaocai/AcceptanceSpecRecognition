using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Parsers;
using AcceptanceSpecSystem.Core.Documents.Writers;

namespace AcceptanceSpecSystem.Core.Documents;

/// <summary>
/// 文档服务工厂
/// </summary>
public class DocumentServiceFactory
{
    private readonly List<IDocumentParser> _parsers;
    private readonly List<IDocumentWriter> _writers;

    /// <summary>
    /// 创建文档服务工厂实例
    /// </summary>
    public DocumentServiceFactory()
    {
        _parsers = new List<IDocumentParser>
        {
            new WordDocumentParser(),
            new ExcelDocumentParser()
            // 后续可添加 ExcelDocumentParser
        };

        _writers = new List<IDocumentWriter>
        {
            new WordDocumentWriter()
            // 后续可添加 ExcelDocumentWriter
        };
    }

    /// <summary>
    /// 获取支持指定文件的解析器
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文档解析器，如果不支持则返回null</returns>
    public IDocumentParser? GetParser(string filePath)
    {
        return _parsers.FirstOrDefault(p => p.CanParse(filePath));
    }

    /// <summary>
    /// 获取支持指定文件的写入器
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文档写入器，如果不支持则返回null</returns>
    public IDocumentWriter? GetWriter(string filePath)
    {
        return _writers.FirstOrDefault(w => w.CanWrite(filePath));
    }

    /// <summary>
    /// 获取指定类型的解析器
    /// </summary>
    /// <param name="documentType">文档类型</param>
    /// <returns>文档解析器，如果不支持则返回null</returns>
    public IDocumentParser? GetParser(DocumentType documentType)
    {
        return _parsers.FirstOrDefault(p => p.DocumentType == documentType);
    }

    /// <summary>
    /// 获取指定类型的写入器
    /// </summary>
    /// <param name="documentType">文档类型</param>
    /// <returns>文档写入器，如果不支持则返回null</returns>
    public IDocumentWriter? GetWriter(DocumentType documentType)
    {
        return _writers.FirstOrDefault(w => w.DocumentType == documentType);
    }

    /// <summary>
    /// 获取所有支持的文档类型
    /// </summary>
    public IReadOnlyList<DocumentType> GetSupportedTypes()
    {
        return _parsers.Select(p => p.DocumentType).Distinct().ToList();
    }

    /// <summary>
    /// 检查文件是否受支持
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持解析该文件</returns>
    public bool IsSupported(string filePath)
    {
        return _parsers.Any(p => p.CanParse(filePath));
    }
}
