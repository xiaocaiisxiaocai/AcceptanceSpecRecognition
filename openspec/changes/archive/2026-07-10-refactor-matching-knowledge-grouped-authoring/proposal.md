# Change: 停用 matching-knowledge 分组式作者视图方案

## Why
当前分支已经移除 `matching-knowledge` 对外配置 API、草稿生成接口和前端配置页，因此这份“分组式作者视图” proposal 不再有落地入口。若继续保留，会与现行主规格和实际代码产生冲突。

## What Changes
- 明确分组式作者视图方案不再实施。
- 明确 `matching-knowledge` 不再提供任何对外作者模型，无论是字典式还是分组式。
- 将本 change 的文案统一收敛为“旧分组作者视图已取消，与现行移除方案保持一致”。

## Impact
- Affected specs: `user-interface`, `api`
- Affected code:
  - `web/src/views/config/matching-knowledge/index.vue`
  - `web/src/api/matching-knowledge.ts`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeComposition.cs`
