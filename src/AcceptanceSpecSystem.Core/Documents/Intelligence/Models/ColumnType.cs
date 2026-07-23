namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 列类型枚举
/// </summary>
public enum ColumnType
{
    /// <summary>未知</summary>
    Unknown = 0,

    /// <summary>项目列（检验项目、测试项目）</summary>
    Project = 1,

    /// <summary>规格列（技术标准、要求）</summary>
    Specification = 2,

    /// <summary>验收列（判定结果、OK/NG）</summary>
    Acceptance = 3,

    /// <summary>备注列（补充说明）</summary>
    Remark = 4
}
