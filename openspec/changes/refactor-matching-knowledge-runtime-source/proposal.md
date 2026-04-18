# Change: 移除 matching-knowledge 对外配置 API

## Why
当前分支已经把匹配知识收敛为匹配引擎内部运行时能力，并删除了 `/api/matching-knowledge`、草稿生成页及前端配置入口。若继续保留“数据库唯一运行时来源 + 对外可编辑 API”这套 proposal，会与现行代码、测试和主规格产生直接冲突，后续维护者也容易被误导回旧方向。

## What Changes
- 明确 `matching-knowledge` 不再作为对外配置能力存在。
- 明确 `/api/matching-knowledge`、`/api/matching-knowledge/drafts/generate` 及相关派生接口已移除。
- 明确匹配知识仅保留为匹配引擎内部运行时知识，不再承诺数据库单例配置页、恢复默认、清空当前配置等作者语义。
- 将此前 proposal / design / tasks 中仍指向 `GET/PUT /api/matching-knowledge` 的内容统一收口为“已移除旧接口与旧页面”。

## Impact
- Affected specs:
  - `api`
  - `user-interface`
  - `data-storage`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeDraftsController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/ConfigurationMatchingKnowledgeProvider.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeBootstrapper.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeComposition.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeDraftGenerationService.cs`
  - `web/src/api/matching-knowledge.ts`
  - `web/src/views/config/matching-knowledge/index.vue`

## Expected Outcome
- 现行 OpenSpec 不再暗示存在可维护的 matching-knowledge API。
- 后续开发不会再被旧 proposal 引回“恢复页面和接口”的方向。
- 运行时匹配知识的事实来源仅保留在匹配引擎内部约定中。
