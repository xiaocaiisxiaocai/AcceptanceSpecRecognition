namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// Word文件存储实体
/// </summary>
public class WordFile
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 原始文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型（用于区分 Word/Excel）
    /// </summary>
    public UploadedFileType FileType { get; set; } = UploadedFileType.WordDocx;

    /// <summary>
    /// 文件内容（二进制）- 旧存储方式（兼容保留）。新实现优先使用 <see cref="FilePath"/>。
    /// </summary>
    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 文件在服务器文件系统中的相对路径（例如：uploads/word-files/2026-01-13/xxx.docx）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 文件哈希值（用于检测重复）
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 上传时间
    /// </summary>
    public DateTime UploadedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：从该文件导入的所有验收规格
    /// </summary>
    public ICollection<AcceptanceSpec> AcceptanceSpecs { get; set; } = new List<AcceptanceSpec>();
}
