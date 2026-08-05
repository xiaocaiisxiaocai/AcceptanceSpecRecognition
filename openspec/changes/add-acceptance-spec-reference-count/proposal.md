# Change: 增加验收规格引用次数

## Why

验收规格当前只能看到内容本身，无法判断某条规格在智能填充中被实际采用的频率，也无法区分历史高频规格与从未采用的规格。系统需要记录当前内容版本被最终采用的行次数，并在内容变化后重新计数，避免旧版本使用量误导用户。

## What Changes

- 为验收规格增加持久化的引用次数字段，历史记录默认从 0 开始。
- 智能填充成功提交时，按最终采用的行数原子累计引用次数；同一执行请求重放不得重复累计。
- 仅当被采用规格的验收或备注至少一项有有效内容时计数。
- 项目、规格、验收或备注发生实质变化时将引用次数重置为 0；仅空白和首尾空格归一化差异不重置。
- 验收规格列表与详情 API 返回引用次数，前端列表和详情同步展示。

## Impact

- Affected specs: `matching-engine`, `data-storage`, `api`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.*.cs`
  - `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecAppService.cs`
  - `src/AcceptanceSpecSystem.Application/Services/SmartFillSpecBackfillAppService.cs`
  - `src/AcceptanceSpecSystem.Data/Entities/AcceptanceSpec.cs`
  - `src/AcceptanceSpecSystem.Data/Migrations/`
  - `web/src/api/spec.ts`
  - `web/src/views/base-data/specs/components/SpecTable.vue`
- Database: 为 `AcceptanceSpecs` 增加非空 `BIGINT` 字段，默认值为 0，不回填历史使用次数。
- Compatibility: 仅向响应增加字段，不改变现有请求结构和匹配结果。
