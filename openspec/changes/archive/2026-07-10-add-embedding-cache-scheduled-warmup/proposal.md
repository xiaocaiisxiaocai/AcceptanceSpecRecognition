# Change: 增加 Embedding 缓存定时预热

## Why
历史验收规格首次参与智能填充或 AI 语义搜索时，系统会在用户请求链路中补齐候选向量，导致首次预览耗时偏高。当前 `EmbeddingCache` 仅按 `SpecId + ModelName` 区分缓存，而匹配、语义搜索使用的文本范围不同，存在错误复用风险。

## What Changes
- 增加后台定时任务，在低峰时段批量补齐缺失的验收规格 Embedding 缓存。
- 为 Embedding 缓存增加用途与文本指纹边界，区分智能匹配、语义搜索、导入重复识别等不同向量文本。
- 规格项目、规格、验收或备注变更时，使受影响缓存失效或重新生成。
- 保留现有懒生成兜底：定时任务未完成时，智能填充和语义搜索仍可按需生成缓存。
- 后台预热失败只记录日志，不影响导入、匹配和系统启动。

## Impact
- Affected specs: `architecture`, `data-storage`, `matching-engine`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Services/*Embedding*`
  - `src/AcceptanceSpecSystem.Api/Options/*`
  - `src/AcceptanceSpecSystem.Api/Program.cs`
  - `src/AcceptanceSpecSystem.Data/Entities/EmbeddingCache.cs`
  - `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/EmbeddingCacheRepository.cs`
  - `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecAppService.cs`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
  - `tests/AcceptanceSpecSystem.Data.Tests/*`
