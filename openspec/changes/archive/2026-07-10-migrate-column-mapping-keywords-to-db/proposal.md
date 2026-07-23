# Change: 表头字段识别词迁移至数据库并统一 Excel 前端识别

## Why
智能结构识别把某列判为 项目/规格/验收/备注 所依赖的表头关键词，目前硬编码在后端代码 4 处（`RuleBasedMappingStrategy.DefaultSynonyms`、`HeaderKeywordMatcher.BuiltInKeywords`、`SmartConfigurationTableRoutingService.HasStructureHeaderSignal`、`SmartConfigurationAppService.IsSpecificationOnlyCandidate`），业务无法维护。系统已有 `ColumnMappingRules` 表 + 配置页 + CRUD API，但表内无种子数据，硬编码词表用户看不见、改不了、删不掉。此外 Excel 导入的"自动识别字段行"用的是前端另一份词表（`dataImport.helpers.ts` 的 `scoreFieldHeader`），绕过后端与数据库，导致 Excel 与 Word 识别口径不一致。每遇到一份新格式的表头就需要改代码发版。

## What Changes
- 表头字段识别词由数据库 `ColumnMappingRules` 作为唯一来源；后端识别代码不再内置这些词表。
- 启动时幂等补齐内置（Builtin、全局）默认词：按字段 bootstrap——某字段无任何内置全局规则时播种该字段全部默认词，已存在则整组跳过（尊重用户增删/禁用）。
- 新增"恢复默认词"能力：`POST /api/column-mapping-rules/restore-defaults`（可选按 `targetField`），无需重启即可补齐缺失的内置默认词。
- Excel 导入"自动识别字段行"改为消费后端 `/api/smart-config/recognize` 的识别结果，删除前端 `scoreFieldHeader` 词表族死代码，Excel 与 Word 识别口径统一。
- 内容特征词/单位符号（`SpecificationLikelihoodScorer` 中判断"值像不像规格/表像不像验收表"的启发式，如 mm/kg/±/≤）性质不同，**保留在代码**，不在本次迁移范围。
- 迁库后表头词与现有自增长学习机制（`SmartConfigurationLearningService`，`/api/smart-config/confirm` 触发）同源于 `ColumnMappingRules`：启动内置播种 + AI 客户级学习 + AI 全局晋升 + 人工兜底四来源共存；启动补齐与 `restore-defaults` 仅动 `Builtin` 全局规则，不触碰 `Learned`/`Manual`/客户级（详见 design.md「与现有学习机制的协同」）。

## Impact
- Affected specs: `matching-engine`, `data-storage`, `user-interface`, `api`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Documents/Intelligence/*`（去硬编码 + 新增默认词 catalog）
  - `src/AcceptanceSpecSystem.Application/Services/SmartConfiguration*`（去硬编码、透传 DB 词）
  - `src/AcceptanceSpecSystem.Api/*`（新增 Initializer、启动播种、恢复默认端点）
  - `web/src/views/data-import/*`（Excel 识别改走后端、删死代码）
  - `web/src/views/config/column-mapping-rules/*`（恢复默认按钮）
  - `tests/*`、`web/tests/*`、`web/src/**/*.test.ts`
