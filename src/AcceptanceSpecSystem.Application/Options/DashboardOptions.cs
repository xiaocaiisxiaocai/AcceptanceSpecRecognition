namespace AcceptanceSpecSystem.Application.Options;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// 仪表盘业务日历时区。默认使用系统部署所在业务的中国标准时间。
    /// 当前分组查询要求该时区不使用夏令时，以保持 SQLite/MySQL 查询语义一致。
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Shanghai";
}
