using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 批量处理器接口
/// </summary>
public interface IBatchProcessor
{
    /// <summary>
    /// 开始批量处理（返回任务ID）
    /// </summary>
    Task<string> StartBatchAsync(List<MatchQuery> queries);
    
    /// <summary>
    /// 处理批量请求
    /// </summary>
    Task<BatchResult> ProcessBatchAsync(BatchRequest request);
    
    /// <summary>
    /// 获取批量处理进度
    /// </summary>
    BatchProgress? GetProgress(string taskId);
    
    /// <summary>
    /// 取消批量处理
    /// </summary>
    Task CancelAsync(string taskId);
    
    /// <summary>
    /// 取消批量处理任务
    /// </summary>
    bool CancelTask(string taskId);
    
    /// <summary>
    /// 获取批量处理结果
    /// </summary>
    Task<BatchResult?> GetResultAsync(string taskId);
}
