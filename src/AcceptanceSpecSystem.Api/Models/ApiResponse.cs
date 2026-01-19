namespace AcceptanceSpecSystem.Api.Models;

/// <summary>
/// 统一API响应模型
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// 响应代码（0=成功，其他=错误码）
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 响应数据
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// 创建成功响应
    /// </summary>
    public static ApiResponse<T> Success(T? data, string message = "操作成功")
    {
        return new ApiResponse<T>
        {
            Code = 0,
            Message = message,
            Data = data
        };
    }

    /// <summary>
    /// 创建错误响应
    /// </summary>
    public static ApiResponse<T> Error(int code, string message)
    {
        return new ApiResponse<T>
        {
            Code = code,
            Message = message,
            Data = default
        };
    }
}

/// <summary>
/// 无数据的API响应
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    /// <summary>
    /// 创建成功响应
    /// </summary>
    public static ApiResponse Success(string message = "操作成功")
    {
        return new ApiResponse
        {
            Code = 0,
            Message = message,
            Data = null
        };
    }

    /// <summary>
    /// 创建错误响应
    /// </summary>
    public new static ApiResponse Error(int code, string message)
    {
        return new ApiResponse
        {
            Code = code,
            Message = message,
            Data = null
        };
    }
}
