using Microsoft.AspNetCore.Mvc;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HistoryController : ControllerBase
{
    private readonly IMatchingEngine _matchingEngine;
    private readonly IAuditLogger _auditLogger;

    public HistoryController(IMatchingEngine matchingEngine, IAuditLogger auditLogger)
    {
        _matchingEngine = matchingEngine;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// 获取所有历史记录
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<HistoryRecord>>> GetAll([FromQuery] HistoryQueryParams? queryParams)
    {
        var records = await _matchingEngine.GetHistoryRecordsAsync();

        if (queryParams != null)
        {
            if (!string.IsNullOrWhiteSpace(queryParams.Search))
            {
                var search = queryParams.Search.ToLower();
                records = records.Where(r =>
                    (r.Project?.ToLower().Contains(search) ?? false) ||
                    (r.TechnicalSpec?.ToLower().Contains(search) ?? false) ||
                    (r.ActualSpec?.ToLower().Contains(search) ?? false) ||
                    (r.Remark?.ToLower().Contains(search) ?? false)
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Project))
            {
                records = records.Where(r => r.Project?.Contains(queryParams.Project) ?? false).ToList();
            }
        }

        return Ok(records);
    }

    /// <summary>
    /// 获取单条历史记录
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<HistoryRecord>> GetById(string id)
    {
        var records = await _matchingEngine.GetHistoryRecordsAsync();
        var record = records.FirstOrDefault(r => r.Id == id);

        if (record == null)
        {
            return NotFound("记录不存在");
        }

        return Ok(record);
    }

    /// <summary>
    /// 添加历史记录
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<HistoryRecord>> Create([FromBody] CreateHistoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Project))
        {
            return BadRequest("项目名称不能为空");
        }
        
        if (string.IsNullOrWhiteSpace(request.TechnicalSpec))
        {
            return BadRequest("技术指标不能为空");
        }

        var record = new HistoryRecord
        {
            Project = request.Project.Trim(),
            TechnicalSpec = request.TechnicalSpec.Trim(),
            ActualSpec = request.ActualSpec?.Trim() ?? "",
            Remark = request.Remark?.Trim() ?? ""
        };

        var created = await _matchingEngine.AddHistoryRecordAsync(record);

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "create_history",
            RecordId = created.Id,
            Details = $"创建历史记录: {created.Project}"
        });

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// 更新历史记录
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<HistoryRecord>> Update(string id, [FromBody] UpdateHistoryRequest request)
    {
        var records = await _matchingEngine.GetHistoryRecordsAsync();
        var existing = records.FirstOrDefault(r => r.Id == id);

        if (existing == null)
        {
            return NotFound("记录不存在");
        }

        // 验证必填字段
        var newProject = request.Project?.Trim() ?? existing.Project;
        var newTechnicalSpec = request.TechnicalSpec?.Trim() ?? existing.TechnicalSpec;

        if (string.IsNullOrWhiteSpace(newProject))
        {
            return BadRequest("项目名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(newTechnicalSpec))
        {
            return BadRequest("技术指标不能为空");
        }

        existing.Project = newProject;
        existing.TechnicalSpec = newTechnicalSpec;
        existing.ActualSpec = request.ActualSpec?.Trim() ?? existing.ActualSpec;
        existing.Remark = request.Remark?.Trim() ?? existing.Remark;

        await _matchingEngine.UpdateHistoryRecordAsync(existing);

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "update_history",
            RecordId = id,
            Details = $"更新历史记录: {existing.Project}"
        });

        return Ok(existing);
    }

    /// <summary>
    /// 删除历史记录
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        var deleted = await _matchingEngine.DeleteHistoryRecordAsync(id);

        if (!deleted)
        {
            return NotFound("记录不存在");
        }

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "delete_history",
            RecordId = id,
            Details = $"删除历史记录: {id}"
        });

        return NoContent();
    }

    /// <summary>
    /// 批量删除历史记录
    /// </summary>
    [HttpPost("batch-delete")]
    public async Task<ActionResult> BatchDelete([FromBody] BatchDeleteRequest request)
    {
        if (request.Ids == null || request.Ids.Count == 0)
        {
            return BadRequest("请提供要删除的记录ID列表");
        }

        var deletedCount = await _matchingEngine.DeleteHistoryRecordsBatchAsync(request.Ids);

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "batch_delete_history",
            RecordId = "",
            Details = $"批量删除历史记录: {deletedCount} 条"
        });

        return Ok(new { deleted = deletedCount });
    }

    /// <summary>
    /// 初始化所有历史记录的向量（用于首次部署或数据迁移）
    /// </summary>
    [HttpPost("initialize-embeddings")]
    public async Task<ActionResult> InitializeEmbeddings()
    {
        var pendingCount = await _matchingEngine.GetRecordsWithoutEmbeddingCountAsync();

        if (pendingCount == 0)
        {
            return Ok(new { message = "所有记录已有向量，无需初始化", initialized = 0 });
        }

        var initialized = await _matchingEngine.InitializeEmbeddingsAsync();

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "initialize_embeddings",
            RecordId = "",
            Details = $"初始化了 {initialized} 条记录的向量"
        });

        return Ok(new { message = $"成功初始化 {initialized} 条记录的向量", initialized });
    }

    /// <summary>
    /// 获取向量初始化状态
    /// </summary>
    [HttpGet("embedding-status")]
    public async Task<ActionResult> GetEmbeddingStatus()
    {
        var records = await _matchingEngine.GetHistoryRecordsAsync();
        var totalCount = records.Count;
        var pendingCount = await _matchingEngine.GetRecordsWithoutEmbeddingCountAsync();

        return Ok(new
        {
            total = totalCount,
            initialized = totalCount - pendingCount,
            pending = pendingCount,
            ready = pendingCount == 0
        });
    }

    /// <summary>
    /// 强制重新生成所有历史记录的向量
    /// </summary>
    [HttpPost("regenerate-embeddings")]
    public async Task<ActionResult> RegenerateEmbeddings()
    {
        var regenerated = await _matchingEngine.RegenerateAllEmbeddingsAsync();
        
        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = "regenerate_embeddings",
            RecordId = "",
            Details = $"重新生成了 {regenerated} 条记录的向量"
        });
        
        return Ok(new { message = $"成功重新生成 {regenerated} 条记录的向量", regenerated });
    }
}

public class HistoryQueryParams
{
    public string? Search { get; set; }
    public string? Project { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CreateHistoryRequest
{
    public string? Project { get; set; }
    public string? TechnicalSpec { get; set; }
    public string? ActualSpec { get; set; }
    public string? Remark { get; set; }
}

public class UpdateHistoryRequest
{
    public string? Project { get; set; }
    public string? TechnicalSpec { get; set; }
    public string? ActualSpec { get; set; }
    public string? Remark { get; set; }
}

public class BatchDeleteRequest
{
    public List<string>? Ids { get; set; }
}
