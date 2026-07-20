namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 文档模板中的一个连续逻辑数据区域。
/// </summary>
public class DocumentTemplateRegion
{
    public int Id { get; set; }

    public int DocumentTemplateId { get; set; }

    public int RegionIndex { get; set; }

    public string HeadersJson { get; set; } = "[]";

    public int HeaderRowIndex { get; set; }

    public int HeaderRowCount { get; set; } = 1;

    public int DataStartRowIndex { get; set; } = 1;

    public int? DataEndRowIndex { get; set; }

    public int? ProjectColumnIndex { get; set; }

    public int SpecificationColumnIndex { get; set; }

    public int? AcceptanceColumnIndex { get; set; }

    public int? RemarkColumnIndex { get; set; }

    public bool IsSpecificationOnly { get; set; }

    public DocumentTemplate DocumentTemplate { get; set; } = null!;
}
