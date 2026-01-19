using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能匹配API控制器
/// </summary>
[Route("api/matching")]
public class MatchingController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly ILogger<MatchingController> _logger;

    // 存储填充任务结果（生产环境应使用Redis或数据库）
    private static readonly ConcurrentDictionary<string, FillTaskResult> _fillTaskResults = new();

    /// <summary>
    /// 创建匹配控制器实例
    /// </summary>
    public MatchingController(
        IUnitOfWork unitOfWork,
        IMatchingService matchingService,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        ITextPreprocessingPipeline textPipeline,
        ILogger<MatchingController> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _textPipeline = textPipeline;
        _logger = logger;
    }

    /// <summary>
    /// 匹配预览
    /// </summary>
    /// <remarks>
    /// 对输入的文本列表进行匹配预览，返回每个项的最佳匹配结果和候选列表
    /// </remarks>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<MatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MatchPreviewResponse>>> Preview([FromBody] MatchPreviewRequest request)
    {
        // 兼容前端：如果未传Items，则尝试从文件表格提取项目/规格作为待匹配项
        if (request.Items == null || request.Items.Count == 0)
        {
            if (request.FileId.HasValue && request.TableIndex.HasValue)
            {
                if (!request.ProjectColumnIndex.HasValue || !request.SpecificationColumnIndex.HasValue)
                {
                    return Error<MatchPreviewResponse>(400, "请手动指定项目列与规格列索引");
                }

                var extracted = await ExtractMatchSourceItemsFromFileAsync(
                    request.FileId.Value,
                    request.TableIndex.Value,
                    request.ProjectColumnIndex.Value,
                    request.SpecificationColumnIndex.Value);

                if (extracted.Count == 0)
                {
                    return Error<MatchPreviewResponse>(400, "未从表格中提取到可匹配的项目/规格数据");
                }

                request.Items = extracted;
            }
            else
            {
                return Error<MatchPreviewResponse>(400, "待匹配文本列表不能为空");
            }
        }

        // 获取候选验收规格
        var candidates = await GetCandidatesAsync(request.CustomerId, request.ProcessId);
        if (candidates.Count == 0)
        {
            return Success(new MatchPreviewResponse
            {
                Items = request.Items.Select(item => new MatchPreviewItem
                {
                    RowIndex = item.RowIndex,
                    SourceProject = item.Project,
                    SourceSpecification = item.Specification,
                    BestMatch = null,
                    Candidates = []
                }).ToList(),
                TotalMatched = 0,
                HighConfidenceCount = 0,
                MediumConfidenceCount = 0,
                LowConfidenceCount = 0
            }, "没有找到可匹配的验收规格");
        }

        // 转换匹配配置
        var config = ConvertToMatchingConfig(request.Config);

        // 创建文本处理会话（按 TextProcessingConfig 开关做预处理）
        var tpSession = await _textPipeline.CreateSessionAsync();

        // 预处理候选项（项目/规格），确保 CombinedText 使用处理后的内容
        var processedCandidates = candidates.Select(c => new MatchCandidate
        {
            SpecId = c.SpecId,
            Project = tpSession.Process(c.Project),
            Specification = tpSession.Process(c.Specification),
            Acceptance = c.Acceptance,
            Embedding = c.Embedding
        }).ToList();

        // 执行匹配
        var previewItems = new List<MatchPreviewItem>();
        int highCount = 0, mediumCount = 0, lowCount = 0;

        foreach (var item in request.Items)
        {
            var processedProject = tpSession.Process(item.Project);
            var processedSpec = tpSession.Process(item.Specification);
            var sourceText = $"{processedProject} {processedSpec}".Trim();

            var matches = await _matchingService.FindMatchesAsync(sourceText, processedCandidates, config);

            var previewItem = new MatchPreviewItem
            {
                RowIndex = item.RowIndex,
                SourceProject = item.Project,
                SourceSpecification = item.Specification,
                BestMatch = matches.FirstOrDefault() != null ? ConvertToMatchResultDto(matches.First()) : null,
                Candidates = matches.Select(ConvertToMatchResultDto).ToList()
            };

            previewItems.Add(previewItem);

            // 统计置信度
            if (previewItem.BestMatch != null)
            {
                var score = previewItem.BestMatch.Score;
                if (score >= 0.8) highCount++;
                else if (score >= 0.6) mediumCount++;
                else lowCount++;
            }
        }

        var response = new MatchPreviewResponse
        {
            Items = previewItems,
            TotalMatched = previewItems.Count(i => i.HasMatch),
            HighConfidenceCount = highCount,
            MediumConfidenceCount = mediumCount,
            LowConfidenceCount = lowCount
        };

        _logger.LogInformation(
            "匹配预览完成: 共{Total}项, 匹配{Matched}项, 高置信度{High}, 中置信度{Medium}, 低置信度{Low}",
            request.Items.Count, response.TotalMatched, highCount, mediumCount, lowCount);

        return Success(response);
    }

    /// <summary>
    /// 执行填充
    /// </summary>
    /// <remarks>
    /// 根据匹配结果，将验收标准填充到源文件中，返回填充后的文件下载链接
    /// </remarks>
    [HttpPost("execute")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ExecuteFillResponse>>> ExecuteFill([FromBody] ExecuteFillRequest request)
    {
        if (request.Mappings == null || request.Mappings.Count == 0)
        {
            return Error<ExecuteFillResponse>(400, "填充映射不能为空");
        }

        var fileId = request.FileId ?? request.SourceFileId;
        var tableIndex = request.TableIndex ?? request.SourceTableIndex;

        if (!fileId.HasValue)
        {
            return Error<ExecuteFillResponse>(400, "源文件ID不能为空");
        }

        if (!tableIndex.HasValue)
        {
            return Error<ExecuteFillResponse>(400, "源表格索引不能为空");
        }

        // 获取源文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId.Value);
        if (wordFile == null)
        {
            return Error<ExecuteFillResponse>(400, "源文件不存在");
        }

        // 获取所有相关的验收规格
        var specIds = request.Mappings
            .Select(m => m.SpecId ?? m.SelectedSpecId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (specIds.Count == 0)
        {
            return Error<ExecuteFillResponse>(400, "未提供有效的验收规格ID");
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => specIds.Contains(s.Id));
        var specDict = specs.ToDictionary(s => s.Id);

        // 获取文档解析器
        var parser = _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return Error<ExecuteFillResponse>(500, "文档解析器不可用");
        }

        // 提取表格数据
        TableData tableData;
        using (var stream = OpenWordFileReadStream(wordFile))
        {
            try
            {
                var mapping = new ColumnMapping
                {
                    HeaderRowIndex = 0,
                    DataStartRowIndex = 1
                };
                tableData = await parser.ExtractTableDataAsync(stream, tableIndex.Value, mapping);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Error<ExecuteFillResponse>(400, "表格索引超出范围");
            }
        }

        // 列索引必须由用户手动指定（不做关键字推断）
        if (!request.AcceptanceColumnIndex.HasValue)
        {
            return Error<ExecuteFillResponse>(400, "请手动指定验收列索引");
        }
        var acceptanceColumnIndex = request.AcceptanceColumnIndex.Value;
        var remarkColumnIndex = request.RemarkColumnIndex;

        // 执行填充
        int filledCount = 0;
        int skippedCount = 0;
        var fillResults = new List<FillResult>();

        foreach (var fillMapping in request.Mappings)
        {
            var selectedSpecId = (fillMapping.SpecId ?? fillMapping.SelectedSpecId) ?? 0;
            if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
            {
                skippedCount++;
                continue;
            }

            // 记录填充信息
            fillResults.Add(new FillResult
            {
                RowIndex = fillMapping.RowIndex,
                SpecId = spec.Id,
                Acceptance = spec.Acceptance ?? "",
                Remark = spec.Remark
            });
            filledCount++;
        }

        // 生成任务ID
        var taskId = Guid.NewGuid().ToString("N");

        // 存储填充结果（包含原文件和填充信息）
        _fillTaskResults[taskId] = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = fileId.Value,
            SourceTableIndex = tableIndex.Value,
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex,
            FillResults = fillResults,
            CreatedAt = DateTime.Now
        };

        // 清理过期的任务结果（保留1小时）
        CleanupExpiredTasks();

        var response = new ExecuteFillResponse
        {
            TaskId = taskId,
            FilledCount = filledCount,
            SkippedCount = skippedCount,
            DownloadUrl = $"/api/matching/download/{taskId}"
        };

        _logger.LogInformation(
            "执行填充完成: 任务{TaskId}, 填充{Filled}行, 跳过{Skipped}行",
            taskId, filledCount, skippedCount);

        return Success(response, $"填充完成：已填充{filledCount}行，跳过{skippedCount}行");
    }

    /// <summary>
    /// 下载填充结果
    /// </summary>
    [HttpGet("download/{taskId}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string taskId)
    {
        if (!_fillTaskResults.TryGetValue(taskId, out var taskResult))
        {
            return NotFound(ApiResponse.Error(404, "任务不存在或已过期"));
        }

        // 获取源文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(taskResult.SourceFileId);
        if (wordFile == null)
        {
            return NotFound(ApiResponse.Error(404, "源文件不存在"));
        }

        // 获取文档写入器
        var writer = _documentServiceFactory.GetWriter(DocumentType.Word);
        if (writer == null)
        {
            return BadRequest(ApiResponse.Error(500, "文档写入器不可用"));
        }

        // 构建写入操作列表
        var operations = new List<CellWriteOperation>();

        foreach (var r in taskResult.FillResults)
        {
            operations.Add(new CellWriteOperation
            {
                RowIndex = r.RowIndex,
                ColumnIndex = taskResult.AcceptanceColumnIndex ?? 0,
                Value = r.Acceptance,
                PreserveFormatting = true
            });

            if (taskResult.RemarkColumnIndex.HasValue && !string.IsNullOrWhiteSpace(r.Remark))
            {
                // 避免与验收列重复写
                if (taskResult.RemarkColumnIndex.Value != (taskResult.AcceptanceColumnIndex ?? 0))
                {
                    operations.Add(new CellWriteOperation
                    {
                        RowIndex = r.RowIndex,
                        ColumnIndex = taskResult.RemarkColumnIndex.Value,
                        Value = r.Remark!,
                        PreserveFormatting = true
                    });
                }
            }
        }

        // 复制原文件并执行写入
        byte[] resultContent;
        using (var resultStream = new MemoryStream())
        {
            // 复制原文件到可写流（优先文件系统，缺失时回退DB二进制）
            await using (var sourceStream = OpenWordFileReadStream(wordFile))
            {
                await sourceStream.CopyToAsync(resultStream);
            }
            resultStream.Position = 0;

            try
            {
                // 执行写入操作
                await writer.WriteTableDataAsync(resultStream, taskResult.SourceTableIndex, operations);
                resultContent = resultStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "填充文档失败: {TaskId}", taskId);
                return BadRequest(ApiResponse.Error(500, $"填充文档失败: {ex.Message}"));
            }
        }

        // 落盘保存填充结果（文件系统存储）
        try
        {
            var relative = await _fileStorage.SaveFilledWordAsync(wordFile.FileName, resultContent);
            taskResult.FilledFilePath = relative;
        }
        catch (Exception ex)
        {
            // 不影响下载（只记录日志）
            _logger.LogWarning(ex, "保存填充结果到文件系统失败: {TaskId}", taskId);
        }

        // 写入操作历史（填充）
        try
        {
            var history = new OperationHistory
            {
                OperationType = OperationType.Fill,
                TargetFile = taskResult.FilledFilePath,
                Details = JsonSerializer.Serialize(new
                {
                    taskId,
                    sourceFileId = taskResult.SourceFileId,
                    sourceTableIndex = taskResult.SourceTableIndex,
                    filledCount = taskResult.FillResults.Count,
                    acceptanceColumnIndex = taskResult.AcceptanceColumnIndex,
                    remarkColumnIndex = taskResult.RemarkColumnIndex
                }),
                CanUndo = false,
                CreatedAt = DateTime.Now
            };
            await _unitOfWork.OperationHistories.AddAsync(history);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入填充操作历史失败: {TaskId}", taskId);
        }

        // 生成下载文件名
        var originalFileName = Path.GetFileNameWithoutExtension(wordFile.FileName);
        var downloadFileName = $"{originalFileName}_filled_{DateTime.Now:yyyyMMddHHmmss}.docx";

        _logger.LogInformation("下载填充结果: 任务{TaskId}, 文件{FileName}", taskId, downloadFileName);

        return File(resultContent, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", downloadFileName);
    }

    /// <summary>
    /// 计算两个文本的相似度
    /// </summary>
    [HttpPost("similarity")]
    [ProducesResponseType(typeof(ApiResponse<SimilarityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SimilarityResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SimilarityResponse>>> ComputeSimilarity([FromBody] SimilarityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text1) || string.IsNullOrWhiteSpace(request.Text2))
        {
            return Error<SimilarityResponse>(400, "文本不能为空");
        }

        var tpSession = await _textPipeline.CreateSessionAsync();
        var t1 = tpSession.Process(request.Text1);
        var t2 = tpSession.Process(request.Text2);

        var config = ConvertToMatchingConfig(request.Config);
        var scores = await _matchingService.ComputeSimilarityAsync(t1, t2, config);

        var response = new SimilarityResponse
        {
            TotalScore = scores.TryGetValue("Total", out var total) ? total : 0,
            Scores = scores
        };

        return Success(response);
    }

    /// <summary>
    /// 获取候选验收规格列表
    /// </summary>
    private async Task<List<MatchCandidate>> GetCandidatesAsync(int? customerId, int? processId)
    {
        IEnumerable<Data.Entities.AcceptanceSpec> specs;

        // 优先按“客户 + 制程”组合筛选（即“一整份验规”的范围）
        if (customerId.HasValue && processId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s =>
                s.CustomerId == customerId.Value && s.ProcessId == processId.Value);
        }
        else if (customerId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.CustomerId == customerId.Value);
        }
        else if (processId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.ProcessId == processId.Value);
        }
        else
        {
            specs = await _unitOfWork.AcceptanceSpecs.GetAllAsync();
        }

        return specs.Select(s => new MatchCandidate
        {
            SpecId = s.Id,
            Project = s.Project,
            Specification = s.Specification,
            Acceptance = s.Acceptance
        }).ToList();
    }

    /// <summary>
    /// 转换为匹配配置
    /// </summary>
    private static MatchingConfig ConvertToMatchingConfig(MatchConfigDto? dto)
    {
        if (dto == null)
        {
            return new MatchingConfig();
        }

        return new MatchingConfig
        {
            UseLevenshtein = dto.UseLevenshtein,
            LevenshteinWeight = dto.LevenshteinWeight,
            UseJaccard = dto.UseJaccard,
            JaccardWeight = dto.JaccardWeight,
            UseCosine = dto.UseCosine,
            CosineWeight = dto.CosineWeight,
            MinScoreThreshold = dto.MinScoreThreshold,
            MaxResults = dto.MaxCandidates
        };
    }

    /// <summary>
    /// 转换为匹配结果DTO
    /// </summary>
    private static MatchResultDto ConvertToMatchResultDto(MatchResult result)
    {
        return new MatchResultDto
        {
            SpecId = result.MatchedSpecId ?? 0,
            Project = result.MatchedProject ?? "",
            Specification = result.MatchedSpecification ?? "",
            Acceptance = result.MatchedAcceptance,
            Score = result.Score,
            ScoreDetails = result.ScoreDetails
        };
    }

    private async Task<List<MatchSourceItem>> ExtractMatchSourceItemsFromFileAsync(
        int fileId,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId);
        if (wordFile == null)
        {
            return [];
        }

        var parser = _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return [];
        }

        using var stream = OpenWordFileReadStream(wordFile);
        TableData tableData;
        try
        {
            tableData = await parser.ExtractTableDataAsync(stream, tableIndex, new ColumnMapping
            {
                HeaderRowIndex = 0,
                DataStartRowIndex = 1
            });
        }
        catch
        {
            return [];
        }

        if (tableData.ColumnCount < 2)
        {
            return [];
        }

        if (projectColumnIndex < 0 || projectColumnIndex >= tableData.ColumnCount)
        {
            return [];
        }

        if (specificationColumnIndex < 0 || specificationColumnIndex >= tableData.ColumnCount)
        {
            return [];
        }

        // 提取数据行（rowIndex 使用文档中的真实行号，便于回写）
        var items = new List<MatchSourceItem>();
        foreach (var row in tableData.Rows)
        {
            var project = row.GetValue(projectColumnIndex) ?? "";
            var spec = row.GetValue(specificationColumnIndex) ?? "";

            if (string.IsNullOrWhiteSpace(project) && string.IsNullOrWhiteSpace(spec))
            {
                continue;
            }

            items.Add(new MatchSourceItem
            {
                RowIndex = row.Index,
                Project = project.Trim(),
                Specification = spec.Trim()
            });
        }

        return items;
    }

    /// <summary>
    /// 打开Word文件读取流：优先文件系统路径，缺失时回退到DB二进制（兼容旧数据）
    /// </summary>
    private Stream OpenWordFileReadStream(WordFile wordFile)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            if (System.IO.File.Exists(fullPath))
            {
                return System.IO.File.OpenRead(fullPath);
            }
        }

        if (wordFile.FileContent != null && wordFile.FileContent.Length > 0)
        {
            return new MemoryStream(wordFile.FileContent);
        }

        throw new InvalidOperationException("文件内容不可用（未找到物理文件且数据库内容为空）");
    }

    /// <summary>
    /// 清理过期的任务结果
    /// </summary>
    private static void CleanupExpiredTasks()
    {
        var expireTime = DateTime.Now.AddHours(-1);
        var expiredKeys = _fillTaskResults
            .Where(kvp => kvp.Value.CreatedAt < expireTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _fillTaskResults.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// 填充任务结果
/// </summary>
internal class FillTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public int SourceFileId { get; set; }
    public int SourceTableIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public List<FillResult> FillResults { get; set; } = [];
    public string? FilledFilePath { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 单行填充结果
/// </summary>
internal class FillResult
{
    public int RowIndex { get; set; }
    public int SpecId { get; set; }
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}
