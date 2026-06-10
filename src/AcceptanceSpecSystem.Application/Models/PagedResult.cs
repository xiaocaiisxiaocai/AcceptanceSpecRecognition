namespace AcceptanceSpecSystem.Application.Models;

/// <summary>
/// Application 层分页结果。
/// </summary>
public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = [];

    public int Total { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }
}
