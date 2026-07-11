using System.IO.Compression;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class BatchReplyAppService
{
    public async Task<MatchingOperationResult<BatchReplyTablePreviewResponse>> TablePreviewAsync(
        BatchReplyUserContext user,
        BatchReplyTablePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TargetTable == null)
        {
            throw Failure(400, "目标表配置不能为空");
        }

        if (request.SourceTables == null || request.SourceTables.Count == 0)
        {
            throw Failure(400, "请至少配置一个来源表格");
        }

        var owner = ResolveOwnerForMatching(user);
        var session = GetSourceSessionForMatching(owner, request.SessionId);
        var targetFile = GetTargetFileForMatching(session, request.TargetId);
        // 预检阶段只使用临时文件对象，不直接污染会话里的原始上传记录。
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        var normalizedSourceConfigs = NormalizeTableConfigs(request.SourceTables);
        var sourceTableMetas = await _documentTableAccessService.GetTablesAsync(sourceFile, cancellationToken);
        var sourceTables = await BuildSourceTablesAsync(
            sourceFile,
            sourceTableMetas,
            normalizedSourceConfigs,
            cancellationToken);
        var sourceLookup = sourceTables.ToDictionary(table => table.TableIndex);

        var sourceTableIndex = ResolveSourceTableIndex(request.TargetTable);
        if (!sourceLookup.TryGetValue(sourceTableIndex, out var sourceTable))
        {
            throw Failure(400, $"来源表格{sourceTableIndex + 1}不存在或未配置");
        }

        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, targetFile.FileType!.Value, targetFile.RelativePath!);
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> targetTables;
        try
        {
            targetTables = await _documentTableAccessService.GetTablesAsync(targetWordFile, cancellationToken);
        }
        catch (ApplicationServiceException ex)
        {
            throw Failure(ex.Code, ex.Message);
        }

        var validation = await ValidateTargetTableAsync(
            targetWordFile,
            targetTables,
            sourceTable,
            request.TargetTable,
            cancellationToken);

        return Result(new BatchReplyTablePreviewResponse
        {
            TargetId = targetFile.TargetId,
            FileName = targetFile.FileName,
            TableIndex = request.TargetTable.TableIndex,
            SourceTableIndex = sourceTableIndex,
            CanApply = validation.Errors.Count == 0,
            Errors = validation.Errors.ToList(),
            DuplicateGroups = validation.DuplicateGroups.ToList(),
            Rows = validation.WriteTable?.Rows
                .OrderBy(row => row.RowIndex)
                .Select(row => new BatchReplyTablePreviewRowDto
                {
                    RowIndex = row.RowIndex,
                    Project = row.Project,
                    Specification = row.Specification,
                    Acceptance = row.Acceptance,
                    Remark = row.Remark
                })
                .ToList() ?? []
        });
    }

    public async Task<MatchingOperationResult<BatchReplyPreviewResponse>> PreviewAsync(
        BatchReplyUserContext user,
        string sessionId,
        IReadOnlyCollection<BatchTableConfig> tableConfigs,
        IReadOnlyCollection<BatchReplyUploadDocument> targetFiles,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForMatching(user);
        if (tableConfigs == null || tableConfigs.Count == 0)
        {
            throw Failure(400, "请至少配置一个来源表格");
        }

        if (targetFiles == null || targetFiles.Count == 0)
        {
            throw Failure(400, "请至少上传一个目标文件");
        }

        var session = GetSourceSessionForMatching(owner, sessionId);
        var normalizedConfigs = NormalizeTableConfigs(tableConfigs);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        var sourceTableMetas = await _documentTableAccessService.GetTablesAsync(sourceFile, cancellationToken);
        var sourceTables = await BuildSourceTablesAsync(
            sourceFile,
            sourceTableMetas,
            normalizedConfigs,
            cancellationToken);

        var previewFiles = new List<BatchReplyTargetFile>();
        foreach (var targetFile in targetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            previewFiles.Add(await BuildPreviewTargetAsync(targetFile, session.SourceFileType, sourceTables, cancellationToken));
        }

        // 预检只判断“当前配置能否应用”，真正写回仍在执行阶段完成。
        await _batchReplySessionService.ReplacePreviewAsync(
            owner.UserId,
            owner.CompanyId,
            session.SessionId,
            sourceTables,
            previewFiles,
            cancellationToken);

        return Result(new BatchReplyPreviewResponse
        {
            SessionId = session.SessionId,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            Files = previewFiles.Select(file => new BatchReplyPreviewFileResult
            {
                TargetId = file.TargetId,
                FileName = file.FileName,
                CanApply = file.CanApply,
                Errors = file.Errors.ToList()
            }).ToList()
        }, previewFiles.Any(file => !file.CanApply)
            ? $"预检完成：可应用{previewFiles.Count(file => file.CanApply)}份，不可应用{previewFiles.Count(file => !file.CanApply)}份"
            : $"预检完成：可应用{previewFiles.Count}份");
    }


    private async Task<List<BatchReplySourceTable>> BuildSourceTablesAsync(
        WordFile sourceFile,
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> sourceTableMetas,
        IReadOnlyCollection<BatchTableConfig> tableConfigs,
        CancellationToken cancellationToken)
    {
        var sourceTables = new List<BatchReplySourceTable>();
        foreach (var config in tableConfigs.OrderBy(item => item.TableIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateTableConfig(sourceFile.FileType, sourceTableMetas, config);
            var rows = await _documentTableAccessService.ExtractReplySourceItemsAsync(
                sourceFile,
                config,
                cancellationToken);
            if (rows.Count == 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}没有可回复的数据");
            }

            sourceTables.Add(new BatchReplySourceTable
            {
                TableIndex = config.TableIndex,
                ProjectColumnIndex = config.ProjectColumnIndex,
                SpecificationColumnIndex = config.SpecificationColumnIndex,
                AcceptanceColumnIndex = config.AcceptanceColumnIndex,
                RemarkColumnIndex = config.RemarkColumnIndex,
                HeaderRowStart = config.HeaderRowStart,
                HeaderRowCount = config.HeaderRowCount,
                DataStartRow = config.DataStartRow,
                FilterEmptySourceRows = config.FilterEmptySourceRows ?? true,
                DuplicateResolutions = config.DuplicateResolutions
                    .Where(item => !string.IsNullOrWhiteSpace(item.GroupId))
                    .Select(item => new BatchReplyDuplicateResolutionDto
                    {
                        GroupId = item.GroupId.Trim(),
                        Strategy = item.Strategy?.Trim() ?? string.Empty
                    })
                    .ToList(),
                Rows = rows.Select(row => new BatchReplySourceRow
                {
                    RowIndex = row.RowIndex,
                    Project = row.Project,
                    Specification = row.Specification,
                    Acceptance = row.Acceptance,
                    Remark = row.Remark
                }).ToList()
            });
        }

        return sourceTables;
    }

    private async Task<BatchReplyTargetFile> BuildPreviewTargetAsync(
        BatchReplyUploadDocument targetFile,
        UploadedFileType expectedFileType,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables,
        CancellationToken cancellationToken)
    {
        var result = new BatchReplyTargetFile
        {
            TargetId = Guid.NewGuid().ToString("N"),
            FileName = targetFile.FileName
        };

        if (targetFile.Content.Length == 0)
        {
            result.Errors.Add("目标文件为空");
            return result;
        }

        var fileType = targetFile.FileType;
        result.FileType = fileType;

        var relativePath = await _batchReplySessionService.SaveTargetFileAsync(
            targetFile.FileName,
            fileType,
            targetFile.Content,
            cancellationToken);
        result.RelativePath = relativePath;

        if (fileType != expectedFileType)
        {
            result.Errors.Add("文件类型不一致");
            return result;
        }

        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, fileType, relativePath!);
        result.Errors = (await ValidateTargetFileAsync(targetWordFile, sourceTables, cancellationToken)).Errors;
        result.CanApply = result.Errors.Count == 0;
        return result;
    }

    private async Task<BatchReplyTargetValidationResult> ValidateTargetFileAsync(
        WordFile targetFile,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchReplyTargetValidationResult();
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> targetTables;
        try
        {
            targetTables = await _documentTableAccessService.GetTablesAsync(targetFile, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApplicationServiceException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取批量回复目标文件失败: {FileName}", targetFile.FileName);
            result.Errors.Add($"读取目标文件失败: {ex.Message}");
            return result;
        }

        foreach (var sourceTable in sourceTables.OrderBy(table => table.TableIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var legacyTargetConfig = new BatchTableConfig
            {
                TableIndex = sourceTable.TableIndex,
                ProjectColumnIndex = sourceTable.ProjectColumnIndex,
                SpecificationColumnIndex = sourceTable.SpecificationColumnIndex,
                AcceptanceColumnIndex = sourceTable.AcceptanceColumnIndex,
                RemarkColumnIndex = sourceTable.RemarkColumnIndex,
                HeaderRowStart = sourceTable.HeaderRowStart,
                HeaderRowCount = sourceTable.HeaderRowCount,
                DataStartRow = sourceTable.DataStartRow,
                FilterEmptySourceRows = sourceTable.FilterEmptySourceRows
            };

            var tableValidation = await ValidateTargetTableAsync(
                targetFile,
                targetTables,
                sourceTable,
                legacyTargetConfig,
                cancellationToken);
            result.Errors.AddRange(tableValidation.Errors);
            if (tableValidation.WriteTable != null)
            {
                result.WriteTables.Add(tableValidation.WriteTable);
            }
        }

        return result;
    }

    private async Task<BatchReplyTargetTableValidationResult> ValidateTargetTableAsync(
        WordFile targetFile,
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> targetTables,
        BatchReplySourceTable sourceTable,
        BatchTableConfig targetConfig,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTableConfig(targetFile.FileType, targetTables, targetConfig);

        var result = new BatchReplyTargetTableValidationResult();
        var targetRows = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
            targetFile,
            targetConfig.TableIndex,
            targetConfig.ProjectColumnIndex,
            targetConfig.SpecificationColumnIndex,
            targetConfig.HeaderRowStart,
            targetConfig.HeaderRowCount,
            targetConfig.DataStartRow,
            targetConfig.FilterEmptySourceRows ?? true,
            cancellationToken);

        var sourceLookupResult = BuildSourceRowLookup(sourceTable);
        if (sourceLookupResult.DuplicateGroups.Count > 0)
        {
            result.Errors.Add($"表格{sourceTable.TableIndex + 1}存在重复的项目/规格组合，请手动处理");
            result.DuplicateGroups.AddRange(sourceLookupResult.DuplicateGroups);
            return result;
        }

        var targetLookupResult = BuildTargetRowLookup(
            targetConfig.TableIndex,
            targetRows,
            targetConfig.DuplicateResolutions);
        if (targetLookupResult.DuplicateGroups.Count > 0)
        {
            result.Errors.Add($"表格{targetConfig.TableIndex + 1}存在重复的项目/规格组合，请手动处理");
            result.DuplicateGroups.AddRange(targetLookupResult.DuplicateGroups);
            return result;
        }

        var skippedKeys = new HashSet<(string Project, string Specification)>(sourceLookupResult.SkippedKeys);
        skippedKeys.UnionWith(targetLookupResult.SkippedKeys);

        var sourceLookup = sourceLookupResult.Lookup
            .Where(item => !skippedKeys.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value);
        var targetLookup = targetLookupResult.Lookup
            .Where(item => !skippedKeys.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value);

        if (sourceLookup.Count != targetLookup.Count ||
            sourceLookup.Keys.Except(targetLookup.Keys).Any() ||
            targetLookup.Keys.Except(sourceLookup.Keys).Any())
        {
            result.Errors.Add($"表格{targetConfig.TableIndex + 1}的项目/规格不一致");
            return result;
        }

        result.WriteTable = new BatchReplyWriteTable
        {
            TableIndex = targetConfig.TableIndex,
            AcceptanceColumnIndex = targetConfig.AcceptanceColumnIndex,
            RemarkColumnIndex = targetConfig.RemarkColumnIndex,
            Rows = targetLookup.Values
                .OrderBy(row => row.RowIndex)
                .Select(targetRow =>
            {
                var sourceRow = sourceLookup[BuildRowKey(targetRow.Project, targetRow.Specification)];
                return new BatchReplyWriteRow
                {
                    RowIndex = targetRow.RowIndex,
                    Project = targetRow.Project,
                    Specification = targetRow.Specification,
                    Acceptance = sourceRow.Acceptance,
                    Remark = sourceRow.Remark
                };
            }).ToList()
        };

        return result;
    }


    private static IReadOnlyCollection<BatchTableConfig> NormalizeTableConfigs(IReadOnlyCollection<BatchTableConfig> tableConfigs)
    {
        if (tableConfigs.Select(item => item.TableIndex).Distinct().Count() != tableConfigs.Count)
        {
            throw Failure(400, "表格配置存在重复");
        }

        return tableConfigs;
    }

    private static void ValidateTableConfig(
        UploadedFileType fileType,
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> tableMetas,
        BatchTableConfig config)
    {
        if (config.TableIndex < 0 || config.TableIndex >= tableMetas.Count)
        {
            throw Failure(400, $"表格{config.TableIndex + 1}不存在");
        }

        var requiredIndexes = new[]
        {
            config.ProjectColumnIndex,
            config.SpecificationColumnIndex,
            config.AcceptanceColumnIndex,
            config.RemarkColumnIndex ?? -1
        };
        if (requiredIndexes.Any(index => index < 0))
        {
            throw Failure(400, $"表格{config.TableIndex + 1}列配置不合法");
        }

        var tableMeta = tableMetas[config.TableIndex];
        if (requiredIndexes.Max() >= tableMeta.ColumnCount)
        {
            throw Failure(400, $"表格{config.TableIndex + 1}列配置超出来源文件范围");
        }

        if (fileType == UploadedFileType.ExcelXlsx)
        {
            if (config.HeaderRowCount.HasValue && config.HeaderRowCount.Value < 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}表头行数不合法");
            }

            if (config.HeaderRowStart.HasValue && config.HeaderRowStart.Value <= 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}表头起始行不合法");
            }

            if (config.DataStartRow.HasValue && config.DataStartRow.Value <= 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}数据起始行不合法");
            }
        }
    }

    private sealed class BatchReplyTargetValidationResult
    {
        public List<string> Errors { get; } = [];

        public List<BatchReplyWriteTable> WriteTables { get; } = [];
    }

    private sealed class BatchReplyTargetTableValidationResult
    {
        public List<string> Errors { get; } = [];

        public List<BatchReplyDuplicateGroupDto> DuplicateGroups { get; } = [];

        public BatchReplyWriteTable? WriteTable { get; set; }
    }
}
