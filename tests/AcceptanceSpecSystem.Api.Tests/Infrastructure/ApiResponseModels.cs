namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public sealed class ApiResponse<T>
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

