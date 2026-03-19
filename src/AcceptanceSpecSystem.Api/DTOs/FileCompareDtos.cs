using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 文件对比上传响应
/// </summary>
public class FileCompareUploadResponse
{
    public FileUploadResponse FileA { get; set; } = new();
    public FileUploadResponse FileB { get; set; } = new();
}

/// <summary>
/// 文件对比预览请求
/// </summary>
public class FileComparePreviewRequest
{
    public int FileIdA { get; set; }
    public int FileIdB { get; set; }
}

/// <summary>
/// 文件对比预览响应
/// </summary>
public class FileComparePreviewResponse
{
    public UploadedFileType FileType { get; set; }
    public List<FileCompareDiffItemDto> Items { get; set; } = new();
    public List<FileCompareHunkDto> Hunks { get; set; } = new();
    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
    public int ModifiedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>
/// 差异项
/// </summary>
public class FileCompareDiffItemDto
{
    public string DiffType { get; set; } = string.Empty;
    public FileCompareLocationDto Location { get; set; } = new();
    public string? OriginalText { get; set; }
    public string? CurrentText { get; set; }
    public string? DisplayLocation { get; set; }
}

/// <summary>
/// 差异位置
/// </summary>
public class FileCompareLocationDto
{
    public string DocumentType { get; set; } = string.Empty;
    public int? TableIndex { get; set; }
    public string? SheetName { get; set; }
    public int? RowIndex { get; set; }
    public int? ColumnIndex { get; set; }
    public string? Address { get; set; }
}

/// <summary>
/// 差异块（类 Git hunk）
/// </summary>
public class FileCompareHunkDto
{
    public int StartItemIndex { get; set; }
    public int EndItemIndex { get; set; }
    public string? RangeText { get; set; }
    public List<FileCompareHunkLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 差异块行
/// </summary>
public class FileCompareHunkLineDto
{
    public string LineType { get; set; } = string.Empty;
    public int ItemIndex { get; set; }
    public string? ChangeGroupId { get; set; }
    public string? DisplayLocation { get; set; }
    public string? OriginalText { get; set; }
    public string? CurrentText { get; set; }
}
